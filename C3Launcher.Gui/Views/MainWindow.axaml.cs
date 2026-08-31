using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using C3Launcher.Core.Docker;
using C3Launcher.Core.Mounting;
using C3Launcher.Gui.ViewModels;

namespace C3Launcher.Gui.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Opened += OnOpened;
        Closing += OnClosing;

        // Tunnel so these still run when a control handles the input itself.
        AddHandler(PointerPressedEvent, OnAnyPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnAnyPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsInteractive(e.Source as Control))
            FocusManager.Focus(null);
    }

    private static bool IsInteractive(Control? source)
    {
        for (var current = source; current is not null; current = current.Parent as Control)
        {
            // ListBoxItem but not ListBox: a click on an item keeps focus for arrow-key
            // navigation, while one on the empty space below the items clears it.
            if (current is TextBox or Button or ComboBox or CheckBox or ListBoxItem)
                return true;
        }

        return false;
    }

    private MainWindowViewModel? Model => DataContext as MainWindowViewModel;

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (Model is not { } model)
            return;

        model.PickFolderAsync = PickFolderAsync;
        model.ShowMountPreviewAsync = ShowMountPreviewAsync;
        model.ShowSessionDetailAsync = ShowSessionDetailAsync;
        model.ShowBuildWindowAsync = ShowBuildWindowAsync;
        model.ConfirmAsync = ConfirmAsync;

        FilterBox.Focus();

        await model.InitializeAsync();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e) => Model?.Dispose();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            FocusManager.Focus(null);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers != KeyModifiers.Control)
            return;

        if (e.Key is >= Key.D1 and <= Key.D9)
        {
            Model?.LaunchPinned(e.Key - Key.D1 + 1);
            e.Handled = true;
        }
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void OnSessionDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: SessionItemViewModel session } && Model is { } model)
            model.ShowSessionCommand.Execute(session);
    }

    private async Task<string?> PickFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a project directory",
            AllowMultiple = false,
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private Task ShowMountPreviewAsync(MountScanResult scan, string mountIgnorePath) =>
        new MountPreviewWindow
        {
            DataContext = new MountPreviewViewModel(scan, mountIgnorePath),
        }.ShowDialog(this);

    private Task ShowSessionDetailAsync(SessionItemViewModel session, SessionService service) =>
        new SessionDetailWindow
        {
            DataContext = new SessionDetailViewModel(session, service),
        }.ShowDialog(this);

    private Task<bool> ShowBuildWindowAsync(ImageBuildService builder, string image, string? claudeVersion)
    {
        var window = new BuildProgressWindow
        {
            DataContext = new BuildProgressViewModel(builder, image, claudeVersion),
        };

        return window.ShowDialog<bool>(this);
    }

    private Task<bool> ConfirmAsync(ConfirmRequest request) =>
        new ConfirmWindow { DataContext = request }.ShowDialog<bool>(this);
}
