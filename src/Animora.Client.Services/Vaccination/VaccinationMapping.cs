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
        var kind = row.Title switch
        {
            "Dose administered" => VaccinationHistoryKind.Administered,
            "Reminder produced" => VaccinationHistoryKind.SmsSent,
            _ => VaccinationHistoryKind.Scheduled,
        };
        var date = new PersianCalendar();
        var utc = row.OccurredUtc.UtcDateTime;
        var jalali = $"{date.GetYear(utc):0000}/{date.GetMonth(utc):00}/{date.GetDayOfMonth(utc):00}";
        return new VaccinationHistoryEntry(row.Id, kind, row.Title, row.RecordId ?? string.Empty,
            row.PetName, row.Species, row.OwnerName, row.Title, string.Empty, jalali,
            utc.ToString("HH:mm", CultureInfo.InvariantCulture), row.Detail ?? string.Empty,
            new ActorRef(row.ActorId, row.ActorId, string.Empty, string.Empty));
    }
}
