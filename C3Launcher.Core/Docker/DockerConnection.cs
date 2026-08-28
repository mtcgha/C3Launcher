using Docker.DotNet;

namespace C3Launcher.Core.Docker;

public sealed class DockerConnection : IDisposable
{
    private DockerConnection(IDockerClient client) => Client = client;

    public IDockerClient Client { get; }

    /// <summary>
    /// With no endpoint the builder resolves the same way the docker CLI does:
    /// DOCKER_HOST, then the active context, then the platform default pipe/socket.
    /// </summary>
    public static DockerConnection Create(Uri? endpoint = null)
    {
        var builder = new DockerClientBuilder().WithTimeout(TimeSpan.FromMinutes(10));
        if (endpoint is not null)
            builder = builder.WithEndpoint(endpoint);

        return new DockerConnection(builder.Build());
    }

    /// <summary>Returns null when the daemon is reachable, otherwise a message for the UI.</summary>
    public async Task<string?> ProbeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await Client.System.PingAsync(cancellationToken);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return $"Could not reach the Docker daemon. Is Docker Desktop running? ({ex.Message})";
        }
    }

    public void Dispose() => Client.Dispose();
}
