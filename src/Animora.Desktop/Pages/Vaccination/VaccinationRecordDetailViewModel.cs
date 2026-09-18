using System.Collections.ObjectModel;
using Animora.Contracts.Vaccination;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Vaccination;

/// <summary>
/// The centered "record detail" modal, mirroring the reference's
/// <c>openVaxDrawer</c> (Animora-UI/assets/vaccination.js): the patient header,
/// the status/SMS pills, the «مشخصات نوبت» key/value grid, the «پیشینه
/// واکسیناسیون» timeline, and the send-SMS / mark-done actions.
/// </summary>
public sealed partial class VaccinationRecordDetailViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;

    public VaccinationRecordDetailViewModel(
        VaccinationRecord record,
        VaccineCatalogEntry? vaccine,
        IDialogService dialogs)
    {
        _dialogs = dialogs;
        Record = record;

        PetInitial = VaccinationPresentation.Initial(record.PetName);
        AvatarBrush = VaccinationPresentation.AvatarFill(record.Tone);
        PetName = record.PetName;
        HeaderSub = $"{VaccinationPresentation.SpeciesLabel(record.Species)} · مالک: {record.OwnerName}";

        var status = VaccinationPresentation.Status(record.Status);
        StatusText = status.Label;
        StatusTone = status.Tone;
        var sms = VaccinationPresentation.Sms(record.SmsState);
        SmsText = sms.Label;
        SmsIconKey = sms.IconKey;

        VaccineName = vaccine?.Name ?? "—";
        VaccineShort = vaccine?.Short ?? string.Empty;
        HasVaccineShort = !string.IsNullOrEmpty(VaccineShort);
        Dose = record.Dose;
        DueDateText = VaccinationPresentation.LongDate(record.DueDate);
        LastDateText = VaccinationPresentation.LongDateOrDash(record.LastDate);
        Site = record.Site;
        Lot = VaccinationPresentation.FaString(record.Lot);
        VetName = record.VetName;
        Mobile = VaccinationPresentation.FaString(record.OwnerMobile);

        // Reference's synthetic short history: the current due entry, the
        // previous dose (when known) and the initial-case note.
        Timeline.Add(new VaccinationTimelineItemViewModel(
            $"{VaccineName} — {record.Dose}",
            $"{(record.Status == VaccinationStatus.Done ? "انجام‌شده" : "سررسید")}: {DueDateText} · {record.VetName}",
            IsActive: true));

        if (record.LastDate is { } last)
        {
            Timeline.Add(new VaccinationTimelineItemViewModel(
                $"{VaccineName} — نوبت قبلی",
                $"{VaccinationPresentation.LongDate(last)} · {record.VetName}",
                IsActive: false));
        }

        Timeline.Add(new VaccinationTimelineItemViewModel(
            "ثبت پرونده و معاینه اولیه",
            "کلینیک وت‌پلاس",
            IsActive: false));
    }

    public VaccinationRecord Record { get; }

    public string PetInitial { get; }
    public Avalonia.Media.IBrush AvatarBrush { get; }
    public string PetName { get; }
    public string HeaderSub { get; }

    public string StatusText { get; }
    public Controls.Tone StatusTone { get; }
    public string SmsText { get; }
    public string SmsIconKey { get; }

    public string VaccineName { get; }
    public string VaccineShort { get; }
    public bool HasVaccineShort { get; }
    public string Dose { get; }
    public string DueDateText { get; }
    public string LastDateText { get; }
    public string Site { get; }
    public string Lot { get; }
    public string VetName { get; }
    public string Mobile { get; }

    public ObservableCollection<VaccinationTimelineItemViewModel> Timeline { get; } = [];

    [RelayCommand]
    private void Close() => _dialogs.Close();
}

/// <summary>
/// One node of the record's «پیشینه واکسیناسیون» timeline, mirroring the
/// reference's <c>vx-tl-item</c> (title, meta line and the active/muted dot).
/// </summary>
public sealed class VaccinationTimelineItemViewModel
{
    public VaccinationTimelineItemViewModel(string title, string meta, bool IsActive)
    {
        Title = title;
        Meta = meta;
        this.IsActive = IsActive;
    }

    public string Title { get; }
    public string Meta { get; }
    public bool IsActive { get; }
}
