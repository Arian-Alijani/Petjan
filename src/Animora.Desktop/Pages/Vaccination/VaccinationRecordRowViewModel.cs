using Animora.Contracts.Vaccination;
using Animora.Desktop.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Vaccination;

/// <summary>
/// Adapts a <see cref="VaccinationRecord"/> to one row of the records table,
/// mirroring the reference's <c>renderRows</c>/<c>dueCell</c>: the pet avatar +
/// name + species, the vaccine name + tag + dose, the due-date cell coloured by
/// urgency, the status pill and the SMS-state cell. Row actions (SMS, mark
/// done, details) are invoked via the page ViewModel's commands, so this
/// adapter stays a pure view-model of the record.
/// </summary>
public sealed partial class VaccinationRecordRowViewModel : ObservableObject
{
    public VaccinationRecordRowViewModel(VaccinationRecord record, VaccineCatalogEntry? vaccine)
    {
        Record = record;
        PetName = record.PetName;
        PetInitial = VaccinationPresentation.Initial(record.PetName);
        AvatarBrush = VaccinationPresentation.AvatarFill(record.Tone);
        SpeciesLabel = VaccinationPresentation.SpeciesLabel(record.Species);
        OwnerName = record.OwnerName;

        // Built-in protocol codes are not stock rows any more (T6) — fall back to the record's own vaccine code.
        VaccineName = vaccine?.Name ?? (string.IsNullOrWhiteSpace(record.VaccineId) ? "—" : record.VaccineId);
        VaccineShort = vaccine?.Short ?? string.Empty;
        HasVaccineShort = !string.IsNullOrEmpty(VaccineShort);
        Dose = record.Dose;

        DueDateText = VaccinationPresentation.LongDate(record.DueDate);
        var rel = VaccinationPresentation.RelativeDue(record);
        RelativeText = rel.Text;
        RelativeUrgency = rel.Urgency;

        var status = VaccinationPresentation.Status(record.Status);
        StatusText = status.Label;
        StatusTone = status.Tone;

        var sms = VaccinationPresentation.Sms(record.SmsState);
        SmsText = sms.Label;
        SmsIconKey = sms.IconKey;
        SmsBrush = ResolveSmsBrush(sms.Tone);
    }

    public VaccinationRecord Record { get; }
    public string PetName { get; }
    public string PetInitial { get; }
    public IBrush AvatarBrush { get; }
    public string SpeciesLabel { get; }
    public string OwnerName { get; }

    public string VaccineName { get; }
    public string VaccineShort { get; }
    public bool HasVaccineShort { get; }
    public string Dose { get; }

    public string DueDateText { get; }
    public string RelativeText { get; }

    /// <summary>far | soon | past — the reference's <c>vx-due .rel</c> class.</summary>
    public string RelativeUrgency { get; }

    public bool IsPast => RelativeUrgency == "past";
    public bool IsSoon => RelativeUrgency == "soon";
    public bool IsFar => RelativeUrgency == "far";

    public string StatusText { get; }
    public Tone StatusTone { get; }

    public string SmsText { get; }
    public string SmsIconKey { get; }
    public IBrush SmsBrush { get; }

    // Reference .vx-sms-cell tone colors (ok/info/danger/mute).
    private static IBrush ResolveSmsBrush(Tone tone) => tone switch
    {
        Tone.Ok => VaccinationPresentation.Fill("#027a48"),
        Tone.Info => VaccinationPresentation.Fill("#1570cd"),
        Tone.Danger => VaccinationPresentation.Fill("#d92d20"),
        _ => VaccinationPresentation.Fill("#667085"),
    };
}
