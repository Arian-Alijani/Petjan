using Animora.Persistence.PostgreSql.Operator;
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
        services.AddRazorComponents();
        services.AddScoped<AdminSession>();services.AddScoped<IAdminSession>(sp=>sp.GetRequiredService<AdminSession>());
        services.AddScoped<OperatorReadQueries>();services.AddScoped<OperatorPlanAdministration>();
        services.AddScoped<IAdminStatusReader,AdminStatusReader>();services.AddScoped<IAdminTenantQueries,AdminTenantQueries>();
        services.AddScoped<IAdminSubscriptionCommands,AdminSubscriptionAdapter>();services.AddScoped<IAdminPlanCommands,AdminPlanAdapter>();
        services.AddScoped<IAdminLicenseCommands,AdminLicenseAdapter>();return services;
    }
}
