using Animora.Contracts.Wire.Errors;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Files;
using Animora.Server.Authorization;
using Animora.Server.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
namespace Animora.Server.Files;

internal static class FileEndpoints
{
    public const string ValetHeader = "X-Animora-Valet";
    public static void MapFileEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(ServerRoutes.Files).RequireAuthorization(CoarsePolicies.TenantMember, CoarsePolicies.DeviceActive)
            .RequireRateLimiting(RateLimitPolicies.Files);
        group.MapPost("", Declare).RequireAuthorization(PermissionCodes.PatientsWrite);
        group.MapPost("/{id}/valet", Issue);
        group.MapPut("/uploads/{uploadId}", Upload).RequireAuthorization(PermissionCodes.PatientsWrite)
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(FileStore.MaxChunkBytes));
        group.MapGet("/uploads/{uploadId}", Resume).RequireAuthorization(PermissionCodes.PatientsWrite);
        group.MapPost("/uploads/{uploadId}/complete", Complete).RequireAuthorization(PermissionCodes.PatientsWrite);
        group.MapGet("/{id}", Download).RequireAuthorization(PermissionCodes.PatientsRead);
        group.MapDelete("/{id}", Delete).RequireAuthorization(PermissionCodes.PatientsWrite);
    }
    private static ValetBinding Binding(HttpContext c, FileObject row, string operation)
    {
        var membership = c.RequestServices.GetRequiredService<IRequestAuthorizationState>().Membership!;
        return new(membership.TenantId, membership.UserId, membership.DeviceId, row.Id, operation, row.DeclaredSizeBytes);
    }
    private static bool Valid(HttpContext c, FileObject row, string operation, ValetKeyService keys) =>
        keys.Validate(c.Request.Headers[ValetHeader].ToString(), Binding(c, row, operation));
    private static IResult Error(HttpContext c, ErrorCode error) => Failure(c, error.Code,
        error == ErrorCodes.FileNotFound ? 404 : error == ErrorCodes.ValetKeyInvalid ? 403 : error == ErrorCodes.ConcurrencyConflict ? 409 : 400);
    private static IResult Failure(HttpContext c, string code, int status) => Results.Json(new WireApiError(code, null, [], CorrelationHeader.Resolve(c)), statusCode: status);
    private static async Task<IResult> Declare(HttpContext c, FileDeclaration body, FileTransferService service, ValetKeyService keys, CancellationToken ct)
    {
        var user = c.RequestServices.GetRequiredService<IRequestAuthorizationState>().Membership!.UserId;
        var result = await service.DeclareAsync(user, body, ct).ConfigureAwait(false);
        if (result.IsFailure) return Error(c, result.Error);
        var row = result.Value!; return Results.Json(new { fileId = row.Id, uploadId = row.UploadId, nextOffset = 0, valet = keys.Issue(Binding(c, row, "upload")) }, statusCode: 201);
    }
    internal sealed record ValetRequest(string Operation);
    private static async Task<IResult> Issue(string id, ValetRequest body, HttpContext c, FileTransferService service, ValetKeyService keys, IAuthorizationService authorization, CancellationToken ct)
    {
        if (body.Operation is not ("upload" or "download")) return Error(c, ErrorCodes.ValetKeyInvalid);
        var allowed = await authorization.AuthorizeAsync(c.User, null, body.Operation == "upload" ? PermissionCodes.PatientsWrite : PermissionCodes.PatientsRead).ConfigureAwait(false);
        if (!allowed.Succeeded) return Error(c, ErrorCodes.ValetKeyInvalid);
        var row = await service.FindAsync(id, false, ct).ConfigureAwait(false);
        if (row is null) return Error(c, ErrorCodes.FileNotFound);
        if (body.Operation == "download" && row.State != FileState.Available) return Failure(c, ServerErrorCodes.FileNotAvailable, 409);
        return Results.Json(new { valet = keys.Issue(Binding(c, row, body.Operation)), expiresInSeconds = 300 });
    }
    private static async Task<IResult> Upload(string uploadId, long offset, HttpContext c, FileTransferService service, ValetKeyService keys, CancellationToken ct)
    {
        var row = await service.FindAsync(uploadId, true, ct).ConfigureAwait(false);
        if (row is null) return Error(c, ErrorCodes.FileNotFound);
        if (!Valid(c, row, "upload", keys)) return Error(c, ErrorCodes.ValetKeyInvalid);
        if (c.Request.ContentLength > FileStore.MaxChunkBytes) return Error(c, ErrorCodes.FileTooLarge);
        var feature = c.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = FileStore.MaxChunkBytes;
        using var memory = new MemoryStream(); var buffer = new byte[65536]; int read;
        while ((read = await c.Request.Body.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        { if (memory.Length + read > FileStore.MaxChunkBytes) return Error(c, ErrorCodes.FileTooLarge); memory.Write(buffer, 0, read); }
        var outcome = await service.WriteAsync(uploadId, offset, memory.ToArray(), ct).ConfigureAwait(false);
        c.Response.Headers["X-Animora-Next-Offset"] = outcome.NextOffset.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return outcome.Error is { } error ? Error(c, error) : outcome.Accepted ? Results.NoContent() : Failure(c, ServerErrorCodes.FileOffsetMismatch, 409);
    }
    private static async Task<IResult> Resume(string uploadId, HttpContext c, FileTransferService service, ValetKeyService keys, CancellationToken ct)
    {
        var row = await service.FindAsync(uploadId, true, ct).ConfigureAwait(false);
        if (row is null) return Error(c, ErrorCodes.FileNotFound);
        return Valid(c, row, "upload", keys) ? Results.Json(new { nextOffset = row.SizeBytes, state = row.State.ToString() }) : Error(c, ErrorCodes.ValetKeyInvalid);
    }
    private static async Task<IResult> Complete(string uploadId, HttpContext c, FileTransferService service, ValetKeyService keys, CancellationToken ct)
    {
        var row = await service.FindAsync(uploadId, true, ct).ConfigureAwait(false);
        if (row is null) return Error(c, ErrorCodes.FileNotFound);
        if (!Valid(c, row, "upload", keys)) return Error(c, ErrorCodes.ValetKeyInvalid);
        var result = await service.CompleteAsync(uploadId, ct).ConfigureAwait(false);
        return result.IsFailure ? Error(c, result.Error) : Results.Json(new { fileId = result.Value!.Id });
    }
    internal static void DownloadHeaders(HttpResponse response)
    { response.Headers["X-Content-Type-Options"] = "nosniff"; response.Headers.CacheControl = "private, no-store"; }
    private static async Task<IResult> Download(string id, HttpContext c, FileTransferService service, FileStore store, ValetKeyService keys, CancellationToken ct)
    {
        var row = await service.FindAsync(id, false, ct).ConfigureAwait(false);
        if (row is null) return Error(c, ErrorCodes.FileNotFound);
        if (!Valid(c, row, "download", keys)) return Error(c, ErrorCodes.ValetKeyInvalid);
        if (row.State != FileState.Available || row.Sha256 is null || !File.Exists(store.Blob(row.Sha256))) return Failure(c, ServerErrorCodes.FileNotAvailable, 409);
        DownloadHeaders(c.Response);
        return Results.File(store.Blob(row.Sha256), row.ContentType, row.OriginalName, enableRangeProcessing: true);
    }
    private static async Task<IResult> Delete(string id, HttpContext c, FileTransferService service, CancellationToken ct)
    { var result = await service.DeleteAsync(id, ct).ConfigureAwait(false); return result.IsSuccess ? Results.NoContent() : Error(c, result.Error); }
}
