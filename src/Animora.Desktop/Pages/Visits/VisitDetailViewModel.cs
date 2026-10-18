using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Animora.Contracts.Visits;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Pages.History;
using Animora.Desktop.Services;
using Animora.Domain.Clinical;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Visits;

/// <summary>
/// One read-only radiology-study row of the visit viewer: the imaged body part, the written result,
/// the attachment count and when it was taken.
/// </summary>
public sealed record VisitRadiologyRowViewModel(
    string BodyPart, string Result, string DateText, string AttachmentText, bool HasAttachments);

/// <summary>
/// One read-only lab-sheet row of the visit viewer: the free-text description, the joined measured
/// values and when the sample was taken.
/// </summary>
public sealed record VisitLabRowViewModel(
    string Description, IReadOnlyList<string> Items, string DateText);

/// <summary>
/// The read-only viewer of one completed visit, opened from the pet file, the owner sheet or the
/// unified history feed. Shows exactly what the encounter wizard captured — vitals, the four
/// clinical note sections (history / exam / diagnosis / plan), billable lines and the paraclinic
/// records — with Persian digits and Jalali times. Nothing here edits the visit; every section
/// collapses away when the visit carries no data for it.
/// </summary>
public sealed partial class VisitDetailViewModel : ObservableObject
{
    private readonly IVisitEncounterService _encounters;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;

    public VisitDetailViewModel(IVisitEncounterService encounters, IToastService toast, IDialogService dialogs)
    {
        _encounters = encounters;
        _toast = toast;
        _dialogs = dialogs;
    }

    /// <summary>The visit id this sheet shows; empty until <see cref="LoadAsync"/> runs.</summary>
    [ObservableProperty]
    private string _visitId = string.Empty;

    [ObservableProperty]
    private string _title = "ویزیت";

    // Patient & owner.
    [ObservableProperty]
    private string _patientName = string.Empty;

    [ObservableProperty]
    private string _patientKind = string.Empty;

    [ObservableProperty]
    private string _ownerName = string.Empty;

    [ObservableProperty]
    private string _ownerPhone = string.Empty;

    [ObservableProperty]
    private bool _hasOwner;

    // Doctor, state and times.
    [ObservableProperty]
    private string _doctorText = string.Empty;

    [ObservableProperty]
    private bool _hasDoctor;

    [ObservableProperty]
    private string _stateLabel = string.Empty;

    [ObservableProperty]
    private Tone _stateTone = Tone.Neutral;

    [ObservableProperty]
    private string _reasonLabel = string.Empty;

    [ObservableProperty]
    private string _startedText = string.Empty;

    [ObservableProperty]
    private string _completedText = string.Empty;

    [ObservableProperty]
    private bool _hasCompleted;

    /// <summary>The visit's invoice id when one was produced — the chip links the two records together.</summary>
    [ObservableProperty]
    private string _invoiceText = string.Empty;

    [ObservableProperty]
    private bool _hasInvoice;

    // Vitals.
    [ObservableProperty]
    private string _weightText = string.Empty;

    [ObservableProperty]
    private string _temperatureText = string.Empty;

    [ObservableProperty]
    private string _pulseText = string.Empty;

    [ObservableProperty]
    private string _respirationText = string.Empty;

    [ObservableProperty]
    private bool _hasVitals;

    // The four note sections.
    [ObservableProperty]
    private string _chiefComplaint = string.Empty;

    [ObservableProperty]
    private bool _hasChiefComplaint;

    [ObservableProperty]
    private string _history = string.Empty;

    [ObservableProperty]
    private bool _hasHistory;

    [ObservableProperty]
    private string _exam = string.Empty;

    [ObservableProperty]
    private bool _hasExam;

    [ObservableProperty]
    private string _diagnosis = string.Empty;

    [ObservableProperty]
    private bool _hasDiagnosis;

    [ObservableProperty]
    private string _plan = string.Empty;

    [ObservableProperty]
    private bool _hasPlan;

