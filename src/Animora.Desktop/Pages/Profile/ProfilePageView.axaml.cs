using Avalonia.Controls;

namespace Animora.Desktop.Pages.Profile;

public partial class ProfilePageView : UserControl
{
    private readonly List<ResponsiveGrid> _cards;
    private bool? _compactNavigation;
    private bool? _stackedCards;

    public ProfilePageView()
    {
        InitializeComponent();
        _cards = new[] { AccountCards, QuickActions, ProfileCards, SchedulingCards, AlertCards,
            AppearanceCards, SecurityCards, AccessCards }.Select(grid => new ResponsiveGrid(grid)).ToList();
        SizeChanged += (_, _) => Reflow();
    }

    // Layout only: narrow windows move the section rail above the content and stack cards.
    // Original attached rows/columns are restored when space becomes available again.
    private void Reflow()
    {
        var compact = Bounds.Width < 980;
        if (_compactNavigation != compact)
        {
            _compactNavigation = compact;
            SettingsWorkspace.ColumnDefinitions = new ColumnDefinitions(compact ? "*" : "196,*");
            SettingsWorkspace.RowDefinitions = new RowDefinitions(compact ? "Auto,Auto" : "Auto");
            SettingsWorkspace.RowSpacing = compact ? 18 : 0;
            SectionNavigation.ItemsPanel = new Avalonia.Controls.Templates.FuncTemplate<Panel?>(() =>
                compact ? new WrapPanel() : new StackPanel { Spacing = 4 });
            Grid.SetColumn(SettingsContent, compact ? 0 : 1);
            Grid.SetRow(SettingsContent, compact ? 1 : 0);
        }
        var stacked = Bounds.Width - 48 - (compact ? 0 : 218) < 740;
        if (_stackedCards == stacked) return;
        _stackedCards = stacked;
        foreach (var grid in _cards) grid.Apply(stacked);
    }

    private sealed class ResponsiveGrid(Grid grid)
    {
        private readonly ColumnDefinitions _columns = grid.ColumnDefinitions;
        private readonly RowDefinitions _rows = grid.RowDefinitions;
        private readonly double _rowSpacing = grid.RowSpacing;
        private readonly (Control Child, int Row, int Column)[] _positions = grid.Children
            .Select(child => (child, Grid.GetRow(child), Grid.GetColumn(child))).ToArray();

        internal void Apply(bool stacked)
        {
            grid.ColumnDefinitions = stacked ? new ColumnDefinitions("*") : _columns;
            grid.RowDefinitions = stacked
                ? new RowDefinitions(string.Join(",", Enumerable.Repeat("Auto", _positions.Length))) : _rows;
            grid.RowSpacing = stacked ? 14 : _rowSpacing;
            for (var i = 0; i < _positions.Length; i++)
            {
                var item = _positions[i];
                Grid.SetRow(item.Child, stacked ? i : item.Row);
                Grid.SetColumn(item.Child, stacked ? 0 : item.Column);
            }
        }
    }
}
