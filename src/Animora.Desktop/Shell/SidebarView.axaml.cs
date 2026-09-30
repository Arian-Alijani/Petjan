using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media.Transformation;

namespace Animora.Desktop.Shell;

public partial class SidebarView : UserControl
{
    /// <summary>
    /// Horizontal shift (px) applied to the fixed-right icon so it glides to the
    /// visual centre of the compact rail when collapsed, instead of snapping
    /// there via an alignment change. Every icon stays right-anchored (so they
    /// remain perfectly column-aligned while expanded) and simply translates as
    /// one, tweened by a matching transform Transition.
    ///
    /// Geometry: compact rail = 76px, Border padding 14+14 ⇒ 48px content;
    /// Button padding 12+12 ⇒ 24px content row. An 18px icon right-anchored in
    /// that 24px row has its centre 9px from the right edge, i.e. at x=15 from
    /// the left; the row centre is x=12, so a 3px shift toward the leading
    /// (left, in this LTR grid) edge lands the icon dead-centre.
    /// Expanded ⇒ no shift (icons stay flush on the right column).
    ///
    /// Returns a <see cref="TransformOperations"/> (translateX) rather than a
    /// raw double so it can be animated by a <c>TransformOperationsTransition</c>
    /// — Avalonia only tweens render transforms expressed as transform
    /// operations, not a bound <c>TranslateTransform.X</c>.
    /// </summary>
    public static readonly IValueConverter RailIconShiftConverter =
        new FuncValueConverter<bool, TransformOperations>(expanded =>
        {
            var builder = TransformOperations.CreateBuilder(1);
            // Negative X moves left (toward the rail centre) in this LTR grid.
            builder.AppendTranslate(expanded ? 0d : -3d, 0d);
            return builder.Build();
        });

    /// <summary>
    /// Full opacity when the sidebar is expanded, fully transparent when
    /// collapsed. Paired with an Opacity transition on the label so they
    /// fade out (rather than snap out) in lock-step with the width tween,
    /// keeping the icon from appearing to jump.
    /// </summary>
    public static readonly IValueConverter ExpandedOpacityConverter =
        new FuncValueConverter<bool, double>(expanded => expanded ? 1d : 0d);

    /// <summary>
    /// Inverse of <see cref="ExpandedOpacityConverter"/>: opaque when collapsed,
    /// transparent when expanded. Drives the collapsed-only brand logo layer so
    /// it cross-fades against the expanded layer instead of snapping in.
    /// </summary>
    public static readonly IValueConverter CollapsedOpacityConverter =
        new FuncValueConverter<bool, double>(expanded => expanded ? 0d : 1d);

    /// <summary>Inverts a single boolean (visible only when collapsed).</summary>
    public static readonly IValueConverter InverseBoolConverter =
        new FuncValueConverter<bool, bool>(v => !v);

    public SidebarView() => InitializeComponent();

}
