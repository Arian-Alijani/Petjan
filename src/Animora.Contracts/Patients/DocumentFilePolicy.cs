namespace Animora.Contracts.Patients;

/// <summary>Why a file may not be attached to a patient record.</summary>
public enum DocumentFileProblem
{
    /// <summary>The file is acceptable.</summary>
    None,

    /// <summary>Not a JPEG / PNG / WebP / GIF image or a PDF, or the extension and the declared type disagree.</summary>
    UnsupportedType,

    /// <summary>Zero bytes.</summary>
    Empty,

    /// <summary>Larger than <see cref="DocumentFilePolicy.MaxBytes"/>.</summary>
    TooLarge,
}

/// <summary>
/// The single rule set for a file attached to a patient's file: a JPEG / PNG / WebP / GIF image or a PDF, at most
/// <see cref="MaxBytes"/>. Every layer consults this one type — the desktop picker (so an invalid file is never even
/// offered), the client service (before a single byte is buffered), the command validator, and the server (which
/// additionally re-hashes the payload and re-sniffs it at <c>complete</c>).
/// </summary>
/// <remarks>
/// It lives in Contracts because Contracts is the only assembly every layer references, and it deliberately has no
/// project references of its own. Static data by design: what may be attached to a medical record is a product rule,
/// not tenant configuration, and it must produce the same verdict on every device while offline. Nothing here touches
/// the file system, the network or the clock.
/// </remarks>
public static class DocumentFilePolicy
{
    /// <summary>Hard cap on one attached file, 20 MiB. A lab report or a radiograph fits easily.</summary>
    public const long MaxBytes = 20L * 1024 * 1024;

    /// <summary>Bytes read to identify the format. Every signature below fits in the first 16 bytes.</summary>
    public const int SignatureBytes = 512;

    /// <summary>The extensions the picker offers and the service accepts.</summary>
    public static readonly IReadOnlyList<string> AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif", ".pdf"];

    /// <summary>The content types accepted, one per allowed format.</summary>
    public static readonly IReadOnlyList<string> AllowedContentTypes =
        ["image/jpeg", "image/png", "image/webp", "image/gif", "application/pdf"];

    /// <summary>File-picker patterns for the allowed extensions (e.g. <c>*.jpg</c>).</summary>
    public static readonly IReadOnlyList<string> PickerPatterns =
        [.. AllowedExtensions.Select(e => "*" + e)];

    /// <summary>Extension → the one content type it may carry (a caller-supplied pair that disagrees is refused).</summary>
    private static readonly Dictionary<string, string> TypeByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif",
        [".pdf"] = "application/pdf",
    };

    /// <summary>The canonical content type of <paramref name="fileName"/>, or <see langword="null"/> when not allowed.</summary>
    public static string? ContentTypeFor(string? fileName) =>
        fileName is not null && TypeByExtension.TryGetValue(Path.GetExtension(fileName), out var type) ? type : null;

    /// <summary>True when the extension is one of <see cref="AllowedExtensions"/>.</summary>
    public static bool IsAllowedExtension(string? fileName) => ContentTypeFor(fileName) is not null;

    /// <summary>True when the declared content type is one of <see cref="AllowedContentTypes"/> (parameters ignored).</summary>
    public static bool IsAllowedContentType(string? contentType)
    {
        var type = Normalize(contentType);
        return type is not null && AllowedContentTypes.Contains(type, StringComparer.Ordinal);
    }

    /// <summary>
    /// True when the leading bytes really are the declared format. The extension and the MIME type are caller
    /// claims; this is the only check that sees the payload. Native executables are refused outright.
    /// </summary>
    public static bool MatchesContentType(string? contentType, ReadOnlySpan<byte> head)
    {
        var type = Normalize(contentType);
        if (type is null || !IsAllowedContentType(type) || head.Length < 4)
        {
            return false;
        }

        if (head.StartsWith("MZ"u8) || head.StartsWith(new byte[] { 0x7f, 0x45, 0x4c, 0x46 }))
        {
            return false;
        }

        return type switch
        {
            "image/jpeg" => head.StartsWith(new byte[] { 0xff, 0xd8, 0xff }),
            "image/png" => head.StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }),
            "image/gif" => head.StartsWith("GIF87a"u8) || head.StartsWith("GIF89a"u8),
            "image/webp" => head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head.Slice(8, 4).SequenceEqual("WEBP"u8),
            "application/pdf" => head.StartsWith("%PDF-"u8),
            _ => false,
        };
    }

    /// <summary>Name + declared type only — checkable before a byte is read.</summary>
    public static DocumentFileProblem ValidateType(string? fileName, string? contentType)
    {
        if (string.IsNullOrWhiteSpace(fileName) || !IsAllowedExtension(fileName))
        {
            return DocumentFileProblem.UnsupportedType;
        }

        var declared = Normalize(contentType);
        // An absent content type is tolerated: the extension decides, and the magic bytes still have to agree.
        if (declared is not null && !string.Equals(declared, ContentTypeFor(fileName), StringComparison.Ordinal))
        {
            return DocumentFileProblem.UnsupportedType;
        }

        return DocumentFileProblem.None;
    }

    /// <summary>Size only — checkable while reading, so an oversized stream is stopped before it is fully buffered.</summary>
    public static DocumentFileProblem ValidateSize(long sizeBytes) => sizeBytes switch
    {
        <= 0 => DocumentFileProblem.Empty,
        > MaxBytes => DocumentFileProblem.TooLarge,
        _ => DocumentFileProblem.None,
    };

    /// <summary>Null-equivalent (<see cref="DocumentFileProblem.None"/>) when the file may be attached.</summary>
    public static DocumentFileProblem Validate(string? fileName, string? contentType, long sizeBytes)
    {
        var type = ValidateType(fileName, contentType);
        return type != DocumentFileProblem.None ? type : ValidateSize(sizeBytes);
    }

    /// <summary>The stable SCREAMING_SNAKE code of a problem — the same strings the error catalog uses.</summary>
    public static string CodeOf(DocumentFileProblem problem) => problem switch
    {
        DocumentFileProblem.UnsupportedType => "FILE_TYPE_NOT_ALLOWED",
        DocumentFileProblem.TooLarge => "FILE_TOO_LARGE",
        DocumentFileProblem.Empty => "VALIDATION_FAILED",
        _ => "VALIDATION_FAILED",
    };

    /// <summary>Lower-cased type without its <c>;charset=…</c> parameters; null for an empty value.</summary>
    private static string? Normalize(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return null;
        }

        var type = contentType.Split(';')[0].Trim().ToLowerInvariant();
        return type.Length == 0 ? null : type;
    }
}
