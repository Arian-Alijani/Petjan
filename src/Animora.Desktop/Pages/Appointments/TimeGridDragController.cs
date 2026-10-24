using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Animora.Desktop.Pages.Appointments;

/// <summary>
/// Owns the direct-manipulation pointer gestures of the day/week time grid
/// (<see cref="TimeGridSurface"/>): drag an existing card to a new slot
/// (reschedule) and drag out an empty range to start a new booking. All data
/// mutation is delegated to <see cref="AppointmentsCalendarViewModel"/>
/// (<c>MoveAppointmentFromDragAsync</c> / <c>CreateBookingFromDragAsync</c>); the
/// controller only interprets pixels into a snapped minute range and drives the
/// per-column ghost box visuals, so the View stays free of booking logic and the
/// ViewModel stays free of geometry.
///
/// <para>
/// A single pair of <em>tunnelling</em> pointer handlers is attached on the
/// surface, so the gesture is recognised before an event-card <see cref="Button"/>
/// swallows the press. A short movement threshold separates a <em>click</em>
/// (open detail / open booking — handled by the existing bound commands) from a
/// <em>drag</em>, so every existing interaction keeps working unchanged.
/// </para>
/// </summary>
internal sealed class TimeGridDragController
{
    /// <summary>Pixels the pointer must travel before a press is treated as a drag (click vs drag).</summary>
    private const double DragThreshold = 4;

    private readonly Control _host;

    // Live gesture state (null when idle). Captured on press, cleared on release/cancel.
    private DragSession? _session;

