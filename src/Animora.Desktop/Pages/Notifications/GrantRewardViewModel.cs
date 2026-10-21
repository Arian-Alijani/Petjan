using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Animora.Contracts.Notifications;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Notifications;

/// <summary>
/// «اعطای جایزه» (manager only): pick a user, the number of free subscription days and a reason. The service records
/// the grant and sends the user a reward message; the real subscription extension is server work (not wired yet).
/// </summary>
public sealed partial class GrantRewardViewModel : ObservableObject
{
    private readonly INotificationService _service;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;

    public GrantRewardViewModel(INotificationService service, IToastService toast, IDialogService dialogs)
    {
        _service = service;
        _toast = toast;
        _dialogs = dialogs;
        _ = LoadUsersAsync();
    }

    public ObservableCollection<NotificationRecipient> Users { get; } = [];

    [ObservableProperty] private NotificationRecipient? _selectedUser;
    [ObservableProperty] private string _daysText = "30";
    [ObservableProperty] private string _reason = string.Empty;
    [ObservableProperty] private string _error = string.Empty;
    [ObservableProperty] private bool _isSaving;

    private async Task LoadUsersAsync()
    {
        try
        {
            foreach (var user in await _service.GetRecipientsAsync()) Users.Add(user);
        }
        catch (InvalidOperationException)
        {
            Error = "فهرست کاربران بارگذاری نشد؛ پنجره را ببندید و دوباره باز کنید.";
        }
    }

    [RelayCommand]
    private async Task GrantAsync()
    {
        Error = string.Empty;
        var digits = PersianNumberFormatter.ToAsciiDigits(DaysText).Trim();
        if (SelectedUser is null) Error = "کاربر دریافت‌کنندهٔ جایزه را انتخاب کنید.";
        else if (!int.TryParse(digits, out var days) || days is < 1 or > 3650)
            Error = "تعداد روز باید عددی بین ۱ تا ۳۶۵۰ باشد.";
        if (Error.Length > 0) return;

        var target = SelectedUser!;
        IsSaving = true;
        try
        {
            await _service.GrantRewardAsync(new GrantRewardRequest(target.UserId, int.Parse(digits), Reason.Trim()));
        }
        catch (InvalidOperationException ex)
        {
            Error = NotificationsPresentation.Failure(ex, "ثبت جایزه ناموفق بود؛ دوباره تلاش کنید.");
            IsSaving = false;
            return;
        }

        IsSaving = false;
        _toast.Show($"جایزه برای {target.Name} ثبت و پیام آن ارسال شد", Tone.Ok);
        _dialogs.Close();
    }

    [RelayCommand]
    private void Cancel() => _dialogs.Close();
}
