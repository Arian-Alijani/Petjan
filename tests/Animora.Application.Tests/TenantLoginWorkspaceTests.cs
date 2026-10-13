using Animora.Client.Services.Identity;
using Animora.Contracts.Identity;
using Animora.Desktop.Services;
using Animora.Domain.Results;
using Xunit;

namespace Animora.Application.Tests;

public sealed class TenantLoginWorkspaceTests
{
    [Fact]
    public async Task TenantIsolation_RetryDisposesPriorWorkspace_CommitTransfersOnlySuccessfulCandidate()
    {
        var created = new List<WorkspaceProvider>();
        IServiceProvider? activated = null;
        await using var workspace = new TenantLoginWorkspace(code =>
        {
            var provider = new WorkspaceProvider(code);
            created.Add(provider);
            return provider;
        }, (_, _) => Task.FromResult(true), provider => { activated = provider; return Task.CompletedTask; });
        var first = await workspace.PrepareAsync("A", default);
        var second = await workspace.PrepareAsync("B", default);
        Assert.True(created[0].Disposed);
        Assert.NotSame(first.Authentication, second.Authentication);
        Assert.Null(activated);
        await workspace.CommitAsync();
        await workspace.ResetAsync();
        Assert.Same(created[1], activated);
        Assert.False(created[1].Disposed); // the shell, not the login workspace, owns this provider now
        await created[1].DisposeAsync();
    }

    [Fact]
    public async Task TenantIsolation_InitializationFailureNeverActivatesAndDisposesCandidate()
    {
        var provider = new WorkspaceProvider("A");
        var activated = false;
        await using var workspace = new TenantLoginWorkspace(_ => provider, (_, _) => Task.FromResult(false),
            _ => { activated = true; return Task.CompletedTask; });
        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.PrepareAsync("A", default));
        Assert.True(provider.Disposed);
        Assert.False(activated);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.CommitAsync());
    }

    [Fact]
    public async Task TenantIsolation_CancelledAttemptDisposesCandidate()
    {
        var provider = new WorkspaceProvider("A");
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await using var workspace = new TenantLoginWorkspace(_ => provider, (_, ct) => Task.FromCanceled<bool>(ct),
            _ => throw new InvalidOperationException("Cancelled workspace cannot activate."));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workspace.PrepareAsync("A", stop.Token));
        Assert.True(provider.Disposed);
    }

    private sealed class WorkspaceProvider(string clinic) : IServiceProvider, IAsyncDisposable
    {
        private readonly StubAuth _auth = new();
        public string Clinic { get; } = clinic;
        public bool Disposed { get; private set; }
        public object? GetService(Type type) => type == typeof(IAuthenticationService) || type == typeof(IOnlineAuthClient) ? _auth : null;
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private sealed class StubAuth : IAuthenticationService, IOnlineAuthClient
    {
        public Task<AuthOutcome> SignInAsync(string username, string password, bool rememberDevice, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SignOutAsync(string reason, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> IsFirstRunAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AuthOutcome> CompleteFirstRunAsync(FirstRunRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AuthOutcome> EnsureLocalMirrorAsync(ServerIdentityMirrorRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<OnlineLoginOutcome>> LoginAsync(string tenantCode, string userName, string password, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result> RefreshAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result> LogoutAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }
}
