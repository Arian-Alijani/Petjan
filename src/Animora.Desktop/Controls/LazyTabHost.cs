using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Metadata;

namespace Animora.Desktop.Controls;

/// <summary>
/// A named lazily-instantiated tab: its <see cref="Key"/> and the
/// <see cref="Template"/> that builds the tab's content on first activation.
/// </summary>
public sealed class LazyTab
{
    /// <summary>Matches <see cref="LazyTabHost.ActiveKey"/>.ToString() (ordinal).</summary>
    public string Key { get; set; } = string.Empty;

    [Content]
    public IDataTemplate? Template { get; set; }
}

/// <summary>
/// Hosts a set of keyed tabs (declared as <see cref="LazyTab"/> templates) and
/// realises <b>only</b> the active tab's content. This is the lazy alternative to
/// stacking every tab in the visual tree and toggling each with
/// <c>IsVisible</c>: hidden tabs were still constructed, measured and arranged
/// (and kept their full subtree in memory), so several heavy tabs inflated the
/// first-open cost and RAM. Here an inactive tab is never built, and a tab that
/// has been built once is cached (keyed) so returning to it preserves its state
/// exactly as before.
///
/// Behaviour is otherwise identical to the old pattern: the same single tab is
/// rendered at a time, in the same place, with the same DataContext (the host's
/// inherited DataContext), and switching tabs swaps which child is shown.
/// </summary>
public class LazyTabHost : ContentControl
{
    /// <summary>The currently-selected tab key (string, or any value whose
    /// <see cref="object.ToString"/> matches a <see cref="LazyTab.Key"/>).</summary>
    public static readonly StyledProperty<object?> ActiveKeyProperty =
        AvaloniaProperty.Register<LazyTabHost, object?>(nameof(ActiveKey));

    private readonly ObservableCollection<LazyTab> _tabTemplates = [];
    private readonly Dictionary<string, Control> _cache = new(StringComparer.Ordinal);

    static LazyTabHost()
    {
        ActiveKeyProperty.Changed.AddClassHandler<LazyTabHost>((host, _) => host.Sync());
    }

    public LazyTabHost()
    {
        _tabTemplates.CollectionChanged += OnTabTemplatesChanged;
    }

    public object? ActiveKey
    {
        get => GetValue(ActiveKeyProperty);
        set => SetValue(ActiveKeyProperty, value);
    }

    /// <summary>The declared tabs, which are the XAML content of the host:
    /// <c>&lt;controls:LazyTabHost&gt;&lt;controls:LazyTab …/&gt;…&lt;/controls:LazyTabHost&gt;</c>.
    /// Marked <see cref="ContentAttribute"/> so child <see cref="LazyTab"/>
    /// elements are appended here rather than reassigning the inherited
    /// <see cref="ContentControl.Content"/> (which would be a compile error
    /// for more than one child).</summary>
    [Content]
    public IList<LazyTab> TabTemplates => _tabTemplates;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // The cached controls were built against the previous DataContext; drop
        // them so the active tab is rebuilt against the new one. In practice the
        // ViewCache reuses a View for its (singleton) ViewModel, so the instance
        // is unchanged on re-navigation and the cache survives.
        _cache.Clear();
        Sync();
    }

    private void OnTabTemplatesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Sync();

    private void Sync()
    {
        var key = ActiveKey?.ToString();
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        LazyTab? match = null;
        foreach (var tab in _tabTemplates)
        {
            if (string.Equals(tab.Key, key, StringComparison.Ordinal))
            {
                match = tab;
                break;
            }
        }

        if (match is null)
        {
            return;
        }

        if (!_cache.TryGetValue(key, out var control))
        {
            control = match.Template?.Build(DataContext) ?? new Control();
            _cache[key] = control;
        }

        control.DataContext = DataContext;

        if (!ReferenceEquals(Content, control))
        {
            Content = control;
        }
    }
}
