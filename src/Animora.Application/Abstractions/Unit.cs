namespace Animora.Application.Abstractions;

/// <summary>The "no value" result of a command that only succeeds or fails.</summary>
public readonly record struct Unit
{
    public static readonly Unit Value;
}
