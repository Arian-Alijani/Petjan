using System;
using Avalonia;
using Avalonia.Controls;

namespace Animora.Desktop.Controls;

/// <summary>
/// The kinds of text a field accepts. Used by <see cref="InputGuard"/> to reject anything the field cannot
/// legally hold, so a wrong value can never reach a ViewModel (and from there the backend).
/// </summary>
public enum InputGuardKind
{
    /// <summary>Free text (default) — only the length cap applies.</summary>
    Any,

    /// <summary>Whole digits only (ASCII and Persian/Arabic-Indic digits are both accepted).</summary>
    Digits,

    /// <summary>A phone number: digits plus <c>+ ( ) -</c> and spaces.</summary>
    Phone,

    /// <summary>A decimal number: digits plus a single <c>.</c>.</summary>
    Decimal,
}

/// <summary>
/// Reusable input control for the whole app, declared on a <see cref="TextBox"/> in XAML:
/// <code>&lt;TextBox controls:InputGuard.Kind="Digits" controls:InputGuard.MaxLength="12" /&gt;</code>
/// <para>
/// <b>Type control.</b> Every text change (typing, pasting, IME, programmatic) is filtered to the declared
/// <see cref="InputGuardKind"/>; an illegal character is dropped instead of being stored, and the caret keeps its
/// position. Persian/Arabic-Indic digits are kept (the app's own formatters normalize them later), so the guard
/// never fights the RTL input the user actually types.
/// </para>
/// <para>
/// <b>Length control.</b> <see cref="MaxLengthProperty"/> caps the field (the same <see cref="TextBox.MaxLength"/>
/// the control itself enforces, declared here so one attribute expresses both rules).
/// </para>
/// </summary>
public static class InputGuard
{
    /// <summary>The characters a field accepts. Defaults to <see cref="InputGuardKind.Any"/>.</summary>
    public static readonly AttachedProperty<InputGuardKind> KindProperty =
        AvaloniaProperty.RegisterAttached<TextBox, InputGuardKind>("Kind", typeof(InputGuard), InputGuardKind.Any);

    /// <summary>The maximum number of characters the field accepts (<c>0</c> = the app-wide default).</summary>
    public static readonly AttachedProperty<int> MaxLengthProperty =
        AvaloniaProperty.RegisterAttached<TextBox, int>("MaxLength", typeof(InputGuard), 0);

    // Marks a box whose TextChanged subscription was already installed (an attached property can be set twice).
    private static readonly AttachedProperty<bool> WatchingProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("Watching", typeof(InputGuard), false);

    static InputGuard()
    {
        KindProperty.Changed.AddClassHandler<TextBox>((box, _) => Attach(box));
        MaxLengthProperty.Changed.AddClassHandler<TextBox>((box, _) => Attach(box));
    }

    public static void SetKind(TextBox box, InputGuardKind value) => box.SetValue(KindProperty, value);

    public static InputGuardKind GetKind(TextBox box) => box.GetValue(KindProperty);

    public static void SetMaxLength(TextBox box, int value) => box.SetValue(MaxLengthProperty, value);

    public static int GetMaxLength(TextBox box) => box.GetValue(MaxLengthProperty);

    private static void Attach(TextBox box)
    {
        var max = GetMaxLength(box);
        if (max > 0 && box.MaxLength != max)
        {
            box.MaxLength = max;
        }

        if (box.GetValue(WatchingProperty))
        {
            Sanitize(box);
            return;
        }

        box.SetValue(WatchingProperty, true);
        box.TextChanged += (_, _) => Sanitize(box);
        Sanitize(box);
    }

    /// <summary>Drops every character the field's <see cref="InputGuardKind"/> does not allow.</summary>
    private static void Sanitize(TextBox box)
    {
        var kind = GetKind(box);
        if (kind == InputGuardKind.Any)
        {
            return;
        }

        var text = box.Text ?? string.Empty;
        var clean = Filter(text, kind);
        if (string.Equals(clean, text, StringComparison.Ordinal))
        {
            return;
        }

        // Keep the caret where the user expects it: shift it by the number of characters removed before it.
        var caret = box.CaretIndex;
        box.Text = clean;
        box.CaretIndex = Math.Clamp(caret - (text.Length - clean.Length), 0, clean.Length);
    }

    private static string Filter(string text, InputGuardKind kind)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        var hasDot = false;

        foreach (var ch in text)
        {
            var keep = kind switch
            {
                InputGuardKind.Digits => char.IsDigit(ch),
                InputGuardKind.Phone => char.IsDigit(ch) || ch is '+' or '(' or ')' or '-' or ' ',
                InputGuardKind.Decimal => char.IsDigit(ch) || (ch == '.' && !hasDot),
                _ => true,
            };

            if (!keep)
            {
                continue;
            }

            if (ch == '.')
            {
                hasDot = true;
            }

            builder.Append(ch);
        }

        return builder.Length == text.Length ? text : builder.ToString();
    }
}
