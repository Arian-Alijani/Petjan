using Animora.Client.Services.Identity;
using Animora.Contracts.Identity;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Animora.Desktop.Services;

/// <summary>Presentation seam for ending the session and returning to a fresh login workspace.</summary>
public interface IAppSessionController
{
    Task RequestSignOutAsync();
}

public sealed class AppSessionController(IAuthenticationService authentication, IDialogService dialogs,
    IOnlineAuthClient? onlineAuth = null) : IAppSessionController
{
    public Func<Task>? ReturnToLoginAsync { get; set; }

    public Task RequestSignOutAsync()
    {
        var confirm = new Button { Content = "خروج از حساب کاربری", Classes = { "danger" } };
        var cancel = new Button { Content = "انصراف" };
        var panel = new StackPanel { Spacing = 16 };
        panel.Children.Add(new TextBlock { Text = "آیا از حساب کاربری خارج می‌شوید؟" });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        actions.Children.Add(confirm);
        actions.Children.Add(cancel);
        panel.Children.Add(actions);
        cancel.Click += (_, _) => dialogs.CloseModal();
        confirm.Click += async (_, _) =>
        {
            confirm.IsEnabled = cancel.IsEnabled = false;
            // Local logout is mandatory even when the server is unavailable. Rebuilding the provider
            // also drops every page, draft, capability and query cache, not just the auth cache.
            try
            {
                await authentication.SignOutAsync("USER_LOGOUT");
                if (onlineAuth is not null) await onlineAuth.LogoutAsync();
            }
            catch (Exception ex)
            {
                Diagnostics.AppTrace.Error("logout cleanup failed; returning to login fail-closed", ex, "auth");
            }
            finally
            {
                dialogs.Close();
                if (ReturnToLoginAsync is { } returnToLogin) await returnToLogin();
            }
        };
        dialogs.ShowModal(panel);
        return Task.CompletedTask;
    }
}
