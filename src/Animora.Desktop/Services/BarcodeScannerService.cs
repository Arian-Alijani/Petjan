using System;
using System.Diagnostics;
using Animora.Desktop.Controls;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Animora.Desktop.Services;

/// <summary>
/// Central keyboard-wedge barcode scanner service (singleton). Attached once to the shell window, it watches
/// every key press (tunnelling, so it works wherever focus is) and raises <see cref="BarcodeScanned"/> when a
/// burst of fast keystrokes ends in Enter (see <see cref="BarcodeScanDetector"/>). Pages subscribe only while
/// they are the current page; with no subscriber the service does nothing, so the scanner behaves as a plain
/// keyboard (e.g. typing into the product form's barcode field).
/// <para>
/// A detected scan never lands in a text field: the burst's characters are let through while they arrive (a
/// scan is only recognisable at its Enter), so when the scan completes the focused <see cref="TextBox"/> is
/// restored to what it held before the burst and the Enter is swallowed. Boxes marked with
/// <see cref="BarcodeField.InputBoxClass"/> are exempt — a scan typed there is genuine field input.
/// </para>
/// </summary>
public sealed class BarcodeScannerService
{
    private readonly BarcodeScanDetector _detector = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    // The focused text box (and its text) captured when the current burst began, restored on a completed scan.
    private TextBox? _burstBox;
    private string? _burstText;
    private int _burstCaret;

    /// <summary>Raised on the UI thread with the scanned code.</summary>
    public event Action<string>? BarcodeScanned;

    /// <summary>Starts watching <paramref name="target"/> (normally the shell window); dispose to stop.</summary>
    public IDisposable Attach(InputElement target)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        return new Detach(target, this);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (BarcodeScanned is null)
        {
            _detector.Reset();
            return;
        }

        // A field that wants the scan as input (the product barcode box) is never intercepted.
        if (e.Source is TextBox box && box.Classes.Contains(BarcodeField.InputBoxClass))
        {
            _detector.Reset();
            return;
        }

        var now = _clock.ElapsedMilliseconds;
        if (e.Key is Key.Enter or Key.Return)
        {
            Complete(e, now);
            return;
        }

        if (e.Key is Key.LeftShift or Key.RightShift or Key.CapsLock)
        {
            return; // modifiers legitimately sit inside a scan (upper-case letters)
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            _detector.Reset();
            return;
        }

        var ch = CharOf(e);
        if (ch is null)
        {
            _detector.Reset();
            return;
        }

        if (!_detector.HasBuffer && e.Source is TextBox first)
        {
            _burstBox = first;
            _burstText = first.Text;
            _burstCaret = first.CaretIndex;
        }

        _detector.Feed(ch.Value, now);
    }

    private void Complete(KeyEventArgs e, long now)
    {
        var code = _detector.Complete(now);
        var box = _burstBox;
        var text = _burstText;
        var caret = _burstCaret;
        _burstBox = null;
        _burstText = null;

        if (code is null)
        {
            return;
        }

        if (box is not null && text is not null)
        {
            box.Text = text;
            box.CaretIndex = Math.Min(caret, text.Length);
        }

        e.Handled = true;
        BarcodeScanned?.Invoke(code);
    }

    /// <summary>The printable character of a key press, or null for non-text keys (arrows, F-keys, …).</summary>
    private static char? CharOf(KeyEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.KeySymbol) && e.KeySymbol.Length == 1 && !char.IsControl(e.KeySymbol[0]))
        {
            return e.KeySymbol[0];
        }

        return e.Key switch
        {
            >= Key.D0 and <= Key.D9 => (char)('0' + (e.Key - Key.D0)),
            >= Key.NumPad0 and <= Key.NumPad9 => (char)('0' + (e.Key - Key.NumPad0)),
            >= Key.A and <= Key.Z => (char)((e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 'A' : 'a') + (e.Key - Key.A)),
            Key.OemMinus or Key.Subtract => '-',
            _ => null,
        };
    }

    private sealed class Detach(InputElement target, BarcodeScannerService owner) : IDisposable
    {
        private InputElement? _target = target;

        public void Dispose()
        {
            if (_target is null)
            {
                return;
            }

            _target.RemoveHandler(InputElement.KeyDownEvent, owner.OnKeyDown);
            _target = null;
            owner._detector.Reset();
        }
    }
}
