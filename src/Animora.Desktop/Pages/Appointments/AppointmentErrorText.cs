using Animora.Contracts.Appointments;

namespace Animora.Desktop.Pages.Appointments;

/// <summary>
/// Maps a failed appointment operation to Persian user text (A10). The backend only returns stable codes, so this
/// is the single place that turns them into messages; unknown codes get a generic retry hint.
/// </summary>
public static class AppointmentErrorText
{
    public static string For(Exception ex) => ex switch
    {
        AppointmentOperationException { WriteCommitted: true } =>
            "تغییر ذخیره شد، اما بارگذاری دوبارهٔ اطلاعات انجام نشد؛ صفحه را تازه‌سازی کنید.",
        AppointmentOperationException op => ForCode(op.Code),
        InvalidOperationException { Message: var m } => ForCode(m),
        _ => "عملیات انجام نشد؛ دوباره تلاش کنید.",
    };

    public static string ForCode(string? code) => code switch
    {
        "APPOINTMENT_SLOT_CONFLICT" => "این بازه با نوبت دیگری تداخل دارد؛ زمان دیگری انتخاب کنید.",
        "DOCTOR_OUTSIDE_WORKING_HOURS" => "این زمان خارج از ساعت کاری پزشک است.",
        "APPOINTMENT_DURATION_INVALID" => "مدت نوبت معتبر نیست.",
        "APPOINTMENT_NOT_MOVABLE" => "این نوبت دیگر قابل جابه‌جایی نیست.",
        "APPOINTMENT_INVALID_TRANSITION" or "APPOINTMENT_STATE_INVALID" =>
            "وضعیت فعلی نوبت اجازهٔ این عملیات را نمی‌دهد؛ اطلاعات را تازه‌سازی کنید.",
        "APPOINTMENT_NOT_FOUND" => "این نوبت دیگر وجود ندارد.",
        "APPOINTMENT_RANGE_TOO_WIDE" => "بازهٔ انتخاب‌شده بیش از حد بزرگ است.",
        "CONCURRENCY_CONFLICT" => "نوبت در همین فاصله تغییر کرده است؛ اطلاعات را تازه‌سازی کنید.",
        "PATIENT_NOT_SELECTED" => "لطفاً ابتدا بیمار را انتخاب کنید.",
        "PATIENT_NOT_FOUND" => "بیمار انتخاب‌شده پیدا نشد.",
        "PERMISSION_DENIED" or "SESSION_EXPIRED" => "شما اجازهٔ انجام این عملیات را ندارید.",
        _ => "عملیات انجام نشد؛ دوباره تلاش کنید.",
    };

    /// <summary>True for cancellation, which must never be shown as a failure.</summary>
    public static bool IsCancellation(Exception ex) => ex is OperationCanceledException;
}
