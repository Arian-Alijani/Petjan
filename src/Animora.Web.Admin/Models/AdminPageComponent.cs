using Animora.Domain.Results;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
namespace Animora.Web.Admin.Models;

public abstract class AdminPageComponent : ComponentBase
{
    [CascadingParameter] public HttpContext? Http { get; set; }
    protected CancellationToken Cancellation=>Http?.RequestAborted??default;
    protected string? Error;
    protected T? Capture<T>(Result<T> result)
    {
        Error=result.IsFailure?result.Error.Code:null;
        if(result.IsFailure&&result.Error==ErrorCodes.ConcurrencyConflict&&Http is { Response.HasStarted:false }) Http.Response.StatusCode=412;
        return result.IsSuccess?result.Value:default;
    }
}
