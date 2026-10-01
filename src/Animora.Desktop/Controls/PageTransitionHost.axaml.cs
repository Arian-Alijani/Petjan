using System;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;

namespace Animora.Desktop.Controls;

/// <summary>
/// Hosts the current page and swaps it behind a "frosted veil": the old page blurs, a round spinner fades in, the
/// destination page is built <em>while the veil is already on screen</em>, then the veil dissolves and the new page
/// sharpens into view.
///
/// Why it stays smooth even when the destination page is heavy (PERFORMANCE.md §17):
/// <list type="number">
/// <item><b>Veil first, heavy work second.</b> The page swap (view build, measure, arrange) is deferred until the
/// veil and blur have been committed to the render thread - the swap is the <em>last</em> thing that happens, not
/// the first.</item>
/// <item><b>Render-thread motion.</b> The veil fade is a composition implicit animation and the spinner is a custom
/// composition visual (<see cref="LoadingSpinner"/>). Both are advanced by the compositor, so they keep animating at
/// full frame rate while the UI thread is blocked building the page. Nothing animated here depends on the UI thread
/// except the blur radius, which is deliberately held constant during the heavy step.</item>
/// <item><b>Cheap blur.</b> A real blur is the only expensive effect here, so it is never animated continuously:
/// the radius moves through <see cref="BlurLevels"/> discrete steps (≈3 re-rasters per direction) and is removed
/// completely (<c>Effect = null</c>) at rest. Nothing is cached, nothing is snapshotted, no bitmaps are allocated.</item>
/// <item><b>Zero idle cost.</b> One animation-frame callback is registered only while a transition runs; at rest
/// the veil is collapsed, the spinner is stopped, the blur is gone.</item>
/// <item><b>Latest wins.</b> Navigating again mid-transition just retargets: only the newest page is ever built,
/// intermediate pages are never realised.</item>
/// </list>
///
/// Phases: <c>Entering</c> (veil fades in, blur ramps up) → <c>Covered</c> (one frame later: swap the page, the
/// expensive step) → <c>Settling</c> (one frame so the heavy layout is behind us) → <c>Exiting</c> (veil fades out,
/// blur ramps down) → <c>Idle</c>.
/// </summary>
public partial class PageTransitionHost : UserControl
{
    public static readonly StyledProperty<object?> PageContentProperty =
        AvaloniaProperty.Register<PageTransitionHost, object?>(nameof(PageContent));

    // Blur: BlurLevels discrete radii (Step, 2*Step, ...) so a ramp is a handful of re-rasters, not one per frame.
    private const int BlurLevels = 3;
    private const double BlurStep = 3.0;

    // Frame-driven ramps (blur) and the render-thread veil fades. The veil fade-out is slightly shorter than the
    // exit ramp so the veil is fully transparent by the time it is collapsed.
    private static readonly TimeSpan EnterRamp = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan ExitRamp = TimeSpan.FromMilliseconds(170);
    private static readonly TimeSpan VeilFadeIn = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan VeilFadeOut = TimeSpan.FromMilliseconds(150);

    private enum Phase { Idle, Entering, Covered, Settling, Exiting }

    private Phase _phase;
    private object? _target;          // newest requested page (latest wins)
    private object? _shown;           // page currently hosted by the stage
    private bool _hasShown;
    private TimeSpan? _phaseStart;    // frame time of the first frame of the current ramp
    private bool _frameQueued;
    private int _blurLevel;
    private BlurEffect?[]? _blurs;    // index = level; allocated once, effects reused

    private ImplicitAnimationCollection? _fadeIn;
    private ImplicitAnimationCollection? _fadeOut;

    /// <summary>The page ViewModel to display (resolved to a View by the app's <c>ViewLocator</c>).</summary>
    public object? PageContent
    {
        get => GetValue(PageContentProperty);
        set => SetValue(PageContentProperty, value);
    }

