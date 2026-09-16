using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Animora.Desktop.Shell;

/// <summary>
/// Standard Avalonia MVVM view resolution: maps a "…ViewModel" instance to
/// its "…View" control by naming convention, so the shell's ContentControl
/// renders the right View for whatever ViewModel the navigation service sets
/// as current. Views are constructed parameterless (no DI dependency) — all
/// data comes from the injected ViewModel via DataContext.
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control Build(object? data)
    {
        if (data is null)
        {
            return new TextBlock { Text = "null" };
        }

        var viewModelName = data.GetType().FullName!;
        var viewName = viewModelName.EndsWith("ViewModel", StringComparison.Ordinal)
            ? viewModelName[..^"ViewModel".Length] + "View"
            : viewModelName + "View";

        var viewType = Type.GetType(viewName);
        if (viewType is not null && Activator.CreateInstance(viewType) is Control control)
        {
            control.DataContext = data;
            return control;
        }

        return new TextBlock { Text = $"View not found: {viewName}" };
    }

    public bool Match(object? data) => data is PageViewModelBase;
}
