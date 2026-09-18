using System.Collections.ObjectModel;
using Animora.Contracts.Vaccination;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Vaccination;

/// <summary>
/// The SMS-reminder compose modal, mirroring the reference's
/// <c>openSmsCompose</c>/<c>vaxSendSms</c> (Animora-UI/assets/vaccination.js):
/// the recipient lock-up, the template chips, the live preview and the
/// character/segment counter. The Send action persists the sent state through
/// <see cref="IVaccinationService"/> (network transmission is stubbed in the
/// mock) and shows the reference's confirmation toast.
/// </summary>
public sealed partial class SmsComposeViewModel : ObservableObject
{
    private readonly IVaccinationService _service;
    private readonly VaccinationRecord _record;
    private readonly IReadOnlyList<SmsTemplate> _templates;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toast;
    private readonly Action _onSent;

    public SmsComposeViewModel(
        IVaccinationService service,
        VaccinationRecord record,
        IReadOnlyList<SmsTemplate> templates,
        IDialogService dialogs,
        IToastService toast,
        Action onSent)
    {
        _service = service;
        _record = record;
        _templates = templates;
        _dialogs = dialogs;
        _toast = toast;
        _onSent = onSent;

        PetInitial = VaccinationPresentation.Initial(record.PetName);
        AvatarBrush = VaccinationPresentation.AvatarFill(record.Tone);
        OwnerName = record.OwnerName;
        PetName = record.PetName;
        OwnerMobile = VaccinationPresentation.FaString(record.OwnerMobile);

        _selectedTemplate = templates.Count > 0 ? templates[0] : null;
        foreach (var template in templates)
        {
            Chips.Add(new SmsTemplateChipViewModel(template)
            {
                IsSelected = template.Id == _selectedTemplate?.Id,
            });
        }

        _ = PaintAsync();
    }

    private SmsTemplate? _selectedTemplate;

    public string PetInitial { get; }
    public Avalonia.Media.IBrush AvatarBrush { get; }
    public string OwnerName { get; }
    public string PetName { get; }
    public string OwnerMobile { get; }

    public ObservableCollection<SmsTemplateChipViewModel> Chips { get; } = [];

    [ObservableProperty]
    private string _previewBody = string.Empty;

    [ObservableProperty]
    private string _characterCount = string.Empty;

    [ObservableProperty]
    private string _segmentCount = string.Empty;

    private async Task PaintAsync()
    {
        if (_selectedTemplate is null)
        {
            return;
        }

        var preview = await _service.BuildSmsPreviewAsync(_record.Id, _selectedTemplate.Id);
        PreviewBody = preview.Body;
        CharacterCount = VaccinationPresentation.Fa(preview.CharacterCount);
        SegmentCount = VaccinationPresentation.Fa(preview.SegmentCount);
    }

    [RelayCommand]
    private async Task SelectTemplateAsync(SmsTemplateChipViewModel? chip)
    {
        if (chip is null)
        {
            return;
        }

        _selectedTemplate = chip.Template;
        foreach (var c in Chips)
        {
            c.IsSelected = c.Template.Id == chip.Template.Id;
        }

        await PaintAsync();
    }

    [RelayCommand]
    private void Cancel() => _dialogs.Close();

    [RelayCommand]
    private async Task SendAsync()
    {
        await _service.SendSmsAsync(_record.Id, PreviewBody);
        _dialogs.Close();
        _onSent();
        _toast.Show("پیامک یادآور ارسال شد (نمونه طراحی)");
    }
}
