using Animora.Persistence.PostgreSql.Operator;
using Animora.Persistence.PostgreSql.Provisioning;
using Animora.Persistence.PostgreSql.Files;
using Animora.Persistence.PostgreSql.Audit;
using Microsoft.AspNetCore.Components.Authorization;
using Animora.Server.Operator;
using Animora.Web.Admin.Ports;
namespace Animora.Server.Admin;

internal static class AdminServiceCollectionExtensions
{
    public static IServiceCollection AddAnimoraAdmin(this IServiceCollection services,IConfiguration configuration)
    {
        services.AddOptions<AdminOptions>().Bind(configuration.GetSection("Admin"))
            .Validate(o=>o.SessionMinutes==30&&o.RequireHttps,"Admin requires HTTPS and an absolute 30-minute session.").ValidateOnStart();
        services.AddAuthentication().AddCookie(OperatorCookie.Scheme,OperatorCookie.Configure);
        services.AddAuthorization(o=>o.AddPolicy(OperatorCookie.Policy,p=>p.AddAuthenticationSchemes(OperatorCookie.Scheme)
            .RequireAuthenticatedUser().RequireRole("operator").RequireAssertion(c=>
            {
                if(c.Resource is not HttpContext http)return false;
                return http.RequestServices.GetRequiredService<OperatorTenantContext>().IsOperator
                    &&c.User.Identities.Any(i=>i.IsAuthenticated&&i.AuthenticationType==OperatorCookie.Scheme)
                    &&(!http.Items.ContainsKey("AdminPasswordChangeRequired")||OperatorCookie.PasswordChangePath(http.Request.Path));
            })));
        services.AddAntiforgery(o=> { o.Cookie.Name="__Host-animora-af";o.Cookie.Path="/";o.Cookie.HttpOnly=true;
            o.Cookie.SecurePolicy=CookieSecurePolicy.Always;o.Cookie.SameSite=SameSiteMode.Strict; });
        services.AddRazorComponents().AddInteractiveServerComponents().AddCircuitOptions(o =>
        {
            o.DisconnectedCircuitMaxRetained = 2;
            o.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(1);
            o.MaxBufferedUnacknowledgedRenderBatches = 4;
        });
        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, OperatorAuthenticationStateProvider>();
        services.AddScoped<IAdminStatusSnapshotSource, AdminStatusSnapshotSource>();
        services.AddScoped<ProvisioningQueries>(); services.AddScoped<FileMismatchService>(); services.AddScoped<AuditQueries>();
        services.AddScoped<AdminProvisioningAdapter>();
        services.AddScoped<IAdminProvisioningQueries>(sp => sp.GetRequiredService<AdminProvisioningAdapter>());
        services.AddScoped<IAdminProvisioningCommands>(sp => sp.GetRequiredService<AdminProvisioningAdapter>());
        services.AddScoped<IAdminWebsiteClients, AdminWebsiteClientsAdapter>();
        services.AddScoped<AdminFilesAdapter>();
        services.AddScoped<IAdminFileQueries>(sp => sp.GetRequiredService<AdminFilesAdapter>());
        services.AddScoped<IAdminFileCommands>(sp => sp.GetRequiredService<AdminFilesAdapter>());
        services.AddScoped<IAdminAuditQueries, AdminAuditAdapter>(); services.AddScoped<IAdminInsightStatus, AdminInsightStatusAdapter>();
        services.AddHttpContextAccessor();services.AddScoped<Animora.Web.Admin.Localization.AdminCulture>();
        services.AddScoped<AdminSession>();services.AddScoped<IAdminSession>(sp=>sp.GetRequiredService<AdminSession>());
        services.AddScoped<OperatorReadQueries>();services.AddScoped<OperatorPlanAdministration>();
        services.AddScoped<IAdminStatusReader,AdminStatusReader>();services.AddScoped<IAdminTenantQueries,AdminTenantQueries>();
        services.AddScoped<IAdminSubscriptionCommands,AdminSubscriptionAdapter>();services.AddScoped<IAdminPlanCommands,AdminPlanAdapter>();
        services.AddScoped<IAdminLicenseCommands,AdminLicenseAdapter>();
        services.AddScoped<IAdminStaffQueries,AdminStaffAdapter>();services.AddScoped<IAdminProductQueries,AdminProductAdapter>();return services;
    }
}
