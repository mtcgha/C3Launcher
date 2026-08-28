using System.Threading.Channels;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace C3Launcher.Core.Docker;

/// <summary>
/// Container events for our own sessions. wt.exe exits as soon as it hands the
/// request to the running Windows Terminal, so process exit says nothing about
/// session lifetime — the container 'die' event is the real signal.
/// </summary>
public sealed class DockerEventMonitor : IAsyncDisposable
{
    private readonly IDockerClient _client;
    private readonly Channel<Message> _channel = Channel.CreateUnbounded<Message>();
    private readonly CancellationTokenSource _cts = new();
    private Task? _pump;

    public DockerEventMonitor(IDockerClient client) => _client = client;

    public IAsyncEnumerable<Message> Events => _channel.Reader.ReadAllAsync();

    public void Start()
    {
        _pump ??= Task.Run(() => PumpAsync(_cts.Token));
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        var parameters = new ContainerEventsParameters
        {
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["type"] = new Dictionary<string, bool> { ["container"] = true },
                ["label"] = new Dictionary<string, bool> { [SessionLabels.SessionId] = true },
            },
        };

        var progress = new CollectingProgress<Message>(message => _channel.Writer.TryWrite(message));

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _client.System.MonitorEventsAsync(parameters, progress, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Daemon restart or a dropped pipe: back off and re-subscribe.
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _channel.Writer.TryComplete();
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();

        if (_pump is not null)
        {
            try
            {
                await _pump;
            }
            catch (OperationCanceledException) { }
        }

        _cts.Dispose();
    }
}