    // Billable lines.
    public ObservableCollection<VisitLineRowViewModel> Lines { get; } = [];

    [ObservableProperty]
    private bool _hasLines;

    [ObservableProperty]
    private string _subtotalText = string.Empty;

    // Paraclinic.
    public ObservableCollection<VisitRadiologyRowViewModel> Radiology { get; } = [];

    public ObservableCollection<VisitLabRowViewModel> Labs { get; } = [];

    [ObservableProperty]
    private bool _hasRadiology;

    [ObservableProperty]
    private bool _hasLabs;

    /// <summary>Loads the visit snapshot and fills every section; a missing visit is reported and leaves the sheet empty.</summary>
    public async Task LoadAsync(string visitId)
    {
        _ = ModalLoadGuard.RunAsync(_toast, _dialogs, "بارگذاری ویزیت ناموفق بود؛ دوباره تلاش کنید", async () =>
        {
            VisitId = visitId;
            var detail = await _encounters.GetDetailAsync(visitId);
            if (detail is null)
            {
                _toast.Show("ویزیت پیدا نشد؛ ممکن است حذف شده باشد.", Tone.Danger);
                return;
            }

            Fill(detail);
        });
    }

    /// <summary>Closes the viewer sheet.</summary>
    [RelayCommand]
    private void Close() => _dialogs.Close();

    private void Fill(VisitDetail d)
    {
        Title = "ویزیت " + PatientKindLabel(d.PatientKind);

        PatientName = string.IsNullOrWhiteSpace(d.PatientName) ? "—" : d.PatientName;
        PatientKind = d.PatientKind;
        OwnerName = string.IsNullOrWhiteSpace(d.OwnerName) ? "مشتری گذری" : d.OwnerName;
        OwnerPhone = string.IsNullOrWhiteSpace(d.OwnerPhone) ? "—" : d.OwnerPhone;
        HasOwner = !string.IsNullOrWhiteSpace(d.OwnerName);

        DoctorText = string.IsNullOrWhiteSpace(d.DoctorName) ? string.Empty : d.DoctorName;
        HasDoctor = !string.IsNullOrWhiteSpace(d.DoctorName);
        StateLabel = VisitStateLabel(d.State);
        StateTone = VisitStateTone(d.State);
        ReasonLabel = VisitReasonLabel(d.Reason);
        StartedText = FaText(Stamp(d.StartedAt));
        CompletedText = FaText(Stamp(d.CompletedAt ?? DateTime.MinValue));
        HasCompleted = d.CompletedAt.HasValue;
        HasInvoice = !string.IsNullOrWhiteSpace(d.InvoiceId);
        InvoiceText = d.InvoiceId is { Length: > 0 } id ? PersianNumberFormatter.ToPersianDigits(id) : string.Empty;

        // Vitals: a reading that was never recorded stays «—» rather than ۰ (a stored zero is still a reading).
        WeightText = FaNum(d.WeightKg);
        TemperatureText = FaNum(d.TemperatureC, " °C");
        PulseText = FaNum(d.PulseBpm, " ضربه");
        RespirationText = FaNum(d.RespirationRate, " نفس");
        HasVitals = d.WeightKg is not null || d.TemperatureC is not null || d.PulseBpm is not null
            || d.RespirationRate is not null;

        ChiefComplaint = d.ChiefComplaint ?? string.Empty;
        HasChiefComplaint = !string.IsNullOrWhiteSpace(ChiefComplaint);
        History = d.History ?? string.Empty;
        HasHistory = !string.IsNullOrWhiteSpace(History);
        Exam = d.Exam ?? string.Empty;
        HasExam = !string.IsNullOrWhiteSpace(Exam);
        Diagnosis = d.Diagnosis ?? string.Empty;
        HasDiagnosis = !string.IsNullOrWhiteSpace(Diagnosis);
        Plan = d.Plan ?? string.Empty;
        HasPlan = !string.IsNullOrWhiteSpace(Plan);

        Lines.Clear();
        foreach (var line in d.Lines)
        {
            Lines.Add(new VisitLineRowViewModel(line.Title,
                FaNum(line.Quantity), FaMoney(line.UnitPrice), FaMoney(line.LineTotal)));
        }

        HasLines = Lines.Count > 0;
        SubtotalText = FaMoney(d.Subtotal);

        Radiology.Clear();
        foreach (var r in d.Radiology)
        {
            Radiology.Add(new VisitRadiologyRowViewModel(r.BodyPart, r.Result ?? string.Empty,
                FaText(Stamp(r.TakenAt)),
                r.AttachmentCount > 0
                    ? FaNum(r.AttachmentCount) + " پیوست"
                    : "بدون پیوست", r.AttachmentCount > 0));
        }

        HasRadiology = Radiology.Count > 0;

        Labs.Clear();
        foreach (var l in d.Labs)
        {
            Labs.Add(new VisitLabRowViewModel(l.Description ?? string.Empty,
                l.Items.Count == 0
                    ? ["مقداری ثبت نشده"]
                    : l.Items.Select(i => i.Unit is { Length: > 0 } u
                        ? $"{i.Name}: {i.Value} {u}"
                        : $"{i.Name}: {i.Value}").ToArray(),
                FaText(Stamp(l.TakenAt))));
        }

        HasLabs = Labs.Count > 0;
    }

