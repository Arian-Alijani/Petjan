using Microsoft.AspNetCore.ResponseCompression;
namespace Animora.Server.Observability;

internal sealed class JsonCompressionMiddleware(RequestDelegate next,IResponseCompressionProvider provider)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // Buffer only the bounded command response, never file/feed streams. Content length must be known
        // before compression middleware chooses eligibility; skip very small payloads.
        var original=context.Response.Body; await using var buffer=new MemoryStream(); context.Response.Body=buffer;
        try
        {
            await next(context).ConfigureAwait(false); context.Response.Body=original;
            context.Response.ContentLength=buffer.Length; buffer.Position=0;
            bool json=context.Response.ContentType?.StartsWith("application/json",StringComparison.OrdinalIgnoreCase)==true;
            if(buffer.Length>=1024&&json&&provider.CheckRequestAcceptsCompression(context))
            {
                var compressor=provider.GetCompressionProvider(context);
                if(compressor is not null)
                {
                    context.Response.Headers.ContentEncoding=compressor.EncodingName; context.Response.Headers.Vary="Accept-Encoding";
                    context.Response.ContentLength=null; await using var compressed=compressor.CreateStream(original);
                    await buffer.CopyToAsync(compressed,context.RequestAborted).ConfigureAwait(false); return;
                }
            }
            await buffer.CopyToAsync(original,context.RequestAborted).ConfigureAwait(false);
        }
        finally { context.Response.Body=original; }
    }
}
