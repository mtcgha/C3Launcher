using Avalonia.Controls;
using Avalonia.Threading;
using C3Launcher.Core.Docker;
using C3Launcher.Core.Launching;
using C3Launcher.Core.Mounting;
using C3Launcher.Core.Projects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace C3Launcher.Gui.ViewModels;

public sealed record ModelChoice(string Label, string? Value);

public sealed record PermissionModeChoice(string Label, string? Value);

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private const int ScanDebounceMs = 150;

    private readonly ProjectStore _store = new();
    private readonly SessionLauncher _launcher = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    // Concurrent: the Docker event pump removes from a background thread while a
    // launch adds from the UI thread.
    private readonly ConcurrentDictionary<string, LaunchedSession> _liveSessions = new();
    private readonly List<ProjectItemViewModel> _allProjects = [];
    private readonly DispatcherTimer _timer;
    private readonly PermissionModeChoice _defaultPermissionMode;

    private LauncherSettings _settings = new();
    private DockerConnection? _docker;
    private SessionService? _sessionService;
    private ImageBuildService? _imageService;
    private ClaudeUpdateService? _updateService;
    private DockerEventMonitor? _events;
    private CancellationTokenSource? _scanCts;
    private MountScanResult? _scan;
    private int _tick;
    private string? _installedClaudeVersion;
    private bool _suppressOptionWrites;

    public MainWindowViewModel()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTimerTick;

        ModelChoices =
        [
            new ModelChoice("Default (account setting)", null),
            new ModelChoice("Opus 5", "opus"),
            new ModelChoice("Sonnet 5", "sonnet"),
            new ModelChoice("Haiku 4.5", "haiku"),
        ];
        SelectedModel = ModelChoices[0];

        PermissionModeChoices =
        [
            new PermissionModeChoice("Default (Claude Code setting)", null),
            new PermissionModeChoice("Manual — ask before every tool", "manual"),
            new PermissionModeChoice("Accept edits — files auto, rest asks", "acceptEdits"),
            new PermissionModeChoice("Auto", "auto"),
            new PermissionModeChoice("Plan", "plan"),
            new PermissionModeChoice("Don't ask", "dontAsk"),
            new PermissionModeChoice("Bypass permissions", "bypassPermissions"),
        ];
        _defaultPermissionMode = PermissionModeChoices
            .First(m => m.Value == ProjectEntry.DefaultPermissionMode);
        SelectedPermissionMode = _defaultPermissionMode;

        if (Design.IsDesignMode)
        {
            SeedDesignData();
            return;
        }

        LoadProjects();
    }

    // Supplied by the view: these need a Window for dialogs and the storage picker.
    public Func<Task<string?>>? PickFolderAsync { get; set; }
    public Func<MountScanResult, string, Task>? ShowMountPreviewAsync { get; set; }
    public Func<SessionItemViewModel, SessionService, Task>? ShowSessionDetailAsync { get; set; }
    public Func<ImageBuildService, string, string?, Task<bool>>? ShowBuildWindowAsync { get; set; }
    public Func<ConfirmRequest, Task<bool>>? ConfirmAsync { get; set; }

    public ObservableCollection<ProjectItemViewModel> Projects { get; } = [];

    public ObservableCollection<SessionItemViewModel> Sessions { get; } = [];

    public ToastHost Toasts { get; } = new();

    public IReadOnlyList<ModelChoice> ModelChoices { get; }

    public IReadOnlyList<PermissionModeChoice> PermissionModeChoices { get; }

    [ObservableProperty]
    public partial string ImageName { get; set; } = ContainerAssets.ImageName;

    [ObservableProperty]
    public partial string ImageVersion { get; set; } = "—";

    [ObservableProperty]
    public partial string UpdateText { get; set; } = "checking…";

    [ObservableProperty]
    public partial bool UpdateAvailable { get; set; }

    [ObservableProperty]
    public partial string? PendingClaudeVersion { get; set; }

    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ProjectItemViewModel? SelectedProject { get; set; }

    [ObservableProperty]
    public partial bool SessionsExpanded { get; set; }

    [ObservableProperty]
    public partial string? StatusText { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string MountSummary { get; set; } = "not scanned";

    [ObservableProperty]
    public partial int BuildCriticalCount { get; set; }

    [ObservableProperty]
    public partial ModelChoice SelectedModel { get; set; }

    [ObservableProperty]
    public partial PermissionModeChoice SelectedPermissionMode { get; set; }

    [ObservableProperty]
    public partial string ExtraArgs { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool SkipUpdateCheck { get; set; }

    public bool HasSelection => SelectedProject is not null;

    public bool HasBuildCritical => BuildCriticalCount > 0;

    public bool HasSessions => Sessions.Count > 0;

    public string SessionsSummary => Sessions.Count switch
    {
        0 => "No active sessions",
        1 => "1 active session",
        var n => $"{n} active sessions",
    };

    public string SessionsTotals
    {
        get
        {
            if (Sessions.Count == 0)
                return string.Empty;

            var memory = Sessions.Aggregate(0UL, (sum, s) => sum + s.MemoryBytes);
            var cpu = Sessions.Sum(s => s.CpuPercent);
            return $"{SessionItemViewModel.FormatBytes(memory)} · {cpu:0.0}%";
        }
    }

    public async Task InitializeAsync()
    {
        try
        {
            _docker = DockerConnection.Create(
                string.IsNullOrWhiteSpace(_settings.DockerEndpoint) ? null : new Uri(_settings.DockerEndpoint));
        }
        catch (Exception ex)
        {
            Toasts.ShowError($"Could not create a Docker client: {ex.Message}");
            UpdateText = "Docker unavailable";
            return;
        }

        if (await _docker.ProbeAsync() is { } probeError)
        {
            Toasts.ShowError(probeError);
            UpdateText = "Docker unavailable";
            return;
        }

        _sessionService = new SessionService(_docker.Client);
        _imageService = new ImageBuildService(_docker.Client);
        _updateService = new ClaudeUpdateService(_docker.Client, new NpmRegistryClient(_http));

        _events = new DockerEventMonitor(_docker.Client);
        _events.Start();
        _ = Task.Run(ConsumeEventsAsync);

        _timer.Start();

        await RefreshSessionsAsync();

        // Anything not backing a live container is a leftover from a previous run
        // that was closed or killed before its container died. Listed straight from
        // Docker rather than read off Sessions: RefreshSessionsAsync swallows a
        // listing failure, and an empty list would read as "nothing is live" and
        // sweep the mount sources and networks of running sessions.
        try
        {
            var live = await _sessionService.ListAsync();
            var liveSessionIds = live.Where(s => s.State is "running").Select(s => s.SessionId).ToList();

            SessionWorkspace.Sweep(liveSessionIds);
            await SessionNetworks.SweepAsync(_docker.Client, liveSessionIds);
        }
        catch (Exception)
        {
            // A daemon hiccup only costs us this sweep; the next start picks it up.
        }

        await CheckForUpdatesAsync();
    }

    private void LoadProjects()
    {
        _settings = _store.Load();
        _allProjects.Clear();

        _suppressOptionWrites = true;
        SessionsExpanded = _settings.SessionsExpanded;
        _suppressOptionWrites = false;

        foreach (var entry in _settings.Projects)
            _allProjects.Add(new ProjectItemViewModel(entry));

        ApplyFilter();
        SelectedProject = Projects.FirstOrDefault();
    }

    private void ApplyFilter()
    {
        var filter = FilterText.Trim();
        var previous = SelectedProject;

        Projects.Clear();

        foreach (var project in _allProjects
            .Where(p => p.Matches(filter))
            .OrderByDescending(p => p.Pinned)
            .ThenBy(p => p.PinSlot ?? int.MaxValue)
            .ThenByDescending(p => p.Entry.LastOpenedUtc ?? DateTime.MinValue))
        {
            Projects.Add(project);
        }

        if (previous is not null && Projects.Contains(previous))
            SelectedProject = previous;
        else
            SelectedProject = Projects.FirstOrDefault();
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    partial void OnBuildCriticalCountChanged(int value) => OnPropertyChanged(nameof(HasBuildCritical));

    partial void OnSessionsExpandedChanged(bool value)
    {
        if (_suppressOptionWrites || Design.IsDesignMode)
            return;

        _settings.SessionsExpanded = value;
        Save();
    }

    partial void OnSelectedProjectChanged(ProjectItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelection));

        _suppressOptionWrites = true;
        SelectedModel = ModelChoices.FirstOrDefault(m => m.Value == value?.Entry.Model) ?? ModelChoices[0];
        SelectedPermissionMode = PermissionModeChoices
            .FirstOrDefault(m => m.Value == value?.Entry.PermissionMode) ?? _defaultPermissionMode;
        ExtraArgs = value?.Entry.ExtraArgs ?? string.Empty;
        SkipUpdateCheck = value?.Entry.SkipUpdateCheck ?? false;
        _suppressOptionWrites = false;

        if (Design.IsDesignMode)
            return;

        _scan = null;
        MountSummary = "not scanned";
        BuildCriticalCount = 0;

        if (value is not null)
            _ = ScanMountsAsync(value);
    }

    partial void OnSelectedModelChanged(ModelChoice value) => WriteOption(e => e.Model = value.Value);

    partial void OnSelectedPermissionModeChanged(PermissionModeChoice value) =>
        WriteOption(e => e.PermissionMode = value.Value);

    partial void OnExtraArgsChanged(string value) => WriteOption(e => e.ExtraArgs = value);

    partial void OnSkipUpdateCheckChanged(bool value) => WriteOption(e => e.SkipUpdateCheck = value);

    private void WriteOption(Action<ProjectEntry> apply)
    {
        if (_suppressOptionWrites || SelectedProject is null)
            return;

        apply(SelectedProject.Entry);
        Save();
    }

    private void Save()
    {
        try
        {
            _store.Save(_settings);
        }
        catch (Exception ex)
        {
            Toasts.ShowError($"Could not save settings: {ex.Message}");
        }
    }

    private async Task ScanMountsAsync(ProjectItemViewModel project)
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;

        MountSummary = "scanning…";

        await Task.Delay(ScanDebounceMs, CancellationToken.None);
        if (token.IsCancellationRequested || SelectedProject != project)
            return;

        try
        {
            var patterns = MountIgnorePattern.ParseFile(
                project.Entry.MountIgnorePath ?? ContainerAssets.DefaultMountIgnorePath);

            var scanner = new MountIgnoreScanner();
            var result = await Task.Run(() => scanner.Scan(project.Entry.Path, patterns, null, token), token);

            if (token.IsCancellationRequested || SelectedProject != project)
                return;

            _scan = result;
            MountSummary = result.MatchCount switch
            {
                0 => "nothing hidden",
                1 => "1 item hidden",
                var n => $"{n} items hidden",
            };
            BuildCriticalCount = result.BuildCriticalWarnings
                .Select(w => w.RelativePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            // The summary slot is one line wide; the reason goes where it fits.
            MountSummary = "scan failed";
            Toasts.ShowError($"Could not scan {project.Entry.Name} for mount filtering: {ex.Message}");
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        if (_updateService is null || _imageService is null)
            return;

        var soak = TimeSpan.FromDays(_settings.UpdateSoakDays);

        if (!await _imageService.ImageExistsAsync(ImageName))
        {
            ImageVersion = "not built";
            _installedClaudeVersion = null;

            // Resolve a version even with no image, so the first build installs a
            // release the soak approves rather than whatever `latest` points at.
            PendingClaudeVersion = await _updateService.GetSoakedVersionAsync(soak);

            UpdateText = PendingClaudeVersion is { } first
                ? $"image not built yet — will install v{first}"
                : "image not built yet";
            UpdateAvailable = false;
            return;
        }

        if (!_settings.CheckForUpdates)
        {
            _installedClaudeVersion = await _updateService.GetInstalledVersionAsync(ImageName);
            ImageVersion = _installedClaudeVersion is { } v ? $"v{v}" : "—";
            UpdateText = "update check disabled";
            return;
        }

        UpdateText = "checking for updates…";

        var status = await _updateService.CheckAsync(ImageName, soak);

        _installedClaudeVersion = status.InstalledVersion;
        ImageVersion = status.InstalledVersion is { } installed ? $"v{installed}" : "—";

        if (status.Error is { } error)
        {
            // The pill it lands in is the "up to date" one, green tick and all, so
            // the reason goes to a toast and the pill says only that it did not run.
            UpdateText = "update check failed";
            UpdateAvailable = false;
            Toasts.ShowError($"Could not check for Claude Code updates: {error}");
            return;
        }

        if (status.UpdateAvailable && status.Latest is { } latest)
        {
            UpdateAvailable = true;
            PendingClaudeVersion = latest.Version.ToString();
            UpdateText = $"v{latest.Version} available";
        }
        else
        {
            UpdateAvailable = false;
            PendingClaudeVersion = null;
            UpdateText = "up to date";
        }
    }

    private async Task ConsumeEventsAsync()
    {
        if (_events is null)
            return;

        await foreach (var message in _events.Events)
        {
            var action = message.Action;
            var labels = message.Actor?.Attributes;

            if (action is "die" or "destroy" or "stop")
            {
                if (labels is not null && labels.TryGetValue(SessionLabels.SessionId, out var sessionId))
                {
                    if (_liveSessions.TryRemove(sessionId, out var session))
                        session.Dispose();

                    // Only once the container is gone is its network free to delete.
                    if (action is "destroy" && _docker is { } docker)
                        await SessionNetworks.DeleteAsync(docker.Client, sessionId);
                }
            }

            await Dispatcher.UIThread.InvokeAsync(RefreshSessionsAsync);
        }
    }

    private async Task RefreshSessionsAsync()
    {
        if (_sessionService is null)
            return;

        IReadOnlyList<SessionInfo> current;
        try
        {
            current = await _sessionService.ListAsync();
        }
        catch
        {
            return;
        }

        var running = current.Where(s => s.State is "running").ToList();

        for (var i = Sessions.Count - 1; i >= 0; i--)
        {
            if (running.All(s => s.ContainerId != Sessions[i].ContainerId))
                Drop(Sessions[i]);
        }

        foreach (var info in running)
        {
            var existing = Sessions.FirstOrDefault(s => s.ContainerId == info.ContainerId);
            if (existing is null)
                Sessions.Add(new SessionItemViewModel(info));
            else
                existing.Update(info);
        }

        SyncSessionState();
    }

    /// <summary>
    /// Everything that reads off <see cref="Sessions"/> rather than off Docker, so
    /// a row taken away by hand leaves the same UI as one that exited normally.
    /// </summary>
    private void SyncSessionState()
    {
        foreach (var project in _allProjects)
        {
            project.IsRunning = Sessions.Any(s =>
                string.Equals(s.Info.ProjectPath, project.Entry.Path, StringComparison.OrdinalIgnoreCase));
        }

        OnPropertyChanged(nameof(HasSessions));
        OnPropertyChanged(nameof(SessionsSummary));
        OnPropertyChanged(nameof(SessionsTotals));
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        foreach (var session in Sessions)
            session.Tick();

        _tick++;

        if (_tick % 3 == 0)
            _ = PollAsync();

        // Only the selected project shows LAST OPENED, and "X min ago" cannot change
        // faster than once a minute, so anything more often is wasted work.
        if (_tick % 60 == 0)
            SelectedProject?.RefreshElapsed();
    }

    /// <summary>
    /// Re-lists before sampling rather than trusting the event stream on its own.
    /// A daemon restart kills every container while the stream is down, and the
    /// events for those deaths are never replayed — so without this the strip
    /// keeps showing sessions that stopped when Docker Desktop did.
    /// </summary>
    private async Task PollAsync()
    {
        await RefreshSessionsAsync();
        await SampleAllAsync();
    }

    private async Task SampleAllAsync()
    {
        if (_sessionService is null)
            return;

        foreach (var session in Sessions.ToList())
        {
            try
            {
                if (await _sessionService.SampleAsync(session.ContainerId) is { } sample)
                    session.Apply(sample);
            }
            catch
            {
                // A container that exited mid-sample is picked up by the next refresh.
            }
        }

        OnPropertyChanged(nameof(SessionsTotals));
    }

    [RelayCommand]
    private async Task LaunchAsync()
    {
        if (SelectedProject is not { } project)
            return;

        IsBusy = true;
        StatusText = "Building image…";

        try
        {
            // Per launch rather than at startup: the compose file declares the
            // volume external, so compose fails with "external volume not
            // found" if anything removed it while the app was open. Creating it
            // is a list plus, at most, one create.
            if (_docker is { } docker)
            {
                try
                {
                    await ClaudeHomeVolume.EnsureAsync(docker.Client);
                }
                catch (Exception ex)
                {
                    Toasts.ShowError($"Could not create the Claude home volume: {ex.Message}");
                    return;
                }
            }

            if (_imageService is not null && ShowBuildWindowAsync is not null)
            {
                var approved = SkipUpdateCheck ? null : PendingClaudeVersion;
                // False covers a cancelled build as well as a failed one. The build
                // window reported the reason; this says what it cost.
                if (!await ShowBuildWindowAsync(_imageService, ImageName, ResolveBuildVersion(approved)))
                {
                    Toasts.ShowError("Session not started — the image build did not complete.");
                    return;
                }

                PendingClaudeVersion = null;
                await CheckForUpdatesAsync();
            }

            StatusText = "Starting session…";

            var outcome = await _launcher.LaunchAsync(new LaunchOptions
            {
                ProjectPath = project.Entry.Path,
                ProjectName = project.Entry.Name,
                Model = SelectedModel.Value,
                PermissionMode = SelectedPermissionMode.Value,
                ExtraArgs = SplitArgs(ExtraArgs),
                Image = ImageName,
                MountIgnorePath = project.Entry.MountIgnorePath,
            });

            if (!outcome.Ok)
            {
                Toasts.ShowError(outcome.Error!);
                return;
            }

            _liveSessions[outcome.Session!.SessionId] = outcome.Session;
            ProjectStore.RecordLaunch(project.Entry);
            project.Refresh();
            Save();
        }
        finally
        {
            // StatusText only ever narrates a launch in progress; failures leave
            // by toast, so there is nothing left for it to say either way.
            StatusText = null;
            IsBusy = false;
        }
    }

    public static IReadOnlyList<string> SplitArgs(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;

        foreach (var c in raw)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (current.Length > 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            args.Add(current.ToString());

        return args;
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (SelectedProject is not { } project)
            return;

        try
        {
            Process.Start(new ProcessStartInfo(project.Entry.Path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Toasts.ShowError($"Could not open the folder: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task AddProjectAsync()
    {
        if (PickFolderAsync is null)
            return;

        var picked = await PickFolderAsync();
        if (picked is null)
            return;

        if (!Core.Launching.ProjectPath.TryResolve(picked, out var full, out var error))
        {
            Toasts.ShowError(error!);
            return;
        }

        var existing = _allProjects.FirstOrDefault(p =>
            string.Equals(p.Entry.Path, full, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            SelectedProject = existing;
            return;
        }

        var entry = ProjectStore.CreateEntry(full);
        _settings.Projects.Add(entry);

        var item = new ProjectItemViewModel(entry);
        _allProjects.Add(item);
        Save();
        ApplyFilter();
        SelectedProject = item;
    }

    [RelayCommand]
    private async Task RemoveProjectAsync(ProjectItemViewModel? project)
    {
        project ??= SelectedProject;
        if (project is null)
            return;

        if (ConfirmAsync is not null && !await ConfirmAsync(new ConfirmRequest(
            $"Remove {project.Entry.Name}?",
            "Drops the project from this list, along with its model choice and extra arguments.",
            "Remove",
            project.IsRunning
                ? "A session is running for this project. It keeps running, and still shows "
                    + "in the sessions strip until it exits."
                : null)))
        {
            return;
        }

        _settings.Projects.Remove(project.Entry);
        _allProjects.Remove(project);
        Save();
        ApplyFilter();

        Toasts.Show($"Removed {project.Entry.Name}");
    }

    [RelayCommand]
    private void TogglePin(ProjectItemViewModel? project)
    {
        project ??= SelectedProject;
        if (project is null)
            return;

        if (project.Entry.Pinned)
        {
            project.Entry.Pinned = false;
            project.Entry.PinSlot = null;
        }
        else
        {
            var used = _allProjects.Where(p => p.Entry.Pinned).Select(p => p.Entry.PinSlot).ToHashSet();
            var slot = Enumerable.Range(1, 9).FirstOrDefault(n => !used.Contains(n));

            project.Entry.Pinned = true;
            project.Entry.PinSlot = slot == 0 ? null : slot;
        }

        project.Refresh();
        Save();
        ApplyFilter();
    }

    public void LaunchPinned(int slot)
    {
        var project = _allProjects.FirstOrDefault(p => p.Entry.PinSlot == slot);
        if (project is null)
            return;

        SelectedProject = project;
        _ = LaunchAsync();
    }

    [RelayCommand]
    private async Task RebuildAsync()
    {
        if (_imageService is null || ShowBuildWindowAsync is null)
            return;

        if (await ShowBuildWindowAsync(_imageService, ImageName, ResolveBuildVersion(PendingClaudeVersion)))
        {
            PendingClaudeVersion = null;
            await CheckForUpdatesAsync();
        }
    }

    /// <summary>
    /// Always names a version if one is knowable. Passing null would let the
    /// Dockerfile's `CLAUDE_VERSION=latest` default choose, and any rebuild of that
    /// layer — an edit above it, a pruned cache — would then install whatever npm
    /// calls latest, soak policy or not.
    /// </summary>
    /// <param name="approved">
    /// The update to install, or null to re-pin whatever is already there. Callers
    /// decide: a launch honours the selected project's skip-update setting, while
    /// Rebuild is a deliberate click on a global action and always applies.
    /// </param>
    private string? ResolveBuildVersion(string? approved) =>
        approved is { Length: > 0 }
            ? approved
            : _installedClaudeVersion ?? PendingClaudeVersion;

    [RelayCommand]
    private async Task PreviewMountsAsync()
    {
        if (_scan is null || ShowMountPreviewAsync is null || SelectedProject is null)
            return;

        var path = SelectedProject.Entry.MountIgnorePath ?? ContainerAssets.DefaultMountIgnorePath;
        await ShowMountPreviewAsync(_scan, path);
    }

    [RelayCommand]
    private void ToggleSessions() => SessionsExpanded = !SessionsExpanded;

    [RelayCommand]
    private async Task StopSessionAsync(SessionItemViewModel? session)
    {
        if (session is null || _sessionService is null)
            return;

        var outcome = await _sessionService.StopAsync(session.ContainerId);

        if (outcome.AlreadyGone)
        {
            Drop(session);
            Toasts.Show($"{session.Name} had already stopped.");
        }
        else if (!outcome.Ok)
        {
            Toasts.ShowError($"Could not stop {session.Name} — {outcome.Error}");
        }

        // Even a clean stop is confirmed by listing rather than assumed: the die
        // event is the normal signal, and this covers the case where it is lost.
        await RefreshSessionsAsync();
    }

    /// <summary>
    /// Takes a session off the strip for a container Docker no longer lists. The
    /// workspace goes with it: the die event that normally disposes it is lost
    /// whenever the daemon restarts under a running session, and it is never
    /// replayed. Disposing twice would be the ordinary path, so
    /// <see cref="_liveSessions"/> is the guard — the die handler removes from it
    /// first, and this finds nothing left to dispose.
    /// </summary>
    private void Drop(SessionItemViewModel session)
    {
        Sessions.Remove(session);

        if (_liveSessions.TryRemove(session.Info.SessionId, out var live))
            live.Dispose();

        SyncSessionState();
    }

    [RelayCommand]
    private async Task ShowSessionAsync(SessionItemViewModel? session)
    {
        if (session is null || _sessionService is null || ShowSessionDetailAsync is null)
            return;

        await ShowSessionDetailAsync(session, _sessionService);
    }

    private void SeedDesignData()
    {
        ImageVersion = "v2.0.14";
        UpdateText = "up to date";
        MountSummary = "142 items hidden";
        BuildCriticalCount = 3;

        foreach (var (name, path, pinned, slot) in new[]
        {
            ("C3Launcher", @"C:\Projects\C3Launcher", true, 1),
            ("claude-container", @"C:\Projects\claude-container", true, 2),
            ("rate-filing-api", @"C:\work\rate-filing-api", false, 0),
        })
        {
            var entry = new ProjectEntry
            {
                Name = name,
                Path = path,
                Pinned = pinned,
                PinSlot = slot == 0 ? null : slot,
            };

            _allProjects.Add(new ProjectItemViewModel(entry));
        }

        Toasts.Show("Removed rate-filing-api");
        Toasts.ShowError("Could not stop claude-container — No such container: 8f2c1a4b9e07");

        ApplyFilter();
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTimerTick;

        _scanCts?.Cancel();
        _scanCts?.Dispose();

        // Deliberately not disposed: their containers may still be running, and the
        // files are bind-mount sources. The sweep on next start collects them.
        _liveSessions.Clear();

        // Not awaited: the event pump marshals to the UI thread, so blocking here
        // while it is mid-dispatch would deadlock on shutdown.
        _ = _events?.DisposeAsync().AsTask();

        _docker?.Dispose();
        _http.Dispose();
    }
}
