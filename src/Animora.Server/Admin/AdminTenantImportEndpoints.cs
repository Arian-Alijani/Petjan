using System.Text;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Server.Hosting;
using Animora.Server.Operator;
using Microsoft.AspNetCore.Antiforgery;

namespace Animora.Server.Admin;

/// <summary>
/// Operator-cookie-protected, antiforgery-validated, size-limited HTTP endpoints for importing a format-v2
/// <c>clinic-import.animora.json</c> bundle into one clinic's tenant (T8). Mirrors <see cref="AdminBackupEndpoints"/>'s hardening.
/// </summary>
internal static class AdminTenantImportEndpoints
{
    public static void MapAdminTenantImportEndpoints(this WebApplication app)
    {
        app.MapPost("/admin/tenants/{id}/import/upload", Upload)
            .RequireAuthorization(OperatorCookie.Policy)
            .RequireRateLimiting(RateLimitPolicies.Operator)
            .WithMetadata(new Microsoft.AspNetCore.Http.Metadata.RequestSizeLimitMetadata(ClinicBundleImportService.MaxBundleBytes + (64 * 1024)))
            .WithOrder(-1);
    }

    private static async Task<IResult> Upload(
        string id,
        HttpContext http,
        AdminTenantImportAdapter importer,
        IAntiforgery antiforgery,
        CancellationToken ct)
    {
        await antiforgery.ValidateRequestAsync(http).ConfigureAwait(false);

        byte[] payload;
        bool confirmed;
        bool dryRun;

        if (http.Request.HasFormContentType)
        {
            var form = await http.Request.ReadFormAsync(ct).ConfigureAwait(false);
            confirmed = string.Equals(form["confirmed"].ToString(), "true", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(form["Form.Confirmed"].ToString(), "true", StringComparison.OrdinalIgnoreCase);
            dryRun = string.Equals(form["dryRun"].ToString(), "true", StringComparison.OrdinalIgnoreCase);

            var file = form.Files.GetFile("bundle") ?? (form.Files.Count > 0 ? form.Files[0] : null);
            if (file is not null)
            {
                if (file.Length <= 0 || file.Length > ClinicBundleImportService.MaxBundleBytes)
                {
                    return Results.BadRequest(new { code = "VALIDATION_FAILED", detail = "Bundle file is empty or exceeds 32 MiB." });
                }

                await using var stream = file.OpenReadStream();
                using var ms = new MemoryStream((int)file.Length);
                await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
                payload = ms.ToArray();
            }
            else if (form["bundleJson"].ToString() is { Length: > 0 } jsonText)
            {
                payload = Encoding.UTF8.GetBytes(jsonText);
            }
            else
            {
                return Results.BadRequest(new { code = "VALIDATION_FAILED", detail = "Missing bundle file." });
            }
        }
        else
        {
            confirmed = string.Equals(http.Request.Query["confirmed"], "true", StringComparison.OrdinalIgnoreCase);
            dryRun = string.Equals(http.Request.Query["dryRun"], "true", StringComparison.OrdinalIgnoreCase);
            using var ms = new MemoryStream();
            await http.Request.Body.CopyToAsync(ms, ct).ConfigureAwait(false);
            payload = ms.ToArray();
        }

        http.Response.Headers.CacheControl = "no-store";

        var result = dryRun
            ? await importer.ValidateAsync(id, payload, ct).ConfigureAwait(false)
            : await importer.ImportAsync(id, payload, confirmed, ct).ConfigureAwait(false);

        if (result.IsFailure)
        {
            var status = result.Error.Code switch
            {
                "NOT_FOUND" => StatusCodes.Status404NotFound,
                "CONCURRENCY_CONFLICT" => StatusCodes.Status409Conflict,
                "STORE_UNAVAILABLE" => StatusCodes.Status503ServiceUnavailable,
                _ => StatusCodes.Status400BadRequest,
            };
            return Results.Json(new { code = result.Error.Code, detail = result.Detail }, statusCode: status);
        }

        return Results.Json(result.Value);
    }
}
