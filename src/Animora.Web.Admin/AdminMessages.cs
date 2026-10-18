using Animora.Domain.Results;
namespace Animora.Web.Admin;

public static class AdminMessages
{
    private const string Generic = "The operation could not be completed. Contact support with the error code.";
    private const string GenericFa = "عملیات انجام نشد. با ذکر کد خطا با پشتیبانی تماس بگیرید.";

    private static readonly IReadOnlyDictionary<string,string> Messages=typeof(ErrorCodes).GetFields(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static)
        .Where(f=>f.FieldType==typeof(ErrorCode)).Select(f=>(ErrorCode)f.GetValue(null)!)
        .DistinctBy(e=>e.Code).ToDictionary(e=>e.Code,e=>e==ErrorCodes.ConcurrencyConflict?"Someone else changed this record. Reload before trying again.":
            e==ErrorCodes.OperatorCredentialsInvalid?"Unable to sign in. Check your credentials and try again.":
            e==ErrorCodes.InsightNotEnabled?"Insight is disabled until an explicit consent ADR is configured.":
            e==ErrorCodes.FileHashMismatch?"Only flagged mismatch files can be requeued or abandoned. Reload the list.":
            e==ErrorCodes.FileNotFound?"The file metadata could not be found.":
            e==ErrorCodes.ValidationFailed?"Check the form fields and try again.":Generic,StringComparer.Ordinal);

    // Persian texts for the codes the console surfaces specifically; every other code falls back to the generic Persian text.
    private static readonly IReadOnlyDictionary<string,string> MessagesFa=new Dictionary<string,string>(StringComparer.Ordinal)
    {
        [ErrorCodes.ConcurrencyConflict.Code]="رکورد توسط شخص دیگری تغییر کرده است. پیش از تلاش دوباره صفحه را بازخوانی کنید.",
        [ErrorCodes.OperatorCredentialsInvalid.Code]="ورود ناموفق بود. اطلاعات ورود را بررسی و دوباره تلاش کنید.",
        [ErrorCodes.InsightNotEnabled.Code]="تا زمانی که ADR رضایت‌نامه تنظیم نشود، Insight غیرفعال است.",
        [ErrorCodes.FileHashMismatch.Code]="فقط فایل‌های علامت‌خورده می‌توانند دوباره در صف قرار گیرند یا رها شوند. فهرست را بازخوانی کنید.",
        [ErrorCodes.FileNotFound.Code]="اطلاعات فایل پیدا نشد.",
        [ErrorCodes.ValidationFailed.Code]="فیلدهای فرم را بررسی و دوباره تلاش کنید.",
        [ErrorCodes.UsernameExists.Code]="این نام کاربری قبلاً در همین کلینیک ثبت شده است.",
        [ErrorCodes.PasswordPolicyViolation.Code]="رمز عبور سیاست امنیتی را رعایت نمی‌کند (حداقل ۸ کاراکتر، شامل حرف و عدد).",
        [ErrorCodes.StoreUnavailable.Code]="بکاپ ساخته نشد: ابزار pg_dump روی سرور در دسترس نیست یا اتصال به دیتابیس/نوشتن در پوشهٔ بکاپ ناموفق بود. تنظیمات Admin:PgDumpPath و Admin:BackupDirectory را بررسی کنید.",
        [ErrorCodes.NotFound.Code]="مورد درخواستی پیدا نشد.",
        [ErrorCodes.PermissionDenied.Code]="مجوز انجام این عملیات را ندارید.",
    };

    public static bool HasMessage(string code)=>Messages.ContainsKey(code);
    public static string For(string code)=>Messages.GetValueOrDefault(code,"The operation could not be completed.");

    /// <summary>The message in the requested language (<c>fa</c> or <c>en</c>); English is the default.</summary>
    public static string For(string code,string lang)
    {
        if(lang!=Localization.AdminCulture.Fa)return For(code);
        return MessagesFa.TryGetValue(code,out var fa)?fa:GenericFa;
    }
}
