namespace Animora.Web.Admin;
/// <summary>
/// Interactive-island budget. Two islands only (status + live log); no global render mode. The live log needs its own island
/// because it pushes new lines while the operator watches; <c>LiveStatusPanel</c> stays on the dashboard.
/// </summary>
public static class RenderModePolicy { public const int MaxIslands=2; }
