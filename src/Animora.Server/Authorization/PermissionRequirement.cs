using Microsoft.AspNetCore.Authorization;

namespace Animora.Server.Authorization;

/// <summary>Requires the caller to hold a catalog permission, evaluated against <b>stored</b> RBAC.</summary>
/// <param name="PermissionCode">A <c>PermissionCodes.*</c> value.</param>
internal sealed record PermissionRequirement(string PermissionCode) : IAuthorizationRequirement;
