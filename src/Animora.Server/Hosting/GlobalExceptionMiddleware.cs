using Animora.Contracts.Wire.Errors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Animora.Server.Hosting;

/// <summary>
/// Maps any unhandled exception to a single <see cref="WireApiError"/> body
/// (HTTP 500) carrying a correlation id, and never leaks a stack trace outside
/// Development (ARCHITECTURE.md §4). A client-aborted request produces no body.
/// First middleware in the pipeline (startup step 7).
/// </summary>
internal sealed class GlobalExceptionMiddleware(
    RequestDelegate next,
    IHostEnvironment environment,
    ILogger<GlobalExceptionMiddleware> logger)
{
    private static readonly IReadOnlyList<WireFieldError> NoFields = [];

    public async Task InvokeAsync(HttpContext context)
    {
        // Establish the correlation id up front so even a failure echoes it.
        var correlationId = CorrelationHeader.Resolve(context);
        CorrelationHeader.Echo(context, correlationId);

        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client went away; there is no one to write a body for.
            logger.LogDebug("Request {CorrelationId} aborted by the client.", correlationId);
        }
        catch (Exception ex) when (!context.Response.HasStarted
            && (Animora.Persistence.PostgreSql.Context.DatabaseFailure.IsPoolExhausted(ex)
                || ex is Animora.Server.Observability.QueryBudgetExceededException))
        {
            // PERF-AUTHZ-01/PERF-QB-01: pool exhaustion (Database:PoolWaitSeconds) or a saturated reporting budget
            // (Server:QueryBudget:WaitSeconds) fails fast as a typed, retryable 503.
            logger.LogWarning("Capacity exhausted (DB pool / reporting budget) for request {CorrelationId}; shedding with 503.", correlationId);
            context.Response.Clear();
            CorrelationHeader.Echo(context, correlationId);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.RetryAfter = "1";
            await context.Response.WriteAsJsonAsync(
                new WireApiError(ServerErrorCodes.ServerBusy, null, NoFields, correlationId),
                context.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError("Unhandled {FailureClass} for request {CorrelationId}.", ex.GetType().Name, correlationId);

            if (context.Response.HasStarted)
            {
                // Can no longer shape the response; let it surface as-is.
                throw;
            }

            context.Response.Clear();
            // Clear() drops headers, including the correlation id echoed up front.
            CorrelationHeader.Echo(context, correlationId);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;

            if(context.Request.Path.StartsWithSegments(ServerRoutes.Admin))
            {
                Animora.Server.Admin.AdminSecurityHeadersMiddleware.Apply(context.Response);
                context.Response.ContentType="text/html; charset=utf-8";
                await context.Response.WriteAsync("<!doctype html><html lang=\"en\"><head><title>Request failed</title></head><body><h1>Request failed</h1><p>Correlation: "
                    +System.Net.WebUtility.HtmlEncode(correlationId)+"</p></body></html>",context.RequestAborted).ConfigureAwait(false);
                return;
            }
            var error = new WireApiError(
                Code: ServerErrorCodes.Unexpected,
                Detail: environment.IsDevelopment() ? ex.Message : null,
                Fields: NoFields,
                CorrelationId: correlationId);

            await context.Response.WriteAsJsonAsync(error, context.RequestAborted).ConfigureAwait(false);
        }
    }
}
