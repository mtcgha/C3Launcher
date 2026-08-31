using C3Launcher.Core.Projects;
using CommunityToolkit.Mvvm.ComponentModel;

namespace C3Launcher.Gui.ViewModels;

public partial class ProjectItemViewModel : ViewModelBase
{
    public ProjectItemViewModel(ProjectEntry entry)
    {
        Entry = entry;
        Name = entry.Name;
        Path = entry.Path;
        Pinned = entry.Pinned;
        PinSlot = entry.PinSlot;
    }

    public ProjectEntry Entry { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string Path { get; set; }

    [ObservableProperty]
    public partial bool Pinned { get; set; }

    [ObservableProperty]
    public partial int? PinSlot { get; set; }

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    public string? PinAccelerator => PinSlot is { } slot ? $"Ctrl+{slot}" : null;

    public string LastOpenedText => Entry.LastOpenedUtc is { } opened
        ? Humanize(DateTime.UtcNow - opened)
        : "never";

    public string LaunchCountText => Entry.LaunchCount.ToString();

    /// <summary>
    /// Re-reads only what drifts with the clock, so the once-a-minute tick doesn't
    /// churn every other binding on the item.
    /// </summary>
    public void RefreshElapsed() => OnPropertyChanged(nameof(LastOpenedText));

    public void Refresh()
    {
        Name = Entry.Name;
        Path = Entry.Path;
        Pinned = Entry.Pinned;
        PinSlot = Entry.PinSlot;
        OnPropertyChanged(nameof(PinAccelerator));
        OnPropertyChanged(nameof(LastOpenedText));
        OnPropertyChanged(nameof(LaunchCountText));
    }

    public bool Matches(string filter) =>
        filter.Length == 0
        || Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Path.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static string Humanize(TimeSpan age) => age switch
    {
        { TotalMinutes: < 1 } => "just now",
        { TotalMinutes: < 60 } => $"{(int)age.TotalMinutes} min ago",
        { TotalHours: < 24 } => Ago((int)age.TotalHours, "hour"),
        { TotalDays: < 30 } => Ago((int)age.TotalDays, "day"),
        _ => Ago((int)(age.TotalDays / 30), "month"),
    };

    private static string Ago(int count, string unit) =>
        count == 1 ? $"1 {unit} ago" : $"{count} {unit}s ago";

    partial void OnPinSlotChanged(int? value) => OnPropertyChanged(nameof(PinAccelerator));
}
