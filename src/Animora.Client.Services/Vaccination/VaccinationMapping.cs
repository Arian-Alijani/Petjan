using System.Globalization;
using Animora.Application.Ports;
using Animora.Application.UseCases.Vaccination;
using Animora.Contracts.Identity;
using Animora.Contracts.Vaccination;
using Animora.Domain.Clinical;
using ContractRecord = Animora.Contracts.Vaccination.VaccinationRecord;

namespace Animora.Client.Services.Vaccination;

internal static class VaccinationMapping
{
    public static ContractRecord ToContract(this VaccinationDueRow row, SmsState sms = SmsState.None) =>
        new(row.Id, row.PetName, row.Species, row.OwnerName, row.OwnerPhone, row.VaccineCode,
            row.DoseNumber.ToString(CultureInfo.InvariantCulture), DateOnly.FromDateTime(row.AdministeredUtc.UtcDateTime),
            DateOnly.FromDateTime((row.NextDueUtc ?? row.AdministeredUtc).UtcDateTime),
            row.DoctorId, row.BatchNumber ?? string.Empty, string.Empty, "neutral",
            // The frontend has four statuses; DueSoon collapses to Due and Upcoming to Scheduled.
            row.Bucket switch
            {
                VaccinationDueBucket.Overdue => VaccinationStatus.Overdue,
                VaccinationDueBucket.DueSoon => VaccinationStatus.Due,
                VaccinationDueBucket.Done => VaccinationStatus.Done,
                _ => VaccinationStatus.Scheduled,
            }, sms);

    public static VaccineCatalogEntry ToContract(this VaccineCatalogReadModel row) =>
        new(row.Code, row.Name, row.Code, string.Empty, "neutral", row.Protects ?? string.Empty,
            row.BoosterDays / 30, row.Stock, row.MinStock);

    public static SmsTemplate ToContract(this SmsTemplateReadModel row) =>
        new(row.Id, row.Name, row.Channel, row.Body);

    public static VaccinationHistoryEntry ToContract(this VaccinationHistoryRow row)
    {
        var kind = VaccinationHistoryTitles.KindOf(row.Title);
        var date = new PersianCalendar();
        var utc = row.OccurredUtc.UtcDateTime;
        var jalali = $"{date.GetYear(utc):0000}/{date.GetMonth(utc):00}/{date.GetDayOfMonth(utc):00}";
        var detail = row.Detail ?? string.Empty;
        // A reminder's detail is the message text; every other entry's detail names the vaccine / medication.
        var (vaccine, note) = kind == VaccinationHistoryKind.SmsSent ? (string.Empty, detail) : (detail, string.Empty);
        return new VaccinationHistoryEntry(row.Id, kind, KindLabel(kind), row.RecordId ?? string.Empty,
            row.PetName, row.Species, row.OwnerName, vaccine, string.Empty, jalali,
            utc.ToString("HH:mm", CultureInfo.InvariantCulture), note,
            new ActorRef(row.ActorId, row.ActorId, string.Empty, string.Empty));
    }

    private static string KindLabel(VaccinationHistoryKind kind) => kind switch
    {
        VaccinationHistoryKind.Administered => "تزریق شد",
        VaccinationHistoryKind.SmsSent => "پیامک",
        VaccinationHistoryKind.Rescheduled => "باززمان‌بندی",
        VaccinationHistoryKind.Updated => "ویرایش برنامه",
        VaccinationHistoryKind.Cancelled => "لغو برنامه",
        _ => "زمان‌بندی",
    };
}
