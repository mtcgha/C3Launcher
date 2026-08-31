using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace C3Launcher.Gui.Views;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow()
    {
        InitializeComponent();

        // Cancel takes focus so Enter and Space can't confirm a destructive action.
        Opened += (_, _) => CancelButton.Focus();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(false);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}
