namespace C3Launcher.Gui.ViewModels;

/// <summary>
/// A message floating over the body. Transient ones drop themselves out of the
/// collection after a few seconds; an error stays until the user closes it,
/// because a failure is usually something they have to act on and a toast that
/// fades while they are reading it takes the only account of what went wrong.
/// </summary>
/// <remarks>
/// A class rather than a record: dismissal removes one instance by identity, and
/// two toasts carrying the same text are two toasts.
/// </remarks>
public sealed class ToastViewModel
{
    public ToastViewModel(string text, bool isError)
    {
        Text = text;
        IsError = isError;
    }

    public string Text { get; }

    public bool IsError { get; }
}
