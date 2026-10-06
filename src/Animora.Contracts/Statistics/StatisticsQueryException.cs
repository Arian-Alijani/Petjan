using System;

namespace Animora.Contracts.Statistics;

/// <summary>Safe failure categories across the «آمار» seam; cancellation stays <see cref="OperationCanceledException"/>.</summary>
public enum StatisticsFailureKind
{
    SessionExpired,
    PermissionDenied,
    StoreUnavailable,
    InvalidInput,
    NotFound,
    Conflict,
    Unexpected,
}

/// <summary>
/// Thrown by <see cref="IStatisticsQueryService"/> when a read or write fails. Carries a whitelisted kind, the
/// machine <see cref="Code"/> and an opaque correlation id — never an exception payload, identity or SQL.
/// </summary>
public sealed class StatisticsQueryException : InvalidOperationException
{
    public StatisticsFailureKind Kind { get; }
    public string Code { get; }
    public string CorrelationId { get; }

    /// <summary>Only a transient store problem is worth an automatic «تلاش مجدد».</summary>
    public bool CanRetry => Kind == StatisticsFailureKind.StoreUnavailable;

    public string UserMessage => Code switch
    {
        "RECURRING_EXPENSE_ALREADY_PAID" => "این هزینه ثابت برای دوره جاری قبلاً پرداخت شده است.",
        "RECURRING_EXPENSE_INACTIVE" => "این هزینه ثابت غیرفعال است و قابل پرداخت نیست.",
        "RECURRING_EXPENSE_NOT_FOUND" => "هزینه ثابت پیدا نشد.",
        "EXPENSE_NOT_FOUND" => "هزینه پیدا نشد.",
        "EXPENSE_NOT_REVERSIBLE" => "این هزینه قبلاً برگشت خورده یا قابل برگشت نیست.",
        "EXPENSE_CATEGORY_INVALID" => "دسته‌بندی هزینه معتبر نیست.",
        "PERIOD_CLOSED" => "دوره حسابداری بسته است؛ ثبت در این تاریخ ممکن نیست.",
        "FINANCE_RANGE_INVALID" => "بازه زمانی انتخاب‌شده معتبر نیست.",
        _ => Kind switch
        {
            StatisticsFailureKind.SessionExpired => "نشست شما منقضی شده است؛ دوباره وارد شوید.",
            StatisticsFailureKind.PermissionDenied => "اجازه انجام این کار را ندارید.",
            StatisticsFailureKind.StoreUnavailable => "پایگاه داده در دسترس نیست؛ دوباره تلاش کنید.",
            StatisticsFailureKind.InvalidInput => "اطلاعات واردشده معتبر نیست؛ فیلدها را بررسی کنید.",
            StatisticsFailureKind.NotFound => "مورد درخواستی پیدا نشد.",
            StatisticsFailureKind.Conflict => "وضعیت داده تغییر کرده است؛ صفحه را تازه‌سازی کنید.",
            _ => "خطای غیرمنتظره در بخش آمار؛ شناسه پیگیری را به پشتیبانی بدهید.",
        },
    };

    public StatisticsQueryException(StatisticsFailureKind kind, string code, string correlationId) : base(code)
    {
        Kind = kind;
        Code = code;
        CorrelationId = correlationId;
    }

    public static StatisticsFailureKind Classify(string code) => code switch
    {
        "SESSION_EXPIRED" or "UNAUTHENTICATED" or "OFFLINE_CAPABILITY_EXPIRED" => StatisticsFailureKind.SessionExpired,
        "PERMISSION_DENIED" or "PERMISSION_UNKNOWN" or "MEMBERSHIP_NOT_ACTIVE" or "TENANT_SUSPENDED"
            or "USER_LOCKED_OUT" or "CAPABILITY_NOT_GRANTED" or "CAPABILITY_SNAPSHOT_MISSING"
            or "CAPABILITY_SNAPSHOT_TAMPERED" => StatisticsFailureKind.PermissionDenied,
        "STORE_UNAVAILABLE" or "STORE_KEY_UNAVAILABLE" or "STORE_CORRUPT" => StatisticsFailureKind.StoreUnavailable,
        "VALIDATION_FAILED" or "EXPENSE_CATEGORY_INVALID" or "FINANCE_RANGE_INVALID" => StatisticsFailureKind.InvalidInput,
        "EXPENSE_NOT_FOUND" or "RECURRING_EXPENSE_NOT_FOUND" or "NOT_FOUND" => StatisticsFailureKind.NotFound,
        "EXPENSE_NOT_REVERSIBLE" or "RECURRING_EXPENSE_INACTIVE" or "RECURRING_EXPENSE_ALREADY_PAID" or "PERIOD_CLOSED"
            or "EXPENSE_IMMUTABLE" or "CONFLICT" => StatisticsFailureKind.Conflict,
        _ => StatisticsFailureKind.Unexpected,
    };
}
