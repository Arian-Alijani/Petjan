using Animora.Desktop.Controls;

namespace Animora.Desktop.Services;

/// <summary>
/// Corner-anchored transient toast messages, matching Animora-UI's
/// window.toast(msg, icon) pattern — driven by a service call instead of
/// a static global.
/// </summary>
public interface IToastService
{
    void Show(string text, Tone tone = Tone.Ok);
}
