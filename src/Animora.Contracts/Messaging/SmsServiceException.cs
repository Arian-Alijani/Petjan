using System;

namespace Animora.Contracts.Messaging;

/// <summary>Safe failure categories across the «مدیریت پیامک» seam; cancellation stays <see cref="OperationCanceledException"/>.</summary>
public enum SmsFailureKind
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
/// Thrown by <see cref="ISmsService"/> when a read or save fails. Mirrors the «آمار»
/// seam's error shape (whitelisted kind + machine code + opaque correlation id) and
/// never carries an exception payload, identity or SQL.
/// </summary>
public sealed class SmsServiceException : InvalidOperationException
{
    public SmsFailureKind Kind { get; }
    public string Code { get; }
    public string CorrelationId { get; }

    /// <summary>Only a transient store problem is worth an automatic «تلاش مجدد».</summary>
    public bool CanRetry => Kind == SmsFailureKind.StoreUnavailable;

    public string UserMessage => Kind switch
    {
        SmsFailureKind.SessionExpired => "نشست شما منقضی شده است؛ دوباره وارد شوید.",
        SmsFailureKind.PermissionDenied => "اجازه مدیریت پیامک را ندارید.",
        SmsFailureKind.StoreUnavailable => "پایگاه داده در دسترس نیست؛ دوباره تلاش کنید.",
        SmsFailureKind.InvalidInput => "اطلاعات واردشده معتبر نیست؛ فیلدها را بررسی کنید.",
        SmsFailureKind.NotFound => "مورد درخواستی پیدا نشد.",
        SmsFailureKind.Conflict => "وضعیت داده تغییر کرده است؛ صفحه را تازه‌سازی کنید.",
        _ => "خطای غیرمنتظره در بخش مدیریت پیامک؛ شناسه پیگیری را به پشتیبانی بدهید.",
    };

    public SmsServiceException(SmsFailureKind kind, string code, string correlationId) : base(code)
    {
        Kind = kind;
        Code = code;
        CorrelationId = correlationId;
    }
}
