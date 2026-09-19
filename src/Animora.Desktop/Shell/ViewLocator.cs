using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Animora.Desktop.Shell;

/// <summary>
/// Standard Avalonia MVVM view resolution: maps a "…ViewModel" instance to its
/// "…View" control, so the shell's ContentControl (and the modal/drawer/sheet
/// hosts) renders the right View for whatever ViewModel the navigation service
/// sets as current. Views are constructed parameterless (no DI dependency) —
/// all data comes from the injected ViewModel via DataContext.
///
/// Resolution is served by two collaborators so the navigation hot path stays
/// allocation-light and never rebuilds a page it has already shown:
/// <list type="bullet">
/// <item><see cref="ViewFactory"/> — a compile-time
/// <see cref="Type"/> → <c>Func&lt;Control&gt;</c> registry, replacing the old
/// name-string + <c>Type.GetType</c> + <c>Activator.CreateInstance</c> reflection
/// with a dictionary lookup and a direct constructor call.</item>
/// <item><see cref="ViewCache"/> — identity-keyed reuse of each View for its
/// ViewModel, so returning to a page re-hosts the already-built visual tree
/// instead of re-inflating it (the ViewModels are singletons, so this is the
/// natural lifetime).</item>
/// </list>
/// Both are injected from the composition root so the locator stays a thin,
/// testable seam with no static mutable state.
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    private readonly ViewCache _cache;

    public ViewLocator(ViewCache cache) => _cache = cache;

    public Control Build(object? data)
    {
        if (data is null)
        {
            return new TextBlock { Text = "null" };
        }

        // Reuse the View already built for this exact ViewModel instance, or
        // build it once through the reflection-free factory registry.
        var control = _cache.GetOrCreate(data, () => ViewFactory.Create(data.GetType()) ?? NotFound(data));

        // The View is reused across navigations, but its DataContext is set on
        // every resolve: transient ViewModels are one ViewModel → one View, so
        // this is a no-op for them, and it keeps the assignment correct even if
        // a future ViewModel is shared.
        control.DataContext = data;
        return control;
    }

    public bool Match(object? data) =>
        data?.GetType().Name.EndsWith("ViewModel", StringComparison.Ordinal) == true;

    private static Control NotFound(object data) =>
        new TextBlock { Text = $"View not found for: {data.GetType().FullName}" };
}
