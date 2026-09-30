using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Domain.Identity;
using Animora.Domain.Results;

namespace Animora.Application.UseCases.Profile;

/// <summary>
/// C10: the persistence seam behind the profile/settings reads. Implemented by the local store; the
/// Application layer never touches EF (CONVENTIONS §2).
/// </summary>
public interface IProfileQueries
{
    /// <summary>The user row (identity fields), or <see langword="null"/> when the id is unknown.</summary>
    Task<string?> DisplayNameAsync(string userId, CancellationToken ct);

    /// <summary>The user's active membership role name inside the tenant, or <see langword="null"/>.</summary>
    Task<string?> RoleAsync(string tenantId, string userId, CancellationToken ct);

    /// <summary>
    /// The raw preference bag (<c>Key</c> → <c>ValueJson</c>) plus the profile KPI inputs and the real
    /// <c>LocalSessions</c> rows of the current user — one scoped read, no sample data.
    /// </summary>
    Task<ProfileFacts> FactsAsync(string tenantId, string userId, CancellationToken ct);

    /// <summary>The tenant's active staff roster (identity + role + shift), ordered by display name.</summary>
    Task<IReadOnlyList<ProfileTeamRowData>> TeamAsync(string tenantId, CancellationToken ct);

    Task<IReadOnlyList<StaffDirectoryRow>> StaffAsync(string tenantId, CancellationToken ct);
}

/// <summary>The non-team inputs of the profile read model.</summary>
public sealed record ProfileFacts(
    DateTimeOffset? LastLoginUtc,
    IReadOnlyDictionary<string, string> Preferences,
    IReadOnlyList<ProfileStatRow> Stats,
    IReadOnlyList<ProfileSessionRow> Sessions);

/// <summary>One roster row before the <c>ProfileTeamRow</c> projection.</summary>
public sealed record ProfileTeamRowData(string UserId, string DisplayName, string Role,
    int WorkStartMinute, int WorkEndMinute);

/// <summary>
/// Reads the signed-in user's profile. Requires <c>identity.staff.view</c> for the team card only: without it
/// the profile (and settings) still load, but <see cref="UserProfileReadModel.Team"/> is empty and
/// <see cref="UserProfileReadModel.CanViewTeam"/> is <see langword="false"/>.
/// </summary>
public sealed record UserProfileQuery : IQuery<UserProfileReadModel>;

public sealed class UserProfileQueryHandler(
    IProfileQueries queries,
    IPermissionEvaluator permissions,
    IStaffMembershipRepository memberships)
    : IQueryHandler<UserProfileQuery, UserProfileReadModel>
{
    public async Task<Result<UserProfileReadModel>> HandleAsync(
        UserProfileQuery query, QueryContext context, CancellationToken ct)
    {
        var facts = await queries.FactsAsync(context.TenantId, context.UserId, ct).ConfigureAwait(false);
        var displayName = await queries.DisplayNameAsync(context.UserId, ct).ConfigureAwait(false);
        if (displayName is null)
            return Result<UserProfileReadModel>.Fail(ErrorCodes.ProfileNotFound, $"No user '{context.UserId}'.");

        var role = await queries.RoleAsync(context.TenantId, context.UserId, ct).ConfigureAwait(false)
            ?? nameof(StaffRole.Receptionist);

        var canViewTeam = await CanViewTeamAsync(context, ct).ConfigureAwait(false);
        var team = canViewTeam
            ? (await queries.TeamAsync(context.TenantId, ct).ConfigureAwait(false))
                .Where(m => m.UserId != context.UserId)
                .Select(m => new ProfileTeamRow(m.UserId, m.DisplayName, m.Role, m.WorkStartMinute, m.WorkEndMinute))
                .ToArray()
            : [];

        return Result<UserProfileReadModel>.Ok(new UserProfileReadModel(
            context.UserId, string.Empty, displayName, role, facts.LastLoginUtc, canViewTeam,
            facts.Preferences, facts.Stats, facts.Sessions, team));
    }

    /// <summary>
    /// The staff-roster gate. A signed-in user's own active membership is the authoritative subject; an
    /// unreadable membership fails closed (no team).
    /// </summary>
    private async Task<bool> CanViewTeamAsync(QueryContext context, CancellationToken ct)
    {
        var membership = await memberships.FindActiveAsync(context.TenantId, context.UserId, ct).ConfigureAwait(false);
        if (membership is null) return false;
        var decision = await permissions.EvaluateAsync(
            PermissionCodes.IdentityStaffView,
            new AuthorizationSubject(context.TenantId, context.UserId, EmptyGrants, membership.Role,
                DeviceId: string.Empty, IsOffline: false),
            resource: null, ct).ConfigureAwait(false);
        return decision.Allowed;
    }

    private static readonly IReadOnlySet<string> EmptyGrants = new HashSet<string>(StringComparer.Ordinal);
}

/// <summary>Reads the persisted app-settings bag for the signed-in user. No permission beyond authentication.</summary>
public sealed record AppSettingsQuery : IQuery<IReadOnlyDictionary<string, string>>;

public sealed class AppSettingsQueryHandler(IProfileQueries queries)
    : IQueryHandler<AppSettingsQuery, IReadOnlyDictionary<string, string>>
{
    public async Task<Result<IReadOnlyDictionary<string, string>>> HandleAsync(
        AppSettingsQuery query, QueryContext context, CancellationToken ct)
    {
        var facts = await queries.FactsAsync(context.TenantId, context.UserId, ct).ConfigureAwait(false);
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in AppSettingsKeys.SettingsKeys)
        {
            if (facts.Preferences.TryGetValue(key, out var value)) settings[key] = value;
        }

        return Result<IReadOnlyDictionary<string, string>>.Ok(settings);
    }
}

public static class ProfileRegistration
{
    public static Animora.Application.Dispatch.ApplicationRegistrationBuilder AddProfileUseCases(
        this Animora.Application.Dispatch.ApplicationRegistrationBuilder builder) => builder
        .AddQuery<UserProfileQuery, UserProfileReadModel, UserProfileQueryHandler>()
        .AddQuery<AppSettingsQuery, IReadOnlyDictionary<string, string>, AppSettingsQueryHandler>()
        .AddQuery<StaffDirectoryQuery, StaffDirectoryResult, StaffDirectoryQueryHandler>()
        .AddCommand<CreateStaffCommand, Unit, CreateStaffCommandHandler>()
        .AddCommand<SetStaffActiveCommand, Unit, SetStaffActiveCommandHandler>()
        .AddCommand<SaveStaffAccessCommand, Unit, SaveStaffAccessCommandHandler>()
        .AddCommand<SaveUserProfileCommand, Unit, SaveUserProfileCommandHandler>()
        .AddCommand<SaveAppSettingsCommand, Unit, SaveAppSettingsCommandHandler>();
}
