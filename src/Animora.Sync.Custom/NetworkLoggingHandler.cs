using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;

namespace Animora.Sync.Custom;

/// <summary>
/// Diagnostic <see cref="DelegatingHandler"/> that prints a full, step-by-step trace of EVERY
/// HTTP exchange with the Animora server to the console: the outgoing request (method, URL,
/// headers, body), the server's response (status, elapsed time, headers, body) and any transport
/// error (timeout, DNS, refused connection, …).
///
/// <para>
/// It is installed as the OUTERMOST handler on the shared <c>animora-sync</c> client, so it brackets
/// the whole pipeline: it logs each logical call before <see cref="BearerAuthHandler"/> runs and the
/// final response/timing/transport-error coming back — including the total time across any 401-refresh
/// retry the auth handler performs internally. (The Authorization header itself is attached deeper in
/// the pipeline and is redacted anyway, so not seeing it here is intentional.) This is purely a
/// debugging aid for diagnosing server-connection problems; it is wired only when the
/// <c>ANIMORA_NET_LOG</c> environment variable is set to <c>1</c>/<c>true</c> (the server-connected
/// launcher sets it), so normal release runs pay nothing for it.
/// </para>
///
/// <para>
/// SECURITY: bearer tokens, cookies and obvious credential fields (password / token / secret) are
/// redacted so the trace can be copied into a bug report without leaking secrets.
/// </para>
/// </summary>
public sealed class NetworkLoggingHandler : DelegatingHandler
{
    // Cap how much of a body we print so a huge sync page / state dump cannot flood the terminal.
    private const int MaxBodyChars = 8 * 1024;

    // Correlates the request line with its matching response line in an interleaved, multi-request log.
    private static int _sequence;

    // One writer at a time so concurrent requests do not interleave mid-line on the console.
    private static readonly object ConsoleLock = new();

    /// <summary>True when the launcher asked for the network trace (<c>ANIMORA_NET_LOG=1|true|yes|on</c>).</summary>
    public static bool IsEnabled
    {
        get
        {
            var raw = Environment.GetEnvironmentVariable("ANIMORA_NET_LOG");
            if (string.IsNullOrWhiteSpace(raw)) return false;
            raw = raw.Trim();
            return raw is "1"
                || raw.Equals("true", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("on", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var id = Interlocked.Increment(ref _sequence);
        await LogRequestAsync(id, request, cancellationToken).ConfigureAwait(false);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            await LogResponseAsync(id, request, response, stopwatch.Elapsed, cancellationToken).ConfigureAwait(false);
            return response;
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            LogError(id, request, stopwatch.Elapsed, "CANCELLED (request aborted / app shutting down)", ex);
            throw;
        }
        catch (TaskCanceledException ex) // request timeout (per-request CTS) — not a user cancel
        {
            stopwatch.Stop();
            LogError(id, request, stopwatch.Elapsed, "TIMEOUT (no response before the request timeout)", ex);
            throw;
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            // The inner exception usually carries the real cause (connection refused, DNS, TLS, host down).
            var detail = ex.InnerException is { } inner ? $"{ex.Message} -> {inner.GetType().Name}: {inner.Message}" : ex.Message;
            LogError(id, request, stopwatch.Elapsed, $"TRANSPORT ERROR ({detail})", ex);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            LogError(id, request, stopwatch.Elapsed, $"UNEXPECTED ERROR ({ex.GetType().Name})", ex);
            throw;
        }
    }

    private static async Task LogRequestAsync(int id, HttpRequestMessage request, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.Append("\n┌─ [#").Append(id).Append("] → REQUEST  ").Append(Timestamp()).Append('\n');
        sb.Append("│  ").Append(request.Method).Append("  ").Append(request.RequestUri).Append('\n');
        AppendHeaders(sb, "│  ", request.Headers, request.Content?.Headers);
        await AppendBodyAsync(sb, "│  ", request.Content, ct).ConfigureAwait(false);
        sb.Append("└───────────────────────────────────────────");
        Write(ConsoleColor.Cyan, sb.ToString());
    }

    private static async Task LogResponseAsync(int id, HttpRequestMessage request, HttpResponseMessage response, TimeSpan elapsed, CancellationToken ct)
    {
        var code = (int)response.StatusCode;
        var ok = response.IsSuccessStatusCode;
        var color = ok ? ConsoleColor.Green : (code >= 500 ? ConsoleColor.Red : ConsoleColor.Yellow);

        var sb = new StringBuilder();
        sb.Append("\n┌─ [#").Append(id).Append("] ← RESPONSE ").Append(Timestamp())
          .Append("  (").Append(elapsed.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)).Append(" ms)\n");
        sb.Append("│  ").Append(code).Append(' ').Append(response.ReasonPhrase)
          .Append("   for ").Append(request.Method).Append(' ').Append(request.RequestUri?.PathAndQuery).Append('\n');
        AppendHeaders(sb, "│  ", response.Headers, response.Content?.Headers);
        await AppendBodyAsync(sb, "│  ", response.Content, ct).ConfigureAwait(false);
        sb.Append("└───────────────────────────────────────────");
        Write(color, sb.ToString());
    }

    private static void LogError(int id, HttpRequestMessage request, TimeSpan elapsed, string summary, Exception ex)
    {
        var sb = new StringBuilder();
        sb.Append("\n┌─ [#").Append(id).Append("] ✖ FAILED   ").Append(Timestamp())
          .Append("  (").Append(elapsed.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)).Append(" ms)\n");
        sb.Append("│  ").Append(request.Method).Append(' ').Append(request.RequestUri).Append('\n');
        sb.Append("│  ").Append(summary).Append('\n');
        sb.Append("│  exception: ").Append(ex.GetType().FullName).Append(": ").Append(ex.Message).Append('\n');
        sb.Append("└───────────────────────────────────────────");
        Write(ConsoleColor.Red, sb.ToString());
    }

