using System.Net;
using System.Text.RegularExpressions;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Server.Admin;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace Animora.Server.Tests.Admin;

internal sealed class AdminHost : IDisposable
{
    private readonly string _root=Directory.CreateTempSubdirectory("animora-admin-test-").FullName;
    public WebApplicationFactory<Program> Factory { get; }
    public HttpClient Client { get; }
    public AdminHost(string? connection=null, Action<IServiceCollection>? configure=null)
    {
        Factory=new WebApplicationFactory<Program>().WithWebHostBuilder(builder=>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?> {
                ["Server:DataRootPath"]=_root,["Database:ConnectionString"]=connection??"Host=127.0.0.1;Database=unavailable;Username=none;Password=none",
                ["Telemetry:Enabled"]="false",["Server:AuthCache:TtlSeconds"]="0" }));
            builder.ConfigureServices(s=>
            {
                // Isolate request behavior: workers/bootstrap are independently tested by their own gates.
                foreach(var d in s.Where(d=>d.ServiceType==typeof(IHostedService)&&(d.ImplementationFactory is not null||d.ImplementationType?.Assembly==typeof(Program).Assembly)).ToArray())s.Remove(d);
                s.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                configure?.Invoke(s);
            });
        });
        Client=Factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress=new Uri("https://localhost"),AllowAutoRedirect=false,HandleCookies=true });
    }
    public async Task<string> TokenAsync(string route="/admin/login")
    {
        string html=await Client.GetStringAsync(route);
        var match=Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        if(!match.Success)throw new InvalidOperationException("SSR antiforgery token missing.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
    public async Task<HttpResponseMessage> LoginAsync(string user="admin",string password="Strong-operator-Pass-123!")=>
        await Client.PostAsync("/admin/account/login",new FormUrlEncodedContent(new Dictionary<string,string> { ["__RequestVerificationToken"]=await TokenAsync(),["userName"]=user,["password"]=password }));
    public async Task SeedOperatorAsync(bool mustChange=false)
    {
        using var scope=Factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AnimoraOperatorDbContext>();
        var account=new OperatorAccount { Id="01K00000000000000000000099",UserName="admin",NormalizedUserName="ADMIN",SecurityStamp="01K00000000000000000000098",Status=OperatorAccountStatus.Active,CreatedUtc=DateTimeOffset.UtcNow,MustChangePassword=mustChange };
        account.PasswordHash=scope.ServiceProvider.GetRequiredService<IPasswordHasher<OperatorAccount>>().HashPassword(account,"Strong-operator-Pass-123!");
        var existing=await db.Accounts.SingleOrDefaultAsync(a=>a.NormalizedUserName=="ADMIN");
        if(existing is null) db.Accounts.Add(account); else db.Entry(existing).CurrentValues.SetValues(account);
        await db.SaveChangesAsync();
    }
    public async Task<HttpResponseMessage> SubmitAsync(string route, string handler, IDictionary<string, string> fields)
    {
        var form = new Dictionary<string, string>(fields)
        {
            ["_handler"] = handler,
            ["__RequestVerificationToken"] = await TokenAsync(route)
        };
        return await Client.PostAsync(route, new FormUrlEncodedContent(form));
    }
    internal static string Hidden(string html, string name)
    {
        var match = Regex.Match(html, "name=\"" + Regex.Escape(name) + "\"[^>]*value=\"([^\"]*)\"");
        if (!match.Success) throw new InvalidOperationException("Expected SSR field missing: " + name);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
    public void Dispose() { Client.Dispose();Factory.Dispose();if(Directory.Exists(_root))Directory.Delete(_root,true); }
}
