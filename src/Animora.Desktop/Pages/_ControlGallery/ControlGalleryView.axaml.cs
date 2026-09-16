using Animora.Desktop.Controls;
using Animora.Desktop.Services;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Animora.Desktop.Pages._ControlGallery;

/// <summary>
/// Dev-only smoke test for the Phase 03 control library. Temporarily hosted
/// by MainWindow; Phase 05 replaces MainWindow's content with the real
/// app shell (that phase decides whether this view is deleted or kept
/// behind a debug-only flag).
/// </summary>
public partial class ControlGalleryView : UserControl
{
    private readonly IToastService _toastService;
    private readonly DialogService _dialogService;

    public ControlGalleryView(IToastService toastService, DialogService dialogService)
    {
        _toastService = toastService;
        _dialogService = dialogService;

        InitializeComponent();

        DataContext = this;

        Toast.Attach((ToastService)toastService);
        _dialogService.Attach(Modal, Drawer);
    }

    /// <summary>Phase 04 proof: a raw ASCII number bound through PersianDigitsConverter.</summary>
    public int SampleCount { get; } = 1234567;

    /// <summary>Phase 04 proof: today's Gregorian date, bound through JalaliDateConverter.</summary>
    public System.DateTime Today { get; } = System.DateTime.Now;

    private void OnShowToastClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _toastService.Show("تغییرات با موفقیت ذخیره شد.", Tone.Ok);

    private void OnShowModalClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _dialogService.ShowModal(BuildOverlayContent("این یک مودال نمونه است."));

    private void OnShowDrawerClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _dialogService.ShowDrawer(BuildOverlayContent("این یک کشوی نمونه است."));

    private Control BuildOverlayContent(string message)
    {
        var closeButton = new Button { Classes = { "ghost" }, Content = "بستن" };
        closeButton.Click += (_, _) => _dialogService.Close();

        return new StackPanel
        {
            Margin = new Avalonia.Thickness(22),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children = { closeButton },
                },
            },
        };
    }
}
