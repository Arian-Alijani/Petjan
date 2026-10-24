using System.Text;

namespace Animora.Desktop.Services;

/// <summary>
/// Pure, UI-free keyboard-wedge detector. USB barcode scanners type their code like a keyboard — many
/// characters in a few milliseconds, terminated by <c>Enter</c>. A human never types that fast, so «a run of
/// printable characters with every inter-key gap at most <see cref="MaxInterKeyMs"/> that ends in Enter and is
/// at least <see cref="MinLength"/> long» is a scan. Time is injected so the rule is deterministic to reason about.
/// </summary>
internal sealed class BarcodeScanDetector
{
    /// <summary>Largest gap between two keys of one scan; a human keystroke cadence is far slower.</summary>
    public const int MaxInterKeyMs = 50;

    /// <summary>Shortest run accepted as a barcode (shorter fast bursts are ordinary typing).</summary>
    public const int MinLength = 4;

    private readonly StringBuilder _buffer = new();
    private long _lastMs = long.MinValue;

    /// <summary>True while a candidate burst has at least one buffered character.</summary>
    public bool HasBuffer => _buffer.Length > 0;

    /// <summary>Number of characters buffered in the current candidate burst.</summary>
    public int Length => _buffer.Length;

    /// <summary>Feeds one printable character. A slow key starts a fresh candidate burst.</summary>
    public void Feed(char c, long nowMs)
    {
        if (_buffer.Length > 0 && nowMs - _lastMs > MaxInterKeyMs)
        {
            _buffer.Clear();
        }

        _buffer.Append(c);
        _lastMs = nowMs;
    }

    /// <summary>
    /// Feeds <c>Enter</c>. Returns the scanned code when the buffered burst qualifies as a scan; otherwise null.
    /// The buffer is always cleared.
    /// </summary>
    public string? Complete(long nowMs)
    {
        var qualifies = _buffer.Length >= MinLength && nowMs - _lastMs <= MaxInterKeyMs;
        var code = qualifies ? _buffer.ToString() : null;
        Reset();
        return code;
    }

    /// <summary>Discards the current candidate burst (a non-text key, focus change, …).</summary>
    public void Reset()
    {
        _buffer.Clear();
        _lastMs = long.MinValue;
    }
}
