using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;

namespace C3Launcher.Gui.ViewModels;

/// <summary>
/// The app's one error surface. Every window that can fail owns a host and
/// renders it with ToastOverlay, so a failure reaches the user the same way
/// wherever they happen to be — there is no second place to learn to look.
/// </summary>
/// <remarks>
/// Not a <see cref="ViewModelBase"/>: ViewLocator resolves a view for anything
/// that is one, and this is a piece of a screen rather than a screen.
/// </remarks>
public sealed partial class ToastHost
{
    private const int TransientMs = 3000;

    // Errors persist, so without a cap a daemon that has gone away buries the
    // window under one toast per failed click.
    private const int MaxToasts = 4;

    public ObservableCollection<ToastViewModel> Items { get; } = [];

    /// <summary>A confirmation. Gone on its own in a few seconds.</summary>
    public void Show(string message)
    {
        var toast = Push(new ToastViewModel(message, isError: false));
        _ = ExpireAsync(toast);
    }

    /// <summary>
    /// No timer: whatever failed is usually something the user has to act on, and
    /// a message that fades takes the only account of it with it. The close button
    /// is the only way out, so a failure raised twice reuses the toast already on
    /// screen rather than stacking a copy.
    /// </summary>
    public void ShowError(string message)
    {
        if (Items.Any(t => t.IsError && t.Text == message))
            return;

        Push(new ToastViewModel(message, isError: true));
    }

    /// <summary>
    /// Over the cap the oldest transient toast goes first: it was about to expire
    /// anyway, where an error is still waiting to be read.
    /// </summary>
    private ToastViewModel Push(ToastViewModel toast)
    {
        while (Items.Count >= MaxToasts)
            Items.Remove(Items.FirstOrDefault(t => !t.IsError) ?? Items[0]);

        Items.Add(toast);
        return toast;
    }

    /// <summary>
    /// The delay deliberately isn't cancellable: a cancelled Task.Delay throws,
    /// and the debugger reports that on every toast closed early.
    /// </summary>
    private async Task ExpireAsync(ToastViewModel toast)
    {
        await Task.Delay(TransientMs, CancellationToken.None);
        Items.Remove(toast);
    }

    [RelayCommand]
    private void Dismiss(ToastViewModel? toast)
    {
        if (toast is not null)
            Items.Remove(toast);
    }
}
