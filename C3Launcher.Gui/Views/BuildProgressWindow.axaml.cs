using Avalonia.Controls;
using Avalonia.Interactivity;
using C3Launcher.Gui.ViewModels;

namespace C3Launcher.Gui.Views;

public partial class BuildProgressWindow : Window
{
    private bool _succeeded;

    public BuildProgressWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is not BuildProgressViewModel model)
            return;

        model.Lines.CollectionChanged += (_, _) => LogScroller.ScrollToEnd();

        await model.RunAsync();

        _succeeded = model.Succeeded;

        // A clean build is the uninteresting case — don't make the user dismiss it.
        if (_succeeded)
            Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        (DataContext as BuildProgressViewModel)?.Cancel();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close(_succeeded);
}
