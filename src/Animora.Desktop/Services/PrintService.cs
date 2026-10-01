using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Platform;

namespace Animora.Desktop.Services;

/// <inheritdoc cref="IPrintService"/>
public sealed class PrintService : IPrintService
{
    private static readonly (string File, int Weight)[] FontWeights =
    [
        ("Vazirmatn-Regular.ttf", 400),
        ("Vazirmatn-Medium.ttf", 500),
        ("Vazirmatn-Bold.ttf", 700),
        ("Vazirmatn-ExtraBold.ttf", 800),
    ];

    private readonly Lazy<string> _fontCss = new(BuildFontCss);

    public string EmbeddedFontCss => _fontCss.Value;

    public async Task<bool> OpenHtmlAsync(string fileStem, string html)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "Animora", "Print");
            Directory.CreateDirectory(dir);
            PruneOld(dir);

            var safe = string.Concat(fileStem.Split(Path.GetInvalidFileNameChars()));
            var path = Path.Combine(dir, $"{safe}-{DateTime.Now:yyyyMMdd-HHmmss}.html");
            await File.WriteAllTextAsync(path, html, new UTF8Encoding(true));

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Exception)
        {
            // No browser association / sandboxed FS: the caller shows a toast and
            // still offers «ذخیره فایل» as the fallback path.
            return false;
        }
    }

    /// <summary>Keeps the temp print folder from growing forever (files older than a day).</summary>
    private static void PruneOld(string dir)
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-1);
            foreach (var f in Directory.EnumerateFiles(dir, "*.html"))
            {
                if (File.GetLastWriteTime(f) < cutoff)
                {
                    File.Delete(f);
                }
            }
        }
        catch (Exception)
        {
            // Best-effort housekeeping only.
        }
    }

    private static string BuildFontCss()
    {
        var sb = new StringBuilder();
        foreach (var (file, weight) in FontWeights)
        {
            try
            {
                using var stream = AssetLoader.Open(new Uri($"avares://Animora.Desktop/Assets/Fonts/{file}"));
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                sb.Append("@font-face{font-family:'Vazirmatn';font-weight:").Append(weight)
                  .Append(";font-style:normal;src:url(data:font/ttf;base64,")
                  .Append(Convert.ToBase64String(ms.ToArray()))
                  .Append(") format('truetype');}\n");
            }
            catch (Exception)
            {
                // Missing weight → the browser synthesizes it from the nearest one.
            }
        }

        return sb.ToString();
    }
}
