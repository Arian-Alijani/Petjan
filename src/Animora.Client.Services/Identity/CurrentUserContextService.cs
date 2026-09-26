using System.Globalization;
using Animora.Application.Security;
using Animora.Client.Services.Mapping;
using Animora.Contracts.Identity;
using Animora.Contracts.Navigation;

namespace Animora.Client.Services.Identity;

/// <summary>
/// Real <see cref="ICurrentUserContext"/> (B03c). The contract is synchronous, so this singleton reads the
/// app-wide <see cref="SessionContextCache"/> (kept current by <c>CurrentSessionQuery</c>, sign-in and first-run
/// handlers) — no sync-over-async. <see cref="RoleLabel"/> is a stable role key, not Persian text.
/// </summary>
public sealed class CurrentUserContextService(SessionContextCache cache) : ICurrentUserContext
{
    public string UserId => cache.Current?.UserId ?? string.Empty;

    public string Name => cache.Current?.DisplayName ?? string.Empty;

    public string RoleLabel => cache.Current?.Role is { } role ? RoleMapping.ToRoleKey(role) : string.Empty;

    public string Initials
    {
        get
        {
            var name = Name.Trim();
            if (name.Length == 0) return string.Empty;
            var enumerator = StringInfo.GetTextElementEnumerator(name);
            return enumerator.MoveNext() ? enumerator.GetTextElement() : string.Empty;
        }
    }

    public UserRole Role => cache.Current?.Role is { } role ? RoleMapping.ToUserRole(role) : UserRole.Manager;
}
