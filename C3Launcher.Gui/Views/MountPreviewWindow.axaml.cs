using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using C3Launcher.Gui.ViewModels;

namespace C3Launcher.Gui.Views;

public partial class MountPreviewWindow : Window
{
    public MountPreviewWindow() => InitializeComponent();

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void OnEditPatterns(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MountPreviewViewModel model)
            return;

        try
        {
            Process.Start(new ProcessStartInfo(model.MountIgnorePath) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // No registered handler for the extensionless file; nothing useful to do.
        }
    }
}
