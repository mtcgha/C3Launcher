using C3Launcher.Core.Docker;
using CommunityToolkit.Mvvm.ComponentModel;

namespace C3Launcher.Gui.ViewModels;

public partial class SessionItemViewModel : ViewModelBase
{
    public SessionItemViewModel(SessionInfo info)
    {
        Info = info;
        Name = info.ProjectName;
        ShortId = Shorten(info.ContainerId);
        Command = info.Command;
        Uptime = FormatUptime(info.Uptime);
    }

    public SessionInfo Info { get; private set; }

    public string ContainerId => Info.ContainerId;

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string ShortId { get; set; }

    [ObservableProperty]
    public partial string Uptime { get; set; } = "--:--:--";

    [ObservableProperty]
    public partial string Memory { get; set; } = "—";

    [ObservableProperty]
    public partial string Cpu { get; set; } = "—";

    [ObservableProperty]
    public partial string Command { get; set; } = string.Empty;

    public ulong MemoryBytes { get; private set; }

    public double CpuPercent { get; private set; }

    public void Update(SessionInfo info)
    {
        Info = info;
        Name = info.ProjectName;
        ShortId = Shorten(info.ContainerId);
        Command = info.Command;
        Uptime = FormatUptime(info.Uptime);
    }

    public void Tick() => Uptime = FormatUptime(Info.Uptime);

    public void Apply(SessionSample sample)
    {
        MemoryBytes = sample.MemoryBytes;
        CpuPercent = sample.CpuPercent;
        Memory = FormatBytes(sample.MemoryBytes);
        Cpu = $"{sample.CpuPercent:0.0}%";
    }

    public static string FormatUptime(TimeSpan uptime) =>
        uptime < TimeSpan.Zero
            ? "00:00:00"
            : $"{(int)uptime.TotalHours:00}:{uptime.Minutes:00}:{uptime.Seconds:00}";

    public static string FormatBytes(ulong bytes) => bytes switch
    {
        >= 1024UL * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):0.0} GB",
        >= 1024UL * 1024 => $"{bytes / (1024.0 * 1024):0} MB",
        >= 1024UL => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} B",
    };

    private static string Shorten(string containerId) =>
        containerId.Length > 12 ? containerId[..12] : containerId;
}
