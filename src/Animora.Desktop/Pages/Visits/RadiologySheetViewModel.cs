using System.Collections.ObjectModel;
using Animora.Contracts.Visits;
using Animora.Desktop.Localization;
using Animora.Desktop.Pages.Patients;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Visits;

/// <summary>
/// The «ثبت رادیولوژی» sheet opened over the encounter: Jalali date, imaged organ / body part, written result and the
/// attached image / report files. Attachments are stored (content-addressed) the moment they are picked; the entry is
/// handed to the encounter through <c>onSave</c> only when the form validates. A backend failure stays inside the
/// sheet as an inline error so the doctor's input is never lost.
/// </summary>
public sealed partial class RadiologySheetViewModel : ObservableObject
{
    /// <summary>Mirrors the backend's per-entry attachment cap (<c>ClinicalRecordLimits.MaxAttachments</c>).</summary>
    public const int MaxAttachments = 20;

    private const int MaxBodyPart = 100;
    private const int MaxText = 4000;

    private readonly IVisitEncounterService _service;
    private readonly IDialogService _dialogs;
    private readonly Func<RadiologyEntry, Task> _onSave;

    public RadiologySheetViewModel(
        IVisitEncounterService service,
        IDialogService dialogs,
        Func<RadiologyEntry, Task> onSave)
    {
        _service = service;
        _dialogs = dialogs;
        _onSave = onSave;
        _dateValue = JalaliCalendar.Today;
    }

    public ObservableCollection<RadiologyFileViewModel> Files { get; } = [];

    /// <summary>The study date as an ASCII Jalali <c>yyyy/MM/dd</c> string (two-way with the date picker).</summary>
    [ObservableProperty]
    private string _dateValue;

    [ObservableProperty]
    private string _bodyPart = string.Empty;

    [ObservableProperty]
    private string _result = string.Empty;

    [ObservableProperty]
    private string _error = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    public bool IsIdle => !IsBusy;

    public bool HasFiles => Files.Count > 0;

    public string FilesCaption => Files.Count == 0
        ? "فایلی پیوست نشده"
        : $"{PersianNumberFormatter.ToPersianDigits(Files.Count)} فایل پیوست شد";

    private void NotifyFiles()
    {
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(FilesCaption));
    }

    [RelayCommand]
    private async Task AttachFilesAsync()
    {
        if (IsBusy)
        {
            return;
        }

        Error = string.Empty;
        IReadOnlyList<PickedFile> picked;
        try
        {
            picked = await _dialogs.PickFilesAsync("انتخاب تصویر یا گزارش رادیولوژی", ParaclinicPresentation.AttachmentPatterns);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Error = "باز کردن پنجرهٔ انتخاب فایل ناموفق بود؛ دوباره تلاش کنید.";
            return;
        }

        if (picked.Count == 0)
        {
            return;
        }

        IsBusy = true;
        try
        {
            foreach (var file in picked)
            {
                if (Files.Count >= MaxAttachments)
                {
                    Error = $"حداکثر {PersianNumberFormatter.ToPersianDigits(MaxAttachments)} فایل برای هر رادیولوژی مجاز است.";
                    break;
                }

                if (file.Size is { } size && size > ParaclinicPresentation.MaxAttachmentBytes)
                {
                    Error = $"«{file.Name}» بزرگ‌تر از {PersianNumberFormatter.ToPersianDigits(ParaclinicPresentation.MaxAttachmentBytes / (1024 * 1024))} مگابایت است.";
                    continue;
                }

                await using var content = await file.OpenReadAsync();
                var stored = await _service.StoreAttachmentAsync(
                    content, file.Name, ParaclinicPresentation.ContentTypeFor(file.Name));

                // The store is content-addressed: attaching the same bytes twice is a no-op.
                if (Files.All(f => f.Hash != stored.Hash))
                {
                    Files.Add(new RadiologyFileViewModel(stored, RemoveFile));
                }
            }
        }
        catch (InvalidOperationException ex)
        {
            Error = PatientsPresentation.CommandFailureMessage(ex, "ذخیرهٔ فایل پیوست ناموفق بود؛ دوباره تلاش کنید.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = "خواندن فایل انتخاب‌شده ممکن نشد؛ دسترسی یا مسیر فایل را بررسی کنید.";
        }
        finally
        {
            IsBusy = false;
            NotifyFiles();
        }
    }

    private void RemoveFile(RadiologyFileViewModel file)
    {
        Files.Remove(file);
        NotifyFiles();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        Error = string.Empty;
        var body = (BodyPart ?? string.Empty).Trim();
        var result = (Result ?? string.Empty).Trim();
        if (!ParaclinicPresentation.TryParseJalali(DateValue, out var takenAt))
        {
            Error = "تاریخ رادیولوژی را انتخاب کنید.";
        }
        else if (body.Length == 0)
        {
            Error = "عضو یا ناحیهٔ تصویربرداری را وارد کنید.";
        }
        else if (body.Length > MaxBodyPart)
        {
            Error = $"نام عضو نباید از {PersianNumberFormatter.ToPersianDigits(MaxBodyPart)} نویسه بیشتر باشد.";
        }
        else if (result.Length > MaxText)
        {
            Error = $"متن نتیجه نباید از {PersianNumberFormatter.ToPersianDigits(MaxText)} نویسه بیشتر باشد.";
        }

        if (Error.Length > 0)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _onSave(new RadiologyEntry(
                Id: string.Empty,
                TakenAt: takenAt,
                BodyPart: body,
                Result: result.Length == 0 ? null : result,
                Files: Files.Select(f => f.File).ToArray()));
            _dialogs.Close();
        }
        catch (InvalidOperationException ex)
        {
            Error = PatientsPresentation.CommandFailureMessage(ex, "ثبت رادیولوژی ناموفق بود؛ دوباره تلاش کنید.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _dialogs.Close();
}
