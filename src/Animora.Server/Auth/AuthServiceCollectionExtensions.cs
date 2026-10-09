using Animora.Domain.Identity;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.DependencyInjection;
using Animora.Persistence.PostgreSql.Identity;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Animora.Server.Auth;

/// <summary>DI wiring of the S04 authentication plane.</summary>
internal static class AuthServiceCollectionExtensions
{
    /// <summary>Access-token lifetime.</summary>
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Registers ASP.NET Core Identity (core, over <see cref="ServerUserStore"/>), the opaque
    /// Data-Protection-protected bearer scheme (<b>no JWT / OIDC</b>), and the sign-in services. Must run
    /// after <c>AddAnimoraPostgreSql</c> (it swaps in the explicitly-bound auth tenant context).
    /// </summary>
    public static IServiceCollection AddAnimoraIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IClock, ServerClock>();

        services.AddAnimoraIdentityPersistence();

        services
            .AddIdentityCore<ServerIdentityUser>(options =>
            {
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = User.MaxFailedAttempts;
                options.Lockout.DefaultLockoutTimeSpan = User.LockoutDuration;
                options.User.RequireUniqueEmail = false;
            })
            .AddUserStore<ServerUserStore>();

        // Opaque bearer + refresh tokens protected by the PostgreSQL-backed Data Protection key ring.
        services
            .AddAuthentication(IdentityConstants.BearerScheme)
            .AddBearerToken(IdentityConstants.BearerScheme, options =>
            {
                options.BearerTokenExpiration = AccessTokenLifetime;
                options.RefreshTokenExpiration = RefreshTokenService.Lifetime;
            });

        services.AddScoped<IAccessTokenIssuer, BearerAccessTokenIssuer>();
        services.AddScoped<LoginService>();

        return services;
    }
}
