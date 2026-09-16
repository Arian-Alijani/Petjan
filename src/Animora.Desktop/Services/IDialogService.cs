using Avalonia.Controls;

namespace Animora.Desktop.Services;

/// <summary>
/// Generic overlay host contract (dim backdrop + centered modal panel /
/// right-anchored sliding drawer panel), matching Animora-UI's modal/drawer
/// look. Screen phases supply the content control; this service only
/// manages showing/closing the host chrome.
/// </summary>
public interface IDialogService
{
    void ShowModal(Control content);

    void ShowDrawer(Control content);

    void Close();
}
