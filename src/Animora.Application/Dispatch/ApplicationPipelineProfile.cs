namespace Animora.Application.Dispatch;

/// <summary>Selects which fixed behavior chain the dispatcher runs (D-S06-1).</summary>
public enum ApplicationPipelineProfile
{
    /// <summary>Desktop chain, including the local command journal. The default.</summary>
    Client = 0,

    /// <summary>Server chain: same handlers, no client-only <c>CommandJournalBehavior</c>.</summary>
    Server = 1,
}