    public TimeGridDragController(Control host)
    {
        _host = host;
        // Tunnel so we observe the press before the event-card Button handles it; we only
        // "claim" the gesture once it crosses the drag threshold, so plain clicks pass through.
        _host.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        _host.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        _host.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);
        _host.AddHandler(InputElement.PointerCaptureLostEvent, OnPointerCaptureLost, RoutingStrategies.Bubble);
    }

    private AppointmentsCalendarViewModel? ViewModel => _host.DataContext as AppointmentsCalendarViewModel;

    // ── gesture lifecycle ────────────────────────────────────────────────────────
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Left button only; never start a gesture the page cannot support (month / day-only).
        if (!e.GetCurrentPoint(_host).Properties.IsLeftButtonPressed) return;
        if (ViewModel is not { SupportsTimeGridDrag: true } vm) return;
        if (e.Source is not Visual source) return;

        // The column panel the press landed in (Tag="drag-surface"), and its column VM.
        var surface = FindColumnSurface(source);
        if (surface?.DataContext is not AppointmentColumnViewModel column) return;

        var origin = e.GetPosition(surface);

        // Did the press begin on a movable event card? (null → empty range → create gesture)
        var card = FindEventCard(source);
        var mode = card is { } c && c.Model is { IsReadOnlyProjection: false } && c.Model.CanMove
            ? DragMode.Move
            : DragMode.Create;

        _session = new DragSession(vm, column, surface, origin, mode, card);
        // Note: we do NOT capture yet and do NOT mark handled — a plain click must still reach the
        // card Button / the empty-cell command. Capture is taken the moment the drag threshold trips.
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_session is not { } session) return;

        var point = e.GetPosition(session.Surface);

        if (!session.IsDragging)
        {
            var delta = point - session.Origin;
            if (Math.Abs(delta.Y) < DragThreshold && Math.Abs(delta.X) < DragThreshold) return;

            // Threshold crossed → promote to a real drag: now claim the pointer so the card Button
            // (or the empty cell) will not fire its click, and show the ghost box.
            session.IsDragging = true;
            e.Pointer.Capture(session.Surface);
            session.Box = FindDragBox(session.Surface);
            session.Label = FindDragBoxLabel(session.Surface);
            if (session.Box is { } box)
            {
                // Toggle a "visible" class (box stays in layout) rather than IsVisible, so the
                // fade/scale entrance transition actually tweens — mirrors the toast/drawer pattern.
                box.Classes.Set("moving", session.Mode == DragMode.Move);
                box.Classes.Set("creating", session.Mode == DragMode.Create);
                box.Classes.Set("visible", true);
            }
        }

        UpdateGhost(session, point);
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_session is not { } session)
        {
            return;
        }

        _session = null;

        if (!session.IsDragging)
        {
            // A click, not a drag: do NOT clear the pointer capture and do NOT mark handled.
            // The press never captured here (capture is only taken once the drag threshold trips
            // in OnPointerMoved), so releasing capture now would run in the tunnel phase *before*
            // the event-card Button's bubbling release and drop the Button's own capture — which
            // cancels its Click and prevents OpenBookingCommand (card → detail) from ever firing.
            // Leaving everything untouched lets the Button raise Click normally (empty space is
            // intentionally inert: creation happens via drag-to-create or the toolbar button).
            return;
        }

        // Real drag: we own the gesture (surface captured in OnPointerMoved) — tear it down here.
        HideBox(session);
        e.Pointer.Capture(null);
        e.Handled = true;

        var (startMinutes, durationMinutes) = session.ResolveRange(e.GetPosition(session.Surface));

        if (session.Mode == DragMode.Move && session.Card is { } card)
        {
            _ = session.ViewModel.MoveAppointmentFromDragAsync(
                card, session.Column.Date, session.Column.DoctorId, startMinutes);
        }
        else
        {
            _ = session.ViewModel.CreateBookingFromDragAsync(
                session.Column.Date, session.Column.DoctorId, startMinutes, durationMinutes);
        }
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_session is { } session)
        {
            HideBox(session);
        }

        _session = null;
    }

    // ── ghost box geometry ───────────────────────────────────────────────────────
    private static void UpdateGhost(DragSession session, Point point)
    {
        if (session.Box is not { } box) return;

        var (startMinutes, durationMinutes) = session.ResolveRange(point);
        var vm = session.ViewModel;

        var top = (startMinutes - vm.GridWindowStartMinutes) / 60.0 * vm.GridPixelsPerHour;
        var height = durationMinutes / 60.0 * vm.GridPixelsPerHour;

        box.Margin = new Thickness(0, Math.Max(0, top), 0, 0);
        box.Height = Math.Max(1, height);

        if (session.Label is { } label)
        {
            // Show the time range plus the live minute count so the user sees exactly how long the
            // dragged-out range is and watches it grow/shrink while dragging (e.g. «۰۹:۰۰ – ۰۹:۴۵ · ۴۵ دقیقه»).
            label.Text = $"{FormatTime(startMinutes)} – {FormatTime(startMinutes + durationMinutes)} · " +
                         $"{AppointmentsPresentation.Fa(durationMinutes)} دقیقه";
        }
    }

    private static void HideBox(DragSession session)
    {
        if (session.Box is { } box)
        {
            box.Classes.Remove("visible");
            box.Classes.Remove("moving");
            box.Classes.Remove("creating");
            box.Height = 0;
        }
    }

    // ── small visual-tree helpers ────────────────────────────────────────────────
    private static Panel? FindColumnSurface(Visual from)
    {
        foreach (var ancestor in from.GetSelfAndVisualAncestors())
        {
            if (ancestor is Panel { Tag: "drag-surface" } panel)
            {
                return panel;
            }
        }

        return null;
    }

    private static AppointmentEventViewModel? FindEventCard(Visual from)
    {
        foreach (var ancestor in from.GetSelfAndVisualAncestors())
        {
            if (ancestor is Button { Tag: "event-card" } button &&
                button.DataContext is AppointmentEventViewModel card)
            {
                return card;
            }

            // Stop climbing once we reach the column surface — the press was on empty space.
            if (ancestor is Panel { Tag: "drag-surface" })
            {
                break;
            }
        }

        return null;
    }

    // DragBox is a direct child of the column surface, so a child scan is namescope-independent
    // (FindControl relies on the template's NameScope, which is brittle across recycled items).
    private static Border? FindDragBox(Panel surface)
    {
        foreach (var child in surface.Children)
        {
            if (child is Border { Name: "DragBox" } box)
            {
                return box;
            }
        }

        return null;
    }

    private static TextBlock? FindDragBoxLabel(Panel surface) =>
        FindDragBox(surface)?.Child as TextBlock;

    private static string FormatTime(int minutes)
    {
        var m = Math.Clamp(minutes, 0, 24 * 60);
        return AppointmentsPresentation.Time(m);
    }

    private enum DragMode
    {
        Create,
        Move,
    }

    /// <summary>One in-flight pointer gesture: its target column, origin and (for a move) the dragged card.</summary>
    private sealed class DragSession(
        AppointmentsCalendarViewModel viewModel,
        AppointmentColumnViewModel column,
        Panel surface,
        Point origin,
        DragMode mode,
        AppointmentEventViewModel? card)
    {
        public AppointmentsCalendarViewModel ViewModel { get; } = viewModel;
        public AppointmentColumnViewModel Column { get; } = column;
        public Panel Surface { get; } = surface;
        public Point Origin { get; } = origin;
        public DragMode Mode { get; } = mode;
        public AppointmentEventViewModel? Card { get; } = card;

        public bool IsDragging { get; set; }
        public Border? Box { get; set; }
        public TextBlock? Label { get; set; }

        /// <summary>
        /// Maps the gesture to a snapped (start, duration) minute range.
        /// <list type="bullet">
        /// <item><b>Create:</b> the range spans origin→current, snapped to the grid granularity, with a
        /// sensible minimum so a tiny drag still produces a bookable slot.</item>
        /// <item><b>Move:</b> the card keeps its own duration; only the start follows the pointer, offset by
        /// where inside the card the drag began so the card does not jump under the cursor.</item>
        /// </list>
        /// </summary>
        public (int StartMinutes, int DurationMinutes) ResolveRange(Point current)
        {
            var snap = Math.Max(1, ViewModel.GridSnapMinutes);
            var winStart = ViewModel.GridWindowStartMinutes;
            var winEnd = ViewModel.GridWindowEndMinutes;
            var pxPerMinute = ViewModel.GridPixelsPerHour / 60.0;

            int MinutesAt(double y) => winStart + (int)Math.Round(y / pxPerMinute);
            int SnapTo(int minutes) => (int)Math.Round(minutes / (double)snap) * snap;

            if (Mode == DragMode.Move && Card is { } card)
            {
                var duration = Math.Max(snap, card.Model.DurationMinutes);
                // Offset by the grab point inside the card so it tracks the cursor naturally.
                var cardTopPx = (card.Model.StartMinutes - winStart) * pxPerMinute;
                var grabOffsetPx = Math.Clamp(Origin.Y - cardTopPx, 0, card.Height);
                var start = SnapTo(MinutesAt(current.Y - grabOffsetPx));
                start = Math.Clamp(start, winStart, Math.Max(winStart, winEnd - duration));
                return (start, duration);
            }

            // Create: normalise origin/current into a range.
            var a = SnapTo(MinutesAt(Origin.Y));
            var b = SnapTo(MinutesAt(current.Y));
            var startMinutes = Math.Min(a, b);
            var endMinutes = Math.Max(a, b);

            // A near-zero drag still yields a usable default-length slot.
            if (endMinutes - startMinutes < snap)
            {
                endMinutes = startMinutes + Math.Max(snap, ViewModel.GridDefaultDurationMinutes);
            }

            startMinutes = Math.Clamp(startMinutes, winStart, Math.Max(winStart, winEnd - snap));
            endMinutes = Math.Clamp(endMinutes, startMinutes + snap, winEnd);
            return (startMinutes, endMinutes - startMinutes);
        }
    }
}
