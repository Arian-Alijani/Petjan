using System.Collections.Generic;
using System.Threading.Tasks;
using Animora.Contracts.Dashboard;
using Animora.Contracts.Navigation;

namespace Animora.Desktop.Mocks;

/// <summary>
/// Deterministic in-memory dashboard data per role, re-derived (not copied)
/// from what DASH_VIEW_MANAGER/CASHIER/DOCTOR visually render
/// (Animora-UI/assets/dashboard-views.js) — a handful of representative KPI
/// numbers and rows per role/section, not the full reference data set.
/// Registered as a singleton (stateless, pure data); see PROGRESS.md.
/// </summary>
public sealed class MockDashboardQueryService : IDashboardQueryService
{
    public Task<DashboardSnapshot> GetSnapshotAsync(UserRole role) => Task.FromResult(role switch
    {
        UserRole.Cashier => Cashier(),
        UserRole.Doctor => Doctor(),
        _ => Manager(),
    });

    private static DashboardSnapshot Manager() => new(
        UserRole.Manager,
        HeroTitle: "صبح بخیر، دکتر آریا",
        HeroSubtitle: "نمای کلی امروز کلینیک — درآمد، ویزیت‌ها و هشدارهای مهم.",
        Stats: new[]
        {
            new DashboardStat("IconChart", "درآمد امروز", "۸۶٫۴ م", "۲۴٪↑", true),
            new DashboardStat("IconUsers", "بیماران فعال", "۱٬۲۸۴", "۸٪↑", true),
            new DashboardStat("IconClipboard", "ویزیت‌های امروز", "۱۸ ویزیت", "۷۵٪", true),
            new DashboardStat("IconBox", "هشدار موجودی", "۳", "۲٫۱٪↓", false),
        },
        PrimaryListTitle: "صف زندهٔ امروز",
        PrimaryList: new[]
        {
            new DashboardListItem("م", "میلو (سگ پامرانین)", "رضا کریمی", "در انتظار", DashboardTone.Neutral),
            new DashboardListItem("ن", "نانا (گربه اسکاتیش)", "الهام صادقی", "فراخوانده", DashboardTone.Info),
            new DashboardListItem("ر", "رکس (سگ هاسکی)", "محسن دهقان", "اورژانس", DashboardTone.Danger),
            new DashboardListItem("ل", "لئو (سگ ژرمن)", "امیر رستگار", "در ویزیت", DashboardTone.Warn),
            new DashboardListItem("س", "سیمبا (گربه بنگال)", "حسین مرادی", "در انتظار", DashboardTone.Neutral),
        },
        SecondaryListTitle: "هشدارهای مدیریتی",
        SecondaryList: new[]
        {
            new DashboardListItem("!", "۳ قلم کالا زیر حد بحرانی", "سرم، آنتی‌بیوتیک، پوشک", "بحرانی", DashboardTone.Danger),
            new DashboardListItem("چ", "۲ چک سررسید نزدیک", "۴۵ م تومان — بررسی وضعیت", "این هفته", DashboardTone.Warn),
            new DashboardListItem("و", "۶ یادآور واکسیناسیون امروز", "پیامک یادآور ارسال نشده", "امروز", DashboardTone.Info),
            new DashboardListItem("پ", "۳ پانسیون فعال", "۲ تسویه‌شده · ۱ در انتظار", "فعال", DashboardTone.Neutral),
        });

    private static DashboardSnapshot Cashier() => new(
        UserRole.Cashier,
        HeroTitle: "صندوق و مالی",
        HeroSubtitle: "گردش نقدی، فاکتورهای باز و آمادهٔ صدور امروز.",
        Stats: new[]
        {
            new DashboardStat("IconCart", "گردش نقدی امروز", "۲٫۴۵ م", "۴ ثبت‌شده", true),
            new DashboardStat("IconReceipt", "مانده فاکتورهای باز", "۲٫۹۵ م", "۲ باز", false),
            new DashboardStat("IconClipboard", "آماده صدور فاکتور", "۳", "آماده", true),
            new DashboardStat("IconCheque", "چک‌های در جریان", "۴۵ م", "۲ چک", true),
        },
        PrimaryListTitle: "فاکتورهای باز",
        PrimaryList: new[]
        {
            new DashboardListItem("ر", "رضا کریمی — میلو", "مانده: ۲۰۰٬۰۰۰ تومان", "تسویه جزئی", DashboardTone.Warn),
            new DashboardListItem("ه", "الهام صادقی — نانا", "مانده: ۱۵۰٬۰۰۰ تومان", "پیش‌نویس", DashboardTone.Neutral),
            new DashboardListItem("م", "محسن دهقان — رکس", "مانده: ۰ تومان", "تسویه کامل", DashboardTone.Ok),
        },
        SecondaryListTitle: "آماده صدور فاکتور",
        SecondaryList: new[]
        {
            new DashboardListItem("س", "سیمبا (گربه بنگال)", "ویزیت پایان‌یافته — بدون تشخیص ثبت", "آماده", DashboardTone.Info),
            new DashboardListItem("ل", "لئو (سگ ژرمن)", "واکسن هاری سالانه", "آماده", DashboardTone.Info),
        });

    private static DashboardSnapshot Doctor() => new(
        UserRole.Doctor,
        HeroTitle: "روز کاری شما",
        HeroSubtitle: "دکتر مینا همتی — واکسن و پیشگیری",
        Stats: new[]
        {
            new DashboardStat("IconCheck", "ویزیت انجام‌شده", "۵", "۷۵٪", true),
            new DashboardStat("IconClock", "در صف شما", "۳", "زنده", false),
            new DashboardStat("IconEdit", "پرونده ناتمام", "۱", "نیازمند تکمیل", false),
            new DashboardStat("IconSyringe", "واکسن باقی‌مانده", "۲", "۴/۶ انجام", true),
        },
        PrimaryListTitle: "صف زندهٔ امروز",
        PrimaryList: new[]
        {
            new DashboardListItem("س", "سیمبا (گربه بنگال)", "حسین مرادی · واکسیناسیون", "در انتظار", DashboardTone.Neutral),
            new DashboardListItem("ر", "رکس (سگ هاسکی)", "محسن دهقان · ویزیت", "اورژانس", DashboardTone.Danger),
            new DashboardListItem("ب", "بلا (سگ شیتزو)", "زهرا نوری · ویزیت", "در انتظار", DashboardTone.Neutral),
        },
        SecondaryListTitle: "یادآورهای واکسیناسیون امروز",
        SecondaryList: new[]
        {
            new DashboardListItem("ل", "لئو (سگ ژرمن)", "هاری سالانه — امیر رستگار", "امروز", DashboardTone.Warn),
            new DashboardListItem("ن", "نانا (گربه اسکاتیش)", "سه‌گانه گربه — الهام صادقی", "در حال انجام", DashboardTone.Info),
            new DashboardListItem("ر", "رکس (سگ هاسکی)", "هاری (تکرار) — محسن دهقان", "انجام شد", DashboardTone.Ok),
        });
}
