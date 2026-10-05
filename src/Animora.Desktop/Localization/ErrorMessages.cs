using System;
using System.Collections.Generic;

namespace Animora.Desktop.Localization;

/// <summary>
/// Single Persian presentation of backend error codes (SCREAMING_SNAKE). Any place that would otherwise
/// show a raw <c>result.Error</c> / <c>ServiceException.Code</c> to the operator goes through here, so an
/// English machine code never reaches the UI. Unknown codes return <c>null</c> so callers keep a
/// context-specific fallback.
/// </summary>
public static class ErrorMessages
{
    private static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Visits / reception
        ["TICKET_STATE_INVALID"] = "این نوبت دیگر قابل شروع نیست (لغو، عدم‌حضور یا تکمیل‌شده).",
        ["TICKET_NOT_FOUND"] = "نوبت موردنظر پیدا نشد؛ فهرست را بازخوانی کنید.",
        ["DOCTOR_NOT_AVAILABLE"] = "پزشک انتخاب‌شده در دسترس نیست.",
        ["WALKIN_PHONE_REQUIRED"] = "برای ویزیت بدون نوبت، شماره تماس لازم است.",
        ["VISIT_NOT_FOUND"] = "ویزیت موردنظر پیدا نشد.",
        ["VISIT_LOCKED"] = "این ویزیت قفل شده و قابل ویرایش نیست.",
        ["VISIT_NOT_EDITABLE"] = "این ویزیت قابل ویرایش نیست.",
        ["VISIT_INVALID_TRANSITION"] = "تغییر وضعیت ویزیت در این مرحله مجاز نیست.",
        ["VISIT_INCOMPLETE"] = "اطلاعات ویزیت کامل نیست.",
        ["VISIT_DIAGNOSIS_REQUIRED"] = "ثبت تشخیص برای پایان ویزیت الزامی است.",
        ["OWNER_NOT_FOUND"] = "مالک موردنظر پیدا نشد.",
        ["PET_NOT_FOUND"] = "بیمار موردنظر پیدا نشد.",
        ["APPOINTMENT_NOT_FOUND"] = "نوبت موردنظر پیدا نشد.",
        ["APPOINTMENT_SLOT_CONFLICT"] = "این زمان با نوبت دیگری تداخل دارد.",
        ["APPOINTMENT_STATE_INVALID"] = "وضعیت فعلی نوبت اجازهٔ این عملیات را نمی‌دهد.",
        ["APPOINTMENT_INVALID_TRANSITION"] = "تغییر وضعیت نوبت در این مرحله مجاز نیست.",

        // Sales / cashier
        ["CASH_SESSION_REQUIRED"] = "برای پرداخت نقدی باید نشست صندوق باز باشد.",
        ["CASH_SESSION_ALREADY_OPEN"] = "یک نشست صندوق از قبل باز است.",
        ["CASH_SESSION_CLOSED"] = "نشست صندوق بسته شده است.",
        ["INVOICE_NOT_FOUND"] = "فاکتور موردنظر پیدا نشد.",
        ["INVOICE_IMMUTABLE"] = "این فاکتور قطعی شده و قابل ویرایش نیست.",
        ["INVOICE_INVALID_TRANSITION"] = "تغییر وضعیت فاکتور در این مرحله مجاز نیست.",
        ["INVOICE_EMPTY"] = "فاکتور بدون ردیف قابل ثبت نیست.",
        ["INVOICE_NEGATIVE_TOTAL"] = "جمع فاکتور نمی‌تواند منفی باشد.",
        ["INVOICE_OWNER_REQUIRED"] = "انتخاب مشتری برای این فاکتور الزامی است.",
        ["INVOICE_VOID_AFTER_PAYMENT"] = "فاکتور دارای پرداخت است؛ ابتدا پرداخت‌ها را برگشت دهید.",
        ["INVOICE_LINE_INDEX_STALE"] = "فاکتور در این بین تغییر کرده است؛ ردیف‌ها دوباره بارگذاری شد.",
        ["PAYMENT_EXCEEDS_DUE"] = "مبلغ پرداخت از مانده فاکتور بیشتر است.",
        ["PAYMENT_DUPLICATE_OPERATION"] = "این پرداخت قبلاً ثبت شده است.",
        ["PAYMENT_METHOD_REFERENCE_REQUIRED"] = "برای این روش پرداخت، شمارهٔ مرجع الزامی است.",
        ["ADJUSTMENT_EXCEEDS_PAID"] = "مبلغ برگشت از مبلغ پرداخت‌شده بیشتر است.",
        ["INSUFFICIENT_STOCK"] = "موجودی کالا کافی نیست.",
        ["STOCK_WOULD_GO_NEGATIVE"] = "موجودی کالا کافی نیست.",
        ["PRODUCT_NOT_FOUND"] = "کالای موردنظر پیدا نشد.",
        ["HANDOFF_ALREADY_INVOICED"] = "برای این مورد قبلاً فاکتور صادر شده است.",

        // Boarding
        ["STAY_NOT_FOUND"] = "اقامت موردنظر پیدا نشد.",
        ["STAY_NOT_EDITABLE"] = "این اقامت قابل ویرایش نیست.",
        ["STAY_LOCKED"] = "این اقامت قفل شده و قابل ویرایش نیست.",
        ["STAY_ITEM_INDEX_STALE"] = "اقلام اقامت در این بین تغییر کرده است؛ دوباره بارگذاری شد.",
        ["STAY_INVOICE_REQUIRED"] = "برای ادامه، ابتدا باید فاکتور اقامت صادر شود.",
        ["STAY_INVALID_TRANSITION"] = "تغییر وضعیت اقامت در این مرحله مجاز نیست.",
        ["STAY_ALREADY_CHECKED_OUT"] = "این اقامت قبلاً ترخیص شده است.",
        ["STAY_DATES_INVALID"] = "تاریخ‌های اقامت معتبر نیست.",
        ["ROOM_AT_CAPACITY"] = "ظرفیت این اتاق تکمیل است.",
        ["ROOM_UNAVAILABLE"] = "این اتاق در دسترس نیست.",
        ["ROOM_NONE_AVAILABLE"] = "اتاق خالی موجود نیست.",
        ["ROOM_CODE_DUPLICATE"] = "اتاقی با این کد از قبل ثبت شده است.",
        ["ROOM_NOT_FOUND"] = "اتاق موردنظر پیدا نشد.",
        ["ROOM_CAPACITY_INVALID"] = "ظرفیت اتاق باید بین ۱ تا ۱۰ باشد.",
        ["SERVICE_NO_STOCK"] = "این خدمت موجودی ندارد.",

        // Generic codes (VALIDATION_FAILED / NOT_FOUND / CONCURRENCY_CONFLICT) are intentionally NOT mapped:
        // callers keep their own context-specific fallback for those.
        ["STORE_UNAVAILABLE"] = "پایگاه داده در دسترس نیست؛ کمی بعد دوباره تلاش کنید.",
        ["PERIOD_CLOSED"] = "دورهٔ مالی بسته شده است.",
    };

    /// <summary>Persian message for <paramref name="code"/>, or <c>null</c> when the code has no dedicated copy.</summary>
    public static string? TryGet(string? code) =>
        code is not null && Map.TryGetValue(code, out var message) ? message : null;

    /// <summary>
    /// Persian message for a failure whose text is the backend code (a result-level <c>Error</c>); anything
    /// unmapped — including free text or an unknown code — resolves to <paramref name="fallback"/> so a raw
    /// English code is never shown.
    /// </summary>
    public static string ForResult(string? error, string fallback) => TryGet(error?.Trim()) ?? fallback;
}
