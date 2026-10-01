namespace Animora.Desktop.Pages.Dashboard;

/// <summary>
/// Pure layout of the revenue chart (no Avalonia types, unit-testable). Inputs are positions in the
/// signed domain [min(0,min), max(0,max)] normalized to 0..1 plus the zero level in the same space;
/// output is canvas coordinates where y grows downward. Values are never clamped.
/// </summary>
public static class RevenueChartMath
{
    public const double Width = 500;
    public const double Height = 180;
    public const double Top = 12;
    public const double Bottom = Height - 12;

    public readonly record struct Layout(double[] X, double[] Y, double ZeroY);

    /// <summary>Null when fewer than two points exist or any input is not a finite number.</summary>
    public static Layout? Build(System.Collections.Generic.IReadOnlyList<double>? points, double zeroLevel)
    {
        if (points is null || points.Count < 2 || !double.IsFinite(zeroLevel)) return null;
        var step = Width / (points.Count - 1);
        var x = new double[points.Count];
        var y = new double[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            if (!double.IsFinite(points[i])) return null;
            x[i] = i * step;
            y[i] = ToY(points[i]);
        }

        return new Layout(x, y, ToY(zeroLevel));
    }

    private static double ToY(double level) => Bottom - System.Math.Clamp(level, 0, 1) * (Bottom - Top);
}
