using Animora.Application.Abstractions;

namespace Animora.Application.UseCases.Diagnostics;

/// <summary>Reference query proving the dispatch chain end to end. No business behavior.</summary>
public sealed record PingQuery : IQuery<string>;
