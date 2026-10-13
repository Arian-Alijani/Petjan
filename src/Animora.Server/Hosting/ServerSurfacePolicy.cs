namespace Animora.Server.Hosting;

/// <summary>Named route classes of the server surface (CONS-GATE-01).</summary>
internal enum SurfaceClass
{
    /// <summary>Anonymous liveness/readiness probes (<c>/health/*</c>).</summary>
    Health,

    /// <summary>Operator web console + its Blazor circuit (<c>/admin/*</c>, <c>/_blazor/*</c>); cookie-authenticated.</summary>
    OperatorConsole,

    /// <summary>Operator API (<c>/operator/*</c>); operator-cookie authenticated.</summary>
    Operator,

    /// <summary>Website-to-server provisioning intake (<c>/provisioning/*</c>); HMAC-signed.</summary>
    Provisioning,

    /// <summary>Pre-session auth endpoints: <c>/auth/login</c>, <c>/auth/refresh</c>, <c>/auth/keys</c> (exact paths).</summary>
    AuthAnonymous,

    /// <summary>Public licence key discovery (<c>/licensing/keys*</c>).</summary>
    LicensingKeys,

    /// <summary>Session-bound auth endpoints (logout, capabilities, device + staff management).</summary>
    AuthSession,

    /// <summary>Licence activation/status endpoints (<c>/licensing/*</c> except keys); tenant-bound, never entitlement-gated.</summary>
    Licensing,

    /// <summary>Tenant data plane: commands, sync, files. Tenant-bound AND entitlement-gated.</summary>
    TenantData,

    /// <summary>Anything else. Fails closed: tenant-bound AND entitlement-gated.</summary>
    Unclassified,
}

/// <summary>
/// CONS-GATE-01: the ONE definition of which request paths bypass the tenant-binding plane and the entitlement
/// plane. <see cref="TenantContextMiddleware"/> and <see cref="Licensing.ServerEntitlementGate"/> /
/// <see cref="Authorization.EntitlementMiddleware"/> all consume this table, so the planes can no longer drift.
/// A path that matches no known class is <see cref="SurfaceClass.Unclassified"/> and is treated as the
/// strictest class (tenant required + entitlement gated) - fail closed.
/// </summary>
internal static class ServerSurfacePolicy
{
    private const StringComparison Cmp = StringComparison.OrdinalIgnoreCase;
    private const string Blazor = "/_blazor";

    /// <summary>Classifies a request path into exactly one <see cref="SurfaceClass"/>.</summary>
    public static SurfaceClass Classify(PathString path)
    {
        if (path.StartsWithSegments("/health", Cmp)) return SurfaceClass.Health;
        if (path.StartsWithSegments(ServerRoutes.Admin, Cmp) || path.StartsWithSegments(Blazor, Cmp)) return SurfaceClass.OperatorConsole;
        if (path.StartsWithSegments(ServerRoutes.Operator, Cmp)) return SurfaceClass.Operator;
        if (path.StartsWithSegments(ServerRoutes.Provisioning, Cmp)) return SurfaceClass.Provisioning;

        if (path.StartsWithSegments(ServerRoutes.Auth, Cmp))
        {
            // Exact-path match on purpose: only these three are reachable before a session exists.
            return path.Equals(ServerRoutes.Auth + "/login", Cmp)
                || path.Equals(ServerRoutes.Auth + "/refresh", Cmp)
                || path.Equals(ServerRoutes.Auth + "/keys", Cmp)
                ? SurfaceClass.AuthAnonymous
                : SurfaceClass.AuthSession;
        }

        if (path.StartsWithSegments(ServerRoutes.Licensing, Cmp))
        {
            return path.StartsWithSegments(ServerRoutes.Licensing + "/keys", Cmp) ? SurfaceClass.LicensingKeys : SurfaceClass.Licensing;
        }

        if (path.StartsWithSegments(ServerRoutes.Commands, Cmp)
            || path.StartsWithSegments(ServerRoutes.Sync, Cmp)
            || path.StartsWithSegments(ServerRoutes.Files, Cmp))
        {
            return SurfaceClass.TenantData;
        }

        return SurfaceClass.Unclassified;
    }

    /// <summary>True when the class never runs tenant binding (no membership resolution).</summary>
    public static bool SkipsTenantBinding(SurfaceClass surface) => surface is
        SurfaceClass.Health or SurfaceClass.OperatorConsole or SurfaceClass.Operator or SurfaceClass.Provisioning
        or SurfaceClass.AuthAnonymous or SurfaceClass.LicensingKeys;

    /// <summary>True when the class is never subject to the licence/entitlement gate.</summary>
    public static bool IsEntitlementExempt(SurfaceClass surface) => surface is
        SurfaceClass.Health or SurfaceClass.OperatorConsole or SurfaceClass.Operator or SurfaceClass.Provisioning
        or SurfaceClass.AuthAnonymous or SurfaceClass.LicensingKeys or SurfaceClass.AuthSession or SurfaceClass.Licensing;

    /// <summary>Path convenience: skips tenant binding.</summary>
    public static bool SkipsTenantBinding(PathString path) => SkipsTenantBinding(Classify(path));

    /// <summary>Path convenience: exempt from the entitlement gate.</summary>
    public static bool IsEntitlementExempt(PathString path) => IsEntitlementExempt(Classify(path));
}
