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
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception for request {CorrelationId}.", correlationId);

            if (context.Response.HasStarted)
            {
                // Can no longer shape the response; let it surface as-is.
                throw;
            }

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;

            var error = new WireApiError(
                Code: ServerErrorCodes.Unexpected,
                Detail: environment.IsDevelopment() ? ex.Message : null,
                Fields: NoFields,
                CorrelationId: correlationId);

            await context.Response.WriteAsJsonAsync(error, context.RequestAborted).ConfigureAwait(false);
        }
    }
}
