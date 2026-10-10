using System.Xml.Linq;
using Animora.Domain.Results;
using Animora.Web.Admin;
using Animora.Web.Admin.Models;
using Animora.Server.Admin;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Xunit;
namespace Animora.Server.Tests.Admin;

public sealed class RenderModePolicyTests
{
    [Fact] public void NoGlobalRenderMode_AndIslandCountWithinBudget()
    {
        Assert.Equal(1, RenderModePolicy.MaxIslands);
        var files = Directory.EnumerateFiles(Path.Combine(RepoRoot.Find(), "src/Animora.Web.Admin"), "*.razor", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("@rendermode", StringComparison.Ordinal)).ToArray();
        Assert.Single(files); Assert.Equal("LiveStatusPanel.razor", Path.GetFileName(files[0]));
        Assert.Contains("prerender: false", File.ReadAllText(files[0]));
        foreach (string f in new[] { "App.razor", "Routes.razor", "Layout/MainLayout.razor" })
            Assert.DoesNotContain("@rendermode", File.ReadAllText(Path.Combine(RepoRoot.Find(), "src/Animora.Web.Admin", f)));
    }
}
public sealed class WebAdminLayeringTests
{
    [Fact] public void RclReferencesOnlyDomain()
    { var xml=XDocument.Load(Path.Combine(RepoRoot.Find(),"src/Animora.Web.Admin/Animora.Web.Admin.csproj"));Assert.Single(xml.Descendants("ProjectReference"));Assert.Contains("Animora.Domain",xml.Descendants("ProjectReference").Single().Attribute("Include")!.Value);Assert.Empty(xml.Descendants("PackageReference")); }
}
public sealed class AdminMessagesTests
{
    [Fact] public void EveryAdapterErrorCode_HasAMessage()
    { foreach(var f in typeof(ErrorCodes).GetFields().Where(f=>f.FieldType==typeof(ErrorCode)))Assert.True(AdminMessages.HasMessage(((ErrorCode)f.GetValue(null)!).Code)); }
}
public sealed class AdminHeadersTests
{
    [Fact] public void NoStore_CSP_FrameDeny_Nosniff()
    { var c=new DefaultHttpContext();AdminSecurityHeadersMiddleware.Apply(c.Response);Assert.Equal("no-store",c.Response.Headers.CacheControl);Assert.Equal("nosniff",c.Response.Headers["X-Content-Type-Options"]);Assert.Equal("DENY",c.Response.Headers["X-Frame-Options"]);Assert.Contains("frame-ancestors 'none'",c.Response.Headers.ContentSecurityPolicy.ToString()); }
}
public sealed class AdminCookieTests
{
    [Fact] public void Cookie_IsHostPrefixed_HttpOnly_SameSiteStrict_Absolute30Min()
    { var o=new CookieAuthenticationOptions();OperatorCookie.Configure(o);Assert.StartsWith("__Host-",o.Cookie.Name);Assert.True(o.Cookie.HttpOnly);Assert.Equal(CookieSecurePolicy.Always,o.Cookie.SecurePolicy);Assert.Equal(SameSiteMode.Strict,o.Cookie.SameSite);Assert.False(o.SlidingExpiration);Assert.Equal(TimeSpan.FromMinutes(30),o.ExpireTimeSpan); }
}
public sealed class AdminPagesTests
{
    [Fact] public void Lists_Paged_Max200() { Assert.Equal((1,200),AdminPage<string>.Bounds(-1,1000)); }
    [Fact] public void Dashboard_ReadsInProcessCountersOnly()
    { string source=File.ReadAllText(Path.Combine(RepoRoot.Find(),"src/Animora.Server/Admin/AdminStatusReader.cs"));Assert.DoesNotContain("CheckAsync",source);Assert.Contains("metrics.OutboxPending",source);Assert.Single(System.Text.RegularExpressions.Regex.Matches(source,"queries.DashboardAsync")); }
    [Fact] public void TempPassword_ShownOnce_NotInUrlOrLogs()
    { foreach(string f in Directory.EnumerateFiles(Path.Combine(RepoRoot.Find(),"src/Animora.Web.Admin/Pages"),"*.razor")) {var source=File.ReadAllText(f);Assert.DoesNotContain("?password",source);Assert.DoesNotContain("Logger",source);}Assert.Contains("r.WasReplay?null:r.OwnerTemporaryPassword",File.ReadAllText(Path.Combine(RepoRoot.Find(),"src/Animora.Server/Admin/AdminSubscriptionAdapter.cs"))); }
}
