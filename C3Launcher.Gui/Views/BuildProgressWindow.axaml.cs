using Avalonia.Controls;
using Avalonia.Interactivity;
using C3Launcher.Gui.ViewModels;
using System.Diagnostics;

namespace C3Launcher.Gui.Views;

public partial class BuildProgressWindow : Window
{
    // A fully cached build finishes in a few hundred milliseconds, and a window that
    // flashes past reads as something going wrong rather than nothing needing doing.
    private static readonly TimeSpan MinimumVisible = TimeSpan.FromMilliseconds(1750);

    private bool _succeeded;
    private bool _closed;

    public BuildProgressWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closed += (_, _) => _closed = true;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is not BuildProgressViewModel model)
            return;

        model.Lines.CollectionChanged += (_, _) => LogScroller.ScrollToEnd();

        var shown = Stopwatch.StartNew();
        await model.RunAsync();

        _succeeded = model.Succeeded;

        // A clean build is the uninteresting case — don't make the user dismiss it.
        if (!_succeeded)
            return;

        var remaining = MinimumVisible - shown.Elapsed;
        if (remaining > TimeSpan.Zero)
            await Task.Delay(remaining);

        if (!_closed)
            Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        (DataContext as BuildProgressViewModel)?.Cancel();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close(_succeeded);
}
