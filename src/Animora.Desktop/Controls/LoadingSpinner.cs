using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Animora.Desktop.Controls;

/// <summary>
/// Round, comet-style loading indicator that is animated entirely on Avalonia's <b>render thread</b>
/// (a <see cref="CompositionCustomVisualHandler"/> driven by <c>OnAnimationFrameUpdate</c>), not by UI-thread
/// timers or XAML animations. That is the whole point: while the UI thread is busy building or laying out a heavy
/// destination page, the spinner keeps turning at full frame rate, so the app never looks frozen
/// (PERFORMANCE.md §17).
///
/// Cost model:
/// <list type="bullet">
/// <item>Idle (<see cref="IsActive"/> = false): zero. No frame callbacks are registered, nothing is drawn.</item>
/// <item>Active: one invalidation of a tiny (≈40 px) rect per frame; ~10 ellipses drawn with brushes and pens
/// that are allocated once per handler, so the render loop allocates nothing.</item>
/// <item>The UI thread is only involved when <see cref="IsActive"/> flips (one message to the render thread).</item>
/// </list>
/// </summary>
public sealed class LoadingSpinner : Control
{
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<LoadingSpinner, bool>(nameof(IsActive));

    // Brand600, used when the theme resource cannot be resolved (design-time / early startup).
    private static readonly Color FallbackColor = Color.FromRgb(0x0D, 0x94, 0x88);

    private CompositionCustomVisual? _visual;

    /// <summary>Starts/stops the animation. Inactive spinners cost nothing.</summary>
    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!TryCreateVisual())
        {
            // The compositor visual of this element can lag the attach by a tick; retry once, off the hot path.
            Dispatcher.UIThread.Post(() => TryCreateVisual(), DispatcherPriority.Loaded);
        }
    }

    private bool TryCreateVisual()
    {
        if (_visual is not null) return true;
        if (this.GetVisualRoot() is null) return false;

        var host = ElementComposition.GetElementVisual(this);
        if (host is null) return false;

        var color = this.TryFindResource("Brand600Color", out var resource) && resource is Color brand
            ? brand
            : FallbackColor;

        _visual = host.Compositor.CreateCustomVisual(new SpinnerHandler(color));
        ElementComposition.SetElementChildVisual(this, _visual);
        _visual.Size = new Vector(Bounds.Width, Bounds.Height);
        SendActive();
        return true;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // Stop the render-thread frame loop before the visual goes away.
        _visual?.SendHandlerMessage(false);
        ElementComposition.SetElementChildVisual(this, null);
        _visual = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 40 : Math.Min(availableSize.Width, 40),
            double.IsInfinity(availableSize.Height) ? 40 : Math.Min(availableSize.Height, 40));

    protected override Size ArrangeOverride(Size finalSize)
    {
        // finalSize, not Bounds: Bounds is only updated after Arrange returns.
        if (_visual is not null) _visual.Size = new Vector(finalSize.Width, finalSize.Height);
        return finalSize;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsActiveProperty) SendActive();
    }

    private void SendActive() => _visual?.SendHandlerMessage(IsActive);

    /// <summary>Render-thread drawing + animation clock. Touched only from the render thread.</summary>
    private sealed class SpinnerHandler : CompositionCustomVisualHandler
    {
        private const int TailLength = 10;
        private const double RevolutionMs = 900;
        private const double TailStep = 0.27; // radians between trailing dots (~15°)
        private const double HeadDotRadius = 3.6;
        private const double TailDotRadius = 1.6;

        private readonly ImmutableSolidColorBrush[] _dotBrushes = new ImmutableSolidColorBrush[TailLength];
        private readonly ImmutablePen _track;
        private bool _running;

        public SpinnerHandler(Color color)
        {
            for (var i = 0; i < TailLength; i++)
            {
                // Quadratic-ish falloff reads as a comet tail rather than a linear ramp.
                var fade = 1.0 - (double)i / TailLength;
                _dotBrushes[i] = new ImmutableSolidColorBrush(color, fade * fade + 0.04);
            }

            _track = new ImmutablePen(new ImmutableSolidColorBrush(color, 0.12), 2.5);
        }

        public override void OnMessage(object message)
        {
            if (message is not bool active) return;

            _running = active;
            if (active) RegisterForNextAnimationFrameUpdate();
            Invalidate(); // an inactive spinner draws nothing, so this also clears the last frame
        }

        public override void OnAnimationFrameUpdate()
        {
            if (!_running) return; // chain ends: no callbacks while idle

            Invalidate();
            RegisterForNextAnimationFrameUpdate();
        }

        public override void OnRender(ImmediateDrawingContext context)
        {
            if (!_running) return;

            var size = EffectiveSize;
            var side = Math.Min(size.X, size.Y);
            if (side <= HeadDotRadius * 2) return;

            var center = new Point(size.X / 2, size.Y / 2);
            var radius = side / 2 - HeadDotRadius - 0.5;

            context.DrawEllipse(null, _track, center, radius, radius);

            // Head angle derived from the compositor clock: continuous, drift-free and independent of the UI thread.
            var phase = CompositionNow.TotalMilliseconds % RevolutionMs / RevolutionMs;
            var head = phase * (Math.PI * 2) - Math.PI / 2;

            // Draw tail first so the head dot lands on top.
            for (var i = TailLength - 1; i >= 0; i--)
            {
                var angle = head - i * TailStep;
                var dot = TailDotRadius + (HeadDotRadius - TailDotRadius) * (1.0 - (double)i / (TailLength - 1));
                var p = new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
                context.DrawEllipse(_dotBrushes[i], null, p, dot, dot);
            }
        }
    }
}
