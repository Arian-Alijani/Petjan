using Avalonia;
using Avalonia.Controls;

namespace Animora.Desktop.Controls;

/// <summary>
/// Shared page load-state block: indeterminate bar while busy plus the state text. Bind
/// <c>IsVisible</c> to the page's "state visible" flag, <see cref="IsBusy"/> and <see cref="StateText"/>.
/// </summary>
public partial class PageLoadingState : UserControl
{
    public static readonly StyledProperty<bool> IsBusyProperty =
        AvaloniaProperty.Register<PageLoadingState, bool>(nameof(IsBusy));

    public static readonly StyledProperty<string?> StateTextProperty =
        AvaloniaProperty.Register<PageLoadingState, string?>(nameof(StateText));

    public bool IsBusy
    {
        get => GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public string? StateText
    {
        get => GetValue(StateTextProperty);
        set => SetValue(StateTextProperty, value);
    }

    public PageLoadingState()
    {
        InitializeComponent();
        Refresh();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsBusyProperty || change.Property == StateTextProperty) Refresh();
    }

    private void Refresh()
    {
        Progress.IsVisible = IsBusy;
        StateTextBlock.Text = StateText;
    }
}
