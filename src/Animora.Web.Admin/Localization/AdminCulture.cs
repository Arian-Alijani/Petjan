using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Animora.Web.Admin.Localization;

/// <summary>
/// Per-request UI language of the operator console (Persian by default, English on request). The choice lives in the
/// <c>admin_lang</c> cookie, written by <c>POST /admin/account/lang</c>; static SSR renders read it through this scoped
/// service. Interactive islands have no <see cref="HttpContext"/>, so pages hand them <see cref="Code"/> explicitly.
/// </summary>
public sealed class AdminCulture
{
    /// <summary>Cookie that stores the chosen language code.</summary>
    public const string CookieName = "admin_lang";

    /// <summary>Persian (right-to-left).</summary>
    public const string Fa = "fa";

    /// <summary>English (left-to-right).</summary>
    public const string En = "en";

    public AdminCulture(IHttpContextAccessor accessor, IConfiguration configuration)
    {
        string? fallback = configuration["Admin:DefaultLanguage"];
        Code = Normalize(accessor.HttpContext?.Request.Cookies[CookieName], Normalize(fallback, Fa));
    }

    /// <summary>Active language code: <c>fa</c> or <c>en</c>.</summary>
    public string Code { get; }

    /// <summary><see langword="true"/> when the active language is right-to-left.</summary>
    public bool IsRtl => Code == Fa;

    /// <summary>HTML <c>dir</c> attribute value.</summary>
    public string Dir => IsRtl ? "rtl" : "ltr";

    /// <summary>The language the switch button offers.</summary>
    public string OtherCode => IsRtl ? En : Fa;

    /// <summary>Translates <paramref name="key"/> into the active language.</summary>
    public string T(string key) => AdminStrings.Get(key, Code);

    /// <summary>Maps any input onto a supported code, otherwise <paramref name="fallback"/>.</summary>
    public static string Normalize(string? value, string fallback = Fa) => value?.Trim().ToLowerInvariant() switch
    {
        En => En,
        Fa => Fa,
        _ => fallback,
    };
}
