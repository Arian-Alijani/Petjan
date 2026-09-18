using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Animora.Desktop.Controls;

/// <summary>One inline span of a <see cref="RichLine"/>: text plus whether it is bold.</summary>
public sealed record RichSpan(string Text, bool Bold);

/// <summary>
/// A wrapping paragraph built from a list of <see cref="RichSpan"/> runs, so a
/// line can interleave normal and bold text with correct RTL flowing — the
/// Finance «کشف کلیدی» highlight and the insight cards (Animora-UI/assets/
/// finance-page.js <c>keyHighlight</c>/<c>insightsRow</c>, where key figures are
/// wrapped in <c>&lt;b&gt;</c>). Avalonia has no inline HTML, so the JS string is
/// supplied as typed spans and rendered as a <see cref="TextBlock"/> whose
/// <see cref="Inline"/>s carry the bold weight + ink color.
/// </summary>
public sealed class RichLine : ContentControl
{
    public static readonly StyledProperty<IReadOnlyList<RichSpan>?> SpansProperty =
        AvaloniaProperty.Register<RichLine, IReadOnlyList<RichSpan>?>(nameof(Spans));

    public static readonly StyledProperty<IBrush> BodyBrushProperty =
        AvaloniaProperty.Register<RichLine, IBrush>(nameof(BodyBrush),
            new SolidColorBrush(Color.Parse("#344054")));

    public static readonly StyledProperty<IBrush> BoldBrushProperty =
        AvaloniaProperty.Register<RichLine, IBrush>(nameof(BoldBrush),
            new SolidColorBrush(Color.Parse("#101828")));

    static RichLine()
    {
        AffectsRender<RichLine>(SpansProperty);
    }

    public IReadOnlyList<RichSpan>? Spans
    {
        get => GetValue(SpansProperty);
        set => SetValue(SpansProperty, value);
    }

    public IBrush BodyBrush
    {
        get => GetValue(BodyBrushProperty);
        set => SetValue(BodyBrushProperty, value);
    }

    public IBrush BoldBrush
    {
        get => GetValue(BoldBrushProperty);
        set => SetValue(BoldBrushProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SpansProperty || change.Property == BodyBrushProperty || change.Property == BoldBrushProperty)
        {
            Rebuild();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Rebuild();
    }

    private void Rebuild()
    {
        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            LineHeight = 22,
            Foreground = BodyBrush,
            FlowDirection = FlowDirection.RightToLeft,
        };

        var spans = Spans;
        if (spans is not null)
        {
            foreach (var span in spans)
            {
                var run = new Run(span.Text) { Foreground = BodyBrush };
                if (span.Bold)
                {
                    run.FontWeight = FontWeight.ExtraBold;
                    run.Foreground = BoldBrush;
                }

                text.Inlines!.Add(run);
            }
        }

        Content = text;
    }
}