    private static void AppendHeaders(StringBuilder sb, string prefix, HttpHeaders headers, HttpHeaders? contentHeaders)
    {
        foreach (var header in headers)
            sb.Append(prefix).Append(header.Key).Append(": ").Append(Redact(header.Key, string.Join(", ", header.Value))).Append('\n');

        if (contentHeaders is null) return;
        foreach (var header in contentHeaders)
            sb.Append(prefix).Append(header.Key).Append(": ").Append(Redact(header.Key, string.Join(", ", header.Value))).Append('\n');
    }

    private static async Task AppendBodyAsync(StringBuilder sb, string prefix, HttpContent? content, CancellationToken ct)
    {
        if (content is null) return;

        var mediaType = content.Headers.ContentType?.MediaType ?? string.Empty;
        // Only print textual payloads; binary sync pages (application/gzip, octet-stream) are summarized by size.
        var isText = mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)
            || mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);

        if (!isText)
        {
            var len = content.Headers.ContentLength;
            sb.Append(prefix).Append("body: <")
              .Append(string.IsNullOrEmpty(mediaType) ? "binary" : mediaType)
              .Append(len.HasValue ? $", {len.Value} bytes" : string.Empty)
              .Append("> (not shown)\n");
            return;
        }

        try
        {
            // ReadAsStringAsync buffers the content; because HttpClient buffers responses and the
            // BearerAuthHandler buffers request bodies, reading here does not consume the stream the
            // caller needs. (Request content is logged after the inner handler already buffered it.)
            var text = await content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(text))
            {
                sb.Append(prefix).Append("body: <empty>\n");
                return;
            }

            var redacted = RedactBody(text);
            var truncated = redacted.Length > MaxBodyChars;
            if (truncated) redacted = redacted[..MaxBodyChars];

            sb.Append(prefix).Append("body (").Append(mediaType).Append("):\n");
            foreach (var line in redacted.Split('\n'))
                sb.Append(prefix).Append("    ").Append(line.TrimEnd('\r')).Append('\n');
            if (truncated) sb.Append(prefix).Append("    … [truncated, body larger than ").Append(MaxBodyChars).Append(" chars]\n");
        }
        catch (Exception ex)
        {
            sb.Append(prefix).Append("body: <could not read: ").Append(ex.GetType().Name).Append(": ").Append(ex.Message).Append(">\n");
        }
    }

    // --- redaction -----------------------------------------------------------------------------

    private static string Redact(string headerName, string value)
    {
        if (headerName.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
        {
            // Keep the scheme (Bearer) so the trace still shows a token WAS present, hide the token.
            var space = value.IndexOf(' ');
            return space > 0 ? $"{value[..space]} <redacted>" : "<redacted>";
        }

        if (headerName.Equals("Cookie", StringComparison.OrdinalIgnoreCase)
            || headerName.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
            return "<redacted>";

        return value;
    }

    private static readonly string[] SensitiveKeys = ["password", "passwordhash", "accesstoken", "refreshtoken", "token", "secret", "apikey", "api_key"];

    private static string RedactBody(string body)
    {
        // Lightweight, dependency-free redaction of JSON string values for sensitive keys:
        //   "password":"abc"  ->  "password":"<redacted>"
        // We avoid a full JSON parse so malformed/partial bodies still log.
        var result = body;
        foreach (var key in SensitiveKeys)
        {
            result = System.Text.RegularExpressions.Regex.Replace(
                result,
                "(\"" + System.Text.RegularExpressions.Regex.Escape(key) + "\"\\s*:\\s*\")(.*?)(\")",
                "$1<redacted>$3",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        return result;
    }

    // --- console I/O ---------------------------------------------------------------------------

    private static string Timestamp() => DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    private static void Write(ConsoleColor color, string message)
    {
        lock (ConsoleLock)
        {
            ConsoleColor? previous = null;
            try
            {
                previous = Console.ForegroundColor;
                Console.ForegroundColor = color;
            }
            catch
            {
                // No console attached / redirected: color is a best-effort nicety, never fatal.
            }

            Console.Out.WriteLine(message);
            Console.Out.Flush();

            if (previous is { } prev)
            {
                try { Console.ForegroundColor = prev; } catch { /* ignore */ }
            }
        }
    }
}
