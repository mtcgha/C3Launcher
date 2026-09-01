using System.Collections.ObjectModel;
using Avalonia.Threading;
using C3Launcher.Core.Compose;
using C3Launcher.Core.Docker;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace C3Launcher.Gui.ViewModels;

public sealed class ProcessRowViewModel
{
    public required string Pid { get; init; }
    public required string User { get; init; }
    public required string Time { get; init; }
    public required string Command { get; init; }
}

public sealed class MountRowViewModel
{
    public required string Source { get; init; }
    public required string Target { get; init; }
    public required bool Writable { get; init; }

    public string Mode => Writable ? "rw" : "ro";
}

public sealed class SecurityFlagViewModel
{
    public required string Text { get; init; }
    public required bool Enforced { get; init; }
}

public partial class SessionDetailViewModel : ViewModelBase, IDisposable
{
    private readonly SessionService _service;
    private readonly SessionItemViewModel _session;
    private readonly DispatcherTimer _timer;
    private readonly CancellationTokenSource _cts = new();

    public SessionDetailViewModel(SessionItemViewModel session, SessionService service)
    {
        _session = session;
        _service = service;

        Name = session.Name;
        ContainerId = session.ContainerId;
        ShortId = session.ShortId;
        Uptime = session.Uptime;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => _ = PollAsync();
    }

    public string Name { get; }
    public string ContainerId { get; }
    public string ShortId { get; }

    public ObservableCollection<ProcessRowViewModel> Processes { get; } = [];
    public ObservableCollection<MountRowViewModel> Mounts { get; } = [];
    public ObservableCollection<SecurityFlagViewModel> SecurityFlags { get; } = [];

    [ObservableProperty]
    public partial string Uptime { get; set; }

    [ObservableProperty]
    public partial string Cpu { get; set; } = "—";

    [ObservableProperty]
    public partial string Memory { get; set; } = "—";

    [ObservableProperty]
    public partial string Network { get; set; } = "—";

    [ObservableProperty]
    public partial string? Error { get; set; }

    [ObservableProperty]
    public partial int HiddenMountCount { get; set; }

    public string MountsSummary => HiddenMountCount == 0
        ? $"{Mounts.Count} shown"
        : $"{Mounts.Count} shown · {HiddenMountCount} stub mounts collapsed";

    public async Task InitializeAsync()
    {
        await LoadInspectAsync();
        await PollAsync();
        _timer.Start();
    }

    private async Task LoadInspectAsync()
    {
        try
        {
            var inspect = await _service.InspectAsync(ContainerId, _cts.Token);

            Mounts.Clear();
            var hidden = 0;

            foreach (var mount in inspect.Mounts ?? [])
            {
                // The stub mounts are noise here — they all share one temp source.
                if (IsStubMount(mount.Destination))
                {
                    hidden++;
                    continue;
                }

                Mounts.Add(new MountRowViewModel
                {
                    Source = mount.Source,
                    Target = mount.Destination,
                    Writable = mount.RW,
                });
            }

            HiddenMountCount = hidden;
            OnPropertyChanged(nameof(MountsSummary));

            SecurityFlags.Clear();

            if (inspect.HostConfig is { } host)
            {
                SecurityFlags.Add(new SecurityFlagViewModel
                {
                    Text = "ReadonlyRootfs",
                    Enforced = host.ReadonlyRootfs,
                });

                var dropped = host.CapDrop is { Count: > 0 } ? string.Join(",", host.CapDrop) : "none";
                SecurityFlags.Add(new SecurityFlagViewModel
                {
                    Text = $"CapDrop: {dropped}",
                    Enforced = host.CapDrop?.Contains("ALL") == true,
                });

                foreach (var option in host.SecurityOpt ?? [])
                {
                    SecurityFlags.Add(new SecurityFlagViewModel
                    {
                        Text = option,
                        Enforced = true,
                    });
                }

                foreach (var (path, options) in host.Tmpfs ?? new Dictionary<string, string>())
                {
                    SecurityFlags.Add(new SecurityFlagViewModel
                    {
                        Text = $"tmpfs {path} {options}",
                        Enforced = false,
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Error = $"Could not inspect the container: {ex.Message}";
        }
    }

    /// <summary>
    /// The project is mounted at /workspace/&lt;project&gt;, so a destination one
    /// level deeper than that is a stub shadowing something inside it.
    /// </summary>
    private static bool IsStubMount(string destination)
    {
        var prefix = ComposePlanner.WorkspaceRoot + "/";

        return destination.StartsWith(prefix, StringComparison.Ordinal)
            && destination.IndexOf('/', prefix.Length) >= 0;
    }

    private async Task PollAsync()
    {
        Uptime = SessionItemViewModel.FormatUptime(_session.Info.Uptime);

        try
        {
            if (await _service.SampleAsync(ContainerId, _cts.Token) is { } sample)
            {
                Cpu = $"{sample.CpuPercent:0.0}%";
                Memory = SessionItemViewModel.FormatBytes(sample.MemoryBytes);
                Network = SessionItemViewModel.FormatBytes(sample.NetworkBytes);
            }

            var processes = await _service.ListProcessesAsync(ContainerId, _cts.Token);
            ApplyProcesses(processes);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // The container exited; the window's owner will drop it from the list.
            _timer.Stop();
        }
    }

    private void ApplyProcesses(SessionProcesses processes)
    {
        var pid = IndexOf(processes.Titles, "PID");
        var user = IndexOf(processes.Titles, "USER", "UID");
        var time = IndexOf(processes.Titles, "TIME");
        var command = IndexOf(processes.Titles, "CMD", "COMMAND");

        Processes.Clear();

        foreach (var row in processes.Rows)
        {
            Processes.Add(new ProcessRowViewModel
            {
                Pid = At(row, pid),
                User = At(row, user),
                Time = At(row, time),
                Command = At(row, command),
            });
        }
    }

    private static int IndexOf(IReadOnlyList<string> titles, params string[] names)
    {
        for (var i = 0; i < titles.Count; i++)
        {
            foreach (var name in names)
            {
                if (string.Equals(titles[i], name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }

        return -1;
    }

    private static string At(IReadOnlyList<string> row, int index) =>
        index >= 0 && index < row.Count ? row[index] : string.Empty;

    [RelayCommand]
    private async Task StopAsync()
    {
        try
        {
            await _service.StopAsync(ContainerId, _cts.Token);
        }
        catch (Exception ex)
        {
            Error = $"Could not stop the session: {ex.Message}";
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _cts.Cancel();
        _cts.Dispose();
    }
}
