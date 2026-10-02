using System;
using Animora.Desktop.Pages.Appointments;
using Avalonia;
using Avalonia.Controls;

namespace Animora.Desktop.Controls;

/// <summary>
/// Arranges appointment event cards on a day/week time-grid column using the
/// exact geometry the reference computes in <c>evCard(a)</c>
/// (Animora-UI/assets/appointments.js):
/// <c>top:&lt;h&gt;px; right:calc(lane*w% + 4px); width:calc(w% - 8px)</c> where
/// <c>w = 100/lanes</c>. The panel reads each child's <see cref="AppointmentEventViewModel"/>
/// (via its DataContext) for the pre-computed <c>Top</c>, <c>Height</c>,
/// <c>Lane</c> and <c>Lanes</c>, and mirrors the cards from the inline-start
/// (right, in RTL) edge — so overlapping appointments pack into lanes exactly
/// like the reference without any JS-style math in the view.
/// </summary>
public sealed class TimeGridEventsPanel : Panel
{
    /// <summary>Smallest readable card width in a crowded lane cluster (A22).</summary>
    private const double MinCardWidth = 44;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;
        foreach (var child in Children)
        {
            if (child.DataContext is AppointmentEventViewModel vm && width > 0)
            {
                var laneWidth = width / Math.Max(1, vm.Lanes);
                child.Measure(new Size(Math.Max(MinCardWidth, laneWidth), Math.Max(0, vm.Height)));
            }
            else
            {
                child.Measure(new Size(0, 0));
            }
        }

        // A Panel's MeasureOverride MUST return a finite size: returning an
        // unbounded (infinite) availableSize throws
        // "InvalidOperationException: Invalid size returned for Measure".
        // Constrain both axes to the panel's own explicit height (BodyHeight) /
        // the finite available width, falling back to 0 when unbounded.
        var ownHeight = double.IsNaN(Height) ? height : Height;
        return new Size(width, ownHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            if (child.DataContext is not AppointmentEventViewModel vm)
            {
                child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }

            var laneWidth = finalSize.Width / Math.Max(1, vm.Lanes);
            var rightInset = (vm.Lane * laneWidth) + 4;
            // A22: never shrink a card to nothing in a dense cluster; keep a readable minimum and clamp inside.
            var width = Math.Min(Math.Max(MinCardWidth, laneWidth - 8), Math.Max(0, finalSize.Width - 8));
            var x = Math.Max(0, finalSize.Width - rightInset - width);
            child.Arrange(new Rect(x, vm.Top, width, vm.Height));
        }

        return finalSize;
    }
}
