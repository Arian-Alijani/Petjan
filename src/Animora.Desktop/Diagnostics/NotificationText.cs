namespace Animora.Desktop.Diagnostics;

/// <summary>Desktop-only Persian copy for machine-readable diagnostic notification codes.</summary>
public static class NotificationText
{
    public static string Resolve(string code, string? entityRef)
    {
        var text = code switch
        {
            "IDEMPOTENCY_FINGERPRINT_MISMATCH" => "یک مغایرت اطلاعاتی شناسایی شد و نیاز به بررسی دارد.",
            "CRASH_RECOVERY_REQUIRED" => "برنامه پس از توقف غیرمنتظره بازیابی شد. گزارش عیب‌یابی آماده است.",
            "STORE_CORRUPT" => "پایگاه داده محلی نیاز به بررسی دارد.",
            _ => "یک رویداد نیازمند توجه ثبت شد.",
        };

        return string.IsNullOrWhiteSpace(entityRef) ? text : $"{text} ({entityRef})";
    }
}
