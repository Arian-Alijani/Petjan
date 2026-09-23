using Animora.Domain.Primitives;
using Animora.Domain.Time;

namespace Animora.Domain.Identity;

/// <summary>
/// A tenant's stored permission set for one <see cref="StaffRole"/>, seeded from
/// <see cref="DefaultRolePermissions"/> and editable afterwards (permission
/// <see cref="PermissionCodes.RolesManage"/>).
/// </summary>
/// <remarks>
/// The system <see cref="StaffRole.Manager"/> definition is immutable: removing authority from
/// the only role that can restore it would lock a tenant out of its own clinic.
/// </remarks>
public sealed class RoleDefinition : AggregateRoot
{
    private readonly HashSet<string> _permissionCodes = new(StringComparer.Ordinal);

    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private RoleDefinition()
    {
    }

    /// <summary>The role this definition describes.</summary>
    public StaffRole Role { get; private set; }

    /// <summary>
    /// Stable, non-localized key the Desktop maps to Persian text, e.g. <c>"role.manager"</c>.
    /// No layer below the Desktop produces user-facing localized strings.
    /// </summary>
    public string DisplayKey { get; private set; } = string.Empty;

    /// <summary>
    /// <see langword="true"/> for the roles shipped with the product. A system
    /// <see cref="StaffRole.Manager"/> definition rejects every grant/revoke.
    /// </summary>
    public bool IsSystem { get; private set; }

    /// <summary>The granted permission codes.</summary>
    public IReadOnlySet<string> PermissionCodes => _permissionCodes;

    /// <summary>Creates a definition seeded from <see cref="DefaultRolePermissions"/>.</summary>
    public static RoleDefinition CreateSystem(string tenantId, StaffRole role, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(clock);

        var definition = new RoleDefinition
        {
            Id = IdGenerator.NewId(clock.UtcNow),
            TenantId = tenantId,
            Role = role,
            DisplayKey = DisplayKeyFor(role),
            IsSystem = true,
        };

        foreach (var code in DefaultRolePermissions.For(role))
        {
            definition._permissionCodes.Add(code);
        }

        return definition;
    }

    /// <summary>The stable display key of <paramref name="role"/>, e.g. <c>"role.manager"</c>.</summary>
    public static string DisplayKeyFor(StaffRole role) =>
        "role." + role.ToString().ToLowerInvariant();

    /// <summary>Grants <paramref name="code"/>. Idempotent.</summary>
    /// <exception cref="InvalidOperationException">The definition is the immutable system Manager role.</exception>
    /// <exception cref="ArgumentException"><paramref name="code"/> is not a declared permission code.</exception>
    public void Grant(string code)
    {
        EnsureMutable();
        EnsureKnown(code);
        _permissionCodes.Add(code);
    }

    /// <summary>Revokes <paramref name="code"/>. Idempotent.</summary>
    /// <exception cref="InvalidOperationException">The definition is the immutable system Manager role.</exception>
    public void Revoke(string code)
    {
        EnsureMutable();
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        _permissionCodes.Remove(code);
    }

    /// <summary><see langword="true"/> when this definition grants <paramref name="code"/>.</summary>
    public bool Has(string code) => _permissionCodes.Contains(code);

    private void EnsureMutable()
    {
        if (IsSystem && Role == StaffRole.Manager)
        {
            throw new InvalidOperationException(
                "The system Manager role definition is immutable: its permission set is always the full catalog.");
        }
    }

    private static void EnsureKnown(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (!Identity.PermissionCodes.Exists(code))
        {
            throw new ArgumentException($"Unknown permission code '{code}'.", nameof(code));
        }
    }
}
