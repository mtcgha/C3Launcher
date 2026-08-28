using Avalonia.Controls;
using Avalonia.Interactivity;
using C3Launcher.Gui.ViewModels;

namespace C3Launcher.Gui.Views;

public partial class SessionDetailWindow : Window
{
    public SessionDetailWindow()
    {
        InitializeComponent();

        Opened += async (_, _) =>
        {
            if (DataContext is SessionDetailViewModel model)
                await model.InitializeAsync();
        };

        Closed += (_, _) => (DataContext as SessionDetailViewModel)?.Dispose();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
