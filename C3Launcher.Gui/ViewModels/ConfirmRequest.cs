namespace C3Launcher.Gui.ViewModels;

/// <summary>
/// Nothing here changes once the dialog is open, so the request doubles as the
/// window's view model.
/// </summary>
public sealed record ConfirmRequest(
    string Title,
    string Message,
    string ConfirmLabel,
    string? Warning = null)
{
    public bool HasWarning => !string.IsNullOrEmpty(Warning);
}