    public PageTransitionHost()
    {
        InitializeComponent();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // No frame callback can fire for a detached host, so settle immediately: whatever was requested is shown,
        // and nothing (veil, blur, spinner) is left running. The composition objects belong to the old compositor
        // and are rebuilt lazily on the next attach.
        if (_phase != Phase.Idle) FinishImmediately();
        _fadeIn = _fadeOut = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PageContentProperty) OnPageContentChanged(change.NewValue);
    }

    // ---- request handling ---------------------------------------------------------------------------------------

    private void OnPageContentChanged(object? value)
    {
        _target = value;

        // First page, a null page, or a host that cannot animate (not attached / not visible): swap directly.
        // The first page is the startup path (PERFORMANCE.md §12) and must not be delayed by a veil.
        if (!_hasShown || value is null || TopLevel.GetTopLevel(this) is null || !IsEffectivelyVisible)
        {
            if (_phase != Phase.Idle) FinishImmediately();
            else ApplyTarget();
            return;
        }

        switch (_phase)
        {
            case Phase.Idle:
                if (!ReferenceEquals(value, _shown)) BeginEnter();
                break;

            case Phase.Entering:
                // Latest wins: the Covered step will build whatever _target is by then.
                break;

            case Phase.Covered:
            case Phase.Settling:
                // The veil is fully up: just build the newer page on the next frame (the older one is never built).
                _phase = Phase.Covered;
                RequestFrame();
                break;

            case Phase.Exiting:
                // Retarget while dissolving: raise the veil again (the implicit animation continues from the
                // current opacity, so there is no jump) and rebuild behind it.
                BeginEnter();
                break;
        }
    }

    // ---- phase transitions --------------------------------------------------------------------------------------

    private void BeginEnter()
    {
        _phase = Phase.Entering;
        _phaseStart = null;

        Veil.IsVisible = true;
        EnsureVeilAnimations();
        Spinner.IsActive = true;
        SetVeilAnimation(_fadeIn);
        Veil.Opacity = 1;

        RequestFrame();
    }

    private void BeginExit()
    {
        _phase = Phase.Exiting;
        _phaseStart = null;

        SetVeilAnimation(_fadeOut);
        Veil.Opacity = 0;
    }

    private void Finish()
    {
        _phase = Phase.Idle;
        _phaseStart = null;

        Veil.IsVisible = false;   // collapses the veil: no layout, hit-testing or rendering at rest
        Spinner.IsActive = false; // stops the render-thread frame loop of the spinner
        SetBlurLevel(0);
    }

    private void FinishImmediately()
    {
        ApplyTarget();
        Veil.Opacity = 0;
        Finish();
    }

    // The only heavy step of the whole transition: building / measuring / arranging the destination page.
    private void ApplyTarget()
    {
        if (_hasShown && ReferenceEquals(_shown, _target)) return;

        Stage.Content = _target;
        _shown = _target;
        _hasShown = true;

        // A new page always starts at the top (the previous offset belongs to the old page).
        Scroller.Offset = default;
    }

    // ---- frame loop (registered only while a transition runs) ---------------------------------------------------

    private void RequestFrame()
    {
        if (_frameQueued) return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        _frameQueued = true;
        top.RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan now)
    {
        _frameQueued = false;

        switch (_phase)
        {
            case Phase.Entering:
            {
                _phaseStart ??= now;
                var t = Progress(now, EnterRamp);
                // Only ever raise the level here (a retarget during Exiting resumes from the current blur).
                var level = (int)Math.Ceiling(EaseOut(t) * BlurLevels);
                if (level > _blurLevel) SetBlurLevel(level);
                if (t >= 1) _phase = Phase.Covered;
                break;
            }

            case Phase.Covered:
                // The veil + full blur were committed in the previous frame; the render thread keeps presenting
                // them (and spinning) while the UI thread now does the expensive swap.
                ApplyTarget();
                _phase = Phase.Settling;
                break;

            case Phase.Settling:
                // The heavy layout of the swap ran in the previous frame; now dissolve the veil.
                BeginExit();
                break;

            case Phase.Exiting:
            {
                _phaseStart ??= now; // first frame after the swap starts a clean ramp, whatever the stall was
                var t = Progress(now, ExitRamp);
                var level = (int)Math.Ceiling((1 - EaseOut(t)) * BlurLevels);
                if (level < _blurLevel) SetBlurLevel(level);
                if (t >= 1)
                {
                    Finish();
                    return;
                }
                break;
            }

            default:
                return;
        }

        RequestFrame();
    }

    private double Progress(TimeSpan now, TimeSpan duration) =>
        _phaseStart is { } start ? Math.Clamp((now - start) / duration, 0d, 1d) : 0d;

    private static double EaseOut(double t)
    {
        var u = 1 - t;
        return 1 - u * u * u; // cubic ease-out: quick start, soft settle
    }

    // ---- blur ---------------------------------------------------------------------------------------------------

    private void SetBlurLevel(int level)
    {
        level = Math.Clamp(level, 0, BlurLevels);
        if (level == _blurLevel) return; // §13: no redundant invalidation

        _blurLevel = level;
        if (level == 0)
        {
            Scroller.Effect = null; // at rest there is no effect layer at all
            return;
        }

        _blurs ??= new BlurEffect?[BlurLevels + 1];
        Scroller.Effect = _blurs[level] ??= new BlurEffect { Radius = level * BlurStep };
    }

    // ---- render-thread veil fades -------------------------------------------------------------------------------

    // Built lazily (first transition), not at attach: child composition visuals are not guaranteed to exist yet while
    // the parent's attach handler runs, and a host that never transitions should not allocate anything.
    private void EnsureVeilAnimations()
    {
        if (_fadeIn is not null) return;

        var visual = ElementComposition.GetElementVisual(Veil);
        if (visual is null) return; // no compositor yet (design time): the veil simply appears/disappears without a fade

        var compositor = visual.Compositor;
        _fadeIn = CreateFade(compositor, VeilFadeIn, new CubicEaseOut());
        _fadeOut = CreateFade(compositor, VeilFadeOut, new CubicEaseInOut());
    }

    // "this.FinalValue" makes one animation reusable for any start/end opacity and lets an interrupted fade reverse
    // smoothly from wherever it currently is. It is evaluated by the compositor, not by the UI thread.
    private static ImplicitAnimationCollection CreateFade(Compositor compositor, TimeSpan duration, Easing easing)
    {
        var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.Target = "Opacity";
        animation.Duration = duration;
        animation.InsertExpressionKeyFrame(1f, "this.FinalValue", easing);

        var collection = compositor.CreateImplicitAnimationCollection();
        collection["Opacity"] = animation;
        return collection;
    }

    private void SetVeilAnimation(ImplicitAnimationCollection? animations)
    {
        if (animations is null) return;
        var visual = ElementComposition.GetElementVisual(Veil);
        if (visual is not null) visual.ImplicitAnimations = animations;
    }
}