    private static string Stamp(DateTime local) =>
        local.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);

    private static string FaText(string isoStamp) =>
        HistoryPresentation.FaText(isoStamp);

    private static string FaMoney(long minor) =>
        Animora.Desktop.Pages.Visits.VisitsPresentation.Money(minor);

    private static string FaNum(double? value, string suffix = "") =>
        value is null
            ? "—"
            : PersianNumberFormatter.ToPersianDigits(value.Value.ToString("0.##", CultureInfo.InvariantCulture)) + suffix;

    private static string FaNum(int? value, string suffix) =>
        value is null
            ? "—"
            : PersianNumberFormatter.ToPersianDigits(value.Value.ToString(CultureInfo.InvariantCulture)) + suffix;

    private static string FaNum(decimal value) =>
        PersianNumberFormatter.ToPersianDigits(value.ToString("0.##", CultureInfo.InvariantCulture));

    private static string PatientKindLabel(string kind) => kind switch
    {
        nameof(Animora.Domain.Patients.PetSpecies.Dog) => "سگ",
        nameof(Animora.Domain.Patients.PetSpecies.Cat) => "گربه",
        nameof(Animora.Domain.Patients.PetSpecies.Bird) => "پرنده",
        nameof(Animora.Domain.Patients.PetSpecies.Rabbit) => "خرگوش",
        _ => string.Empty,
    };

    private static string VisitReasonLabel(string reason) => reason switch
    {
        nameof(VisitReason.Checkup) => "معاینهٔ دوره‌ای",
        nameof(VisitReason.Illness) => "بیماری",
        nameof(VisitReason.Injury) => "آسیب‌دیدگی",
        nameof(VisitReason.Vaccination) => "واکسیناسیون",
        nameof(VisitReason.Surgery) => "جراحی",
        nameof(VisitReason.Dental) => "دندان‌پزشکی",
        _ => string.Empty,
    };

    private static string VisitStateLabel(string state) => state switch
    {
        "Completed" => "تکمیل‌شده",
        "InProgress" => "در حال معاینه",
        "Cancelled" => "لغو‌شده",
        _ => "پیش‌نویس",
    };

    private static Tone VisitStateTone(string state) => state switch
    {
        "Completed" => Tone.Ok,
        "InProgress" => Tone.Warn,
        "Cancelled" => Tone.Danger,
        _ => Tone.Neutral,
    };
}

/// <summary>One billable line row: title, quantity, unit price and line total, all pre-formatted.</summary>
public sealed record VisitLineRowViewModel(string Title, string Quantity, string UnitPrice, string LineTotal);
