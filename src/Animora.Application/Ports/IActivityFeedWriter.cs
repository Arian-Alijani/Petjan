using Animora.Application.Activity;

namespace Animora.Application.Ports;

/// <summary>Stages a feed row in the caller's transaction without flushing changes.</summary>
public interface IActivityFeedWriter
{
    Task WriteAsync(ActivityEventDraft draft, CancellationToken ct);
}
