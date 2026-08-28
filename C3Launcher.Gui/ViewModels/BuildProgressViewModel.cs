using Avalonia.Threading;
using C3Launcher.Core.Docker;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace C3Launcher.Gui.ViewModels;

public partial class BuildProgressViewModel : ViewModelBase
{
    private readonly ImageBuildService _builder;
    private readonly string _image;
    private readonly string? _claudeVersion;
    private readonly CancellationTokenSource _cts = new();
    private readonly DispatcherTimer _timer;
    private DateTime _startedUtc;

    public BuildProgressViewModel(ImageBuildService builder, string image, string? claudeVersion)
    {
        _builder = builder;
        _image = image;
        _claudeVersion = claudeVersion;

        Headline = claudeVersion is null
            ? $"Rebuilding {image}"
            : $"Rebuilding {image} with Claude Code {claudeVersion}";

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Elapsed = SessionItemViewModel.FormatUptime(DateTime.UtcNow - _startedUtc);
    }

    public ObservableCollection<string> Lines { get; } = [];

    public string Headline { get; }

    [ObservableProperty]
    public partial string CurrentStep { get; set; } = "starting…";

    [ObservableProperty]
    public partial string Elapsed { get; set; } = "00:00:00";

    [ObservableProperty]
    public partial bool IsRunning { get; set; } = true;

    [ObservableProperty]
    public partial bool Succeeded { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    public string FooterNote => "Only the last layer rebuilds — everything above it is cached";

    public event Action<bool>? Finished;

    public async Task RunAsync()
    {
        _startedUtc = DateTime.UtcNow;
        _timer.Start();

        var progress = new Progress<string>(line =>
        {
            Lines.Add(line);

            if (Lines.Count > 2000)
                Lines.RemoveAt(0);

            if (line.StartsWith("Step ", StringComparison.Ordinal)
                || line.StartsWith("#", StringComparison.Ordinal))
            {
                CurrentStep = line;
            }
        });

        BuildOutcome outcome;
        try
        {
            outcome = await _builder.BuildAsync(_image, _claudeVersion, progress, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            outcome = new BuildOutcome(false, "Cancelled.");
        }

        _timer.Stop();
        IsRunning = false;
        Succeeded = outcome.Ok;
        Error = outcome.Error;
        CurrentStep = outcome.Ok ? "Build complete" : outcome.Error ?? "Build failed";

        Finished?.Invoke(outcome.Ok);
    }

    public void Cancel() => _cts.Cancel();
}
