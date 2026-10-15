namespace Animora.Server.Hosting;

/// <summary>
/// The server's reserved route prefixes (ARCHITECTURE.md §4). S01 only declares
/// the constants; later phases map endpoints under them (commands → S06,
/// sync → S11/S12, auth → S04, operator → S14, admin → S19). This is the single
/// source other server phases reference for a surface prefix.
/// </summary>
internal static class ServerRoutes
{
    public const string Commands = "/api/commands";
    public const string Sync = "/sync";

    /// <summary>D04 authoritative financial read path (invoices/payments since a cursor); separate from command intake and the state feed.</summary>
    public const string Finance = "/finance";
    public const string Auth = "/auth";
    public const string Operator = "/operator";
    public const string Provisioning = "/provisioning";
    public const string Licensing = "/licensing";
    public const string Files = "/files";
    public const string Admin = "/admin";
    public const string HealthLive = "/health/live";
    public const string HealthReady = "/health/ready";

    /// <summary>Orchestrator startup probe: 200 once every required readiness probe has been healthy at least once.</summary>
    public const string HealthStartup = "/health/startup";
}
