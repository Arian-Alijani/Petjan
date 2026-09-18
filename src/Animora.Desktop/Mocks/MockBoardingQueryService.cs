using Animora.Contracts.Boarding;

namespace Animora.Desktop.Mocks;

/// <summary>
/// Deterministic in-memory implementation of <see cref="IBoardingQueryService"/>.
/// The six stays are re-derived from what the reference screen visually needs
/// (Animora-UI/assets/boarding.js <c>STAYS</c>) so the rebuilt board renders the
/// same cards, status pills, counts and Persian text. Check-in/check-out dates
/// are expressed relative to the live Jalali "today" — exactly like the
/// reference's <c>day(offset)</c> — so the derived status distribution
/// (اقامت فعلی / تخلیه امروز / تخلیه‌شده) and therefore every tab count matches.
/// This is the single swap point a future backend replaces (R-18) — no
/// ViewModel or View change is required to do so.
/// </summary>
public sealed class MockBoardingQueryService : IBoardingQueryService
{
    private readonly IReadOnlyList<BoardingStay> _stays;

    public MockBoardingQueryService()
    {
        // The reference pins "today" (1404/05/11); re-anchor to the live date so
        // the relative check-in/out offsets keep their reference distribution.
        var today = DateOnly.FromDateTime(DateTime.Today);
        DateOnly Day(int offset) => today.AddDays(offset);

        _stays =
        [
            new BoardingStay(
                Id: "bd-1", PetName: "لئو", Species: BoardingSpecies.Dog, Breed: "ژرمن شپرد",
                OwnerName: "امیر رستگار", OwnerPhone: "۰۹۳۵ ۱۱۲ ۲۳۳۴", PatientId: "pt-3",
                Room: "A-2", CheckIn: Day(-3), CheckOut: Day(1), DailyRate: 800_000,
                PaymentMode: BoardingPaymentMode.Postpaid, Status: BoardingStatus.Active,
                Tone: "b", Transferred: false,
                Note: "پیاده‌روی هر صبح؛ حساسیت غذایی به مرغ — فقط غذای ماهی.",
                ExtraItemsCost: 1_300_000), // it-3 (۱×۸۵۰٬۰۰۰) + bx-1 (۳×۱۵۰٬۰۰۰)

            new BoardingStay(
                Id: "bd-2", PetName: "میشا", Species: BoardingSpecies.Cat, Breed: "پرشین",
                OwnerName: "نگار محمدی", OwnerPhone: "۰۹۱۲ ۳۴۵ ۶۷۸۹", PatientId: "pt-1",
                Room: "B-1", CheckIn: Day(-5), CheckOut: Day(0), DailyRate: 550_000,
                PaymentMode: BoardingPaymentMode.Prepaid, Status: BoardingStatus.Active,
                Tone: "a", Transferred: false,
                Note: "داروی قطره چشم روزی دو بار — توسط پرسنل.",
                ExtraItemsCost: 1_020_000), // it-2 (۴×۸۵٬۰۰۰) + it-8 (۱×۲۸۰٬۰۰۰) + bx-2 (۱×۴۰۰٬۰۰۰)

            new BoardingStay(
                Id: "bd-3", PetName: "گلچین", Species: BoardingSpecies.Bird, Breed: "کاسکو",
                OwnerName: "سارا یوسفی", OwnerPhone: "۰۹۱۹ ۸۷۶ ۵۴۳۲", PatientId: "",
                Room: "B-2", CheckIn: Day(-10), CheckOut: Day(4), DailyRate: 320_000,
                PaymentMode: BoardingPaymentMode.Postpaid, Status: BoardingStatus.Active,
                Tone: "e", Transferred: false,
                Note: "",
                ExtraItemsCost: 150_000), // it-9 (۱×۱۵۰٬۰۰۰)

            new BoardingStay(
                Id: "bd-4", PetName: "کوکو", Species: BoardingSpecies.Rabbit, Breed: "خرگوش لوپ",
                OwnerName: "مریم آذری", OwnerPhone: "۰۹۱۰ ۵۵۶ ۶۷۷۸", PatientId: "",
                Room: "A-1", CheckIn: Day(-8), CheckOut: Day(-2), DailyRate: 380_000,
                PaymentMode: BoardingPaymentMode.Prepaid, Status: BoardingStatus.Done,
                Tone: "f", Transferred: true,
                Note: "تحویل با رضایت کامل.",
                ExtraItemsCost: 450_000), // it-5 (۱×۴۵۰٬۰۰۰)

            new BoardingStay(
                Id: "bd-5", PetName: "هاپو", Species: BoardingSpecies.Dog, Breed: "گلدن رتریور",
                OwnerName: "فاطمه رحیمی", OwnerPhone: "۰۹۳۵ ۱۰۸ ۵۵۶۶", PatientId: "pt-10",
                Room: "A-3", CheckIn: Day(-1), CheckOut: Day(5), DailyRate: 900_000,
                PaymentMode: BoardingPaymentMode.Postpaid, Status: BoardingStatus.Active,
                Tone: "c", Transferred: false,
                Note: "حساسیت پوستی — از صابون معمولی استفاده نشود.",
                ExtraItemsCost: 0),

            new BoardingStay(
                Id: "bd-6", PetName: "سیمبا", Species: BoardingSpecies.Cat, Breed: "مین‌کون",
                OwnerName: "کیانا فتحی", OwnerPhone: "۰۹۲۱ ۶۶۴ ۲۲۱۰", PatientId: "pt-11",
                Room: "VIP", CheckIn: Day(-12), CheckOut: Day(-5), DailyRate: 1_200_000,
                PaymentMode: BoardingPaymentMode.Prepaid, Status: BoardingStatus.Done,
                Tone: "d", Transferred: false,
                Note: "",
                ExtraItemsCost: 1_350_000), // bx-3 (۱×۵۵۰٬۰۰۰) + bx-2 (۲×۴۰۰٬۰۰۰)
        ];
    }

    public Task<IReadOnlyList<BoardingStay>> GetStaysAsync() =>
        Task.FromResult(_stays);
}
