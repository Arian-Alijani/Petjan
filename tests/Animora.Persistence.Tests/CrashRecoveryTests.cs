using Animora.Contracts.Diagnostics;
using Animora.Infrastructure.Local.Startup;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Persistence.Tests;

public sealed class CrashRecoveryTests
{
    [Fact]
    public async Task MarkerTriggersQuickCheckRecoveryAndIsCleared()
    {
        var notifications = new RecordingNotifications();
        using var fixture = new LocalStoreTestFixture(configureServices: services =>
        {
            services.AddSingleton(notifications);
            services.AddSingleton<IUserNotificationPort>(notifications);
            services.AddSingleton<GlobalExceptionHandler>();
            services.AddSingleton<CrashRecoveryService>();
        });
        var diagnostics = await fixture.InitializeAsync();
        fixture.Services.GetRequiredService<GlobalExceptionHandler>().Diagnostics = diagnostics;
        var marker = Path.Combine(fixture.Directory, GlobalExceptionHandler.MarkerFileName);
        await File.WriteAllTextAsync(marker, "test");

        var result = await fixture.Services.GetRequiredService<CrashRecoveryService>().RecoverIfRequiredAsync();

        Assert.True(result.IsSuccess, result.Detail);
        Assert.True(result.Value!.RecoveredFromCrash);
        Assert.False(File.Exists(marker));
        Assert.Equal(1, notifications.Count);
    }

    private sealed class RecordingNotifications : IUserNotificationPort
    {
        public int Count { get; private set; }
        public void Notify(NotificationSeverity severity, string code, string? entityRef, NotificationAction? action) => Count++;
    }
}
