using System.Data;
using System.Data.Common;
using Animora.Persistence.PostgreSql.Tenancy;
using Xunit;

namespace Animora.Server.Tests.Persistence;

public sealed class SessionInitializerContractTests
{
    /// <summary>A connection that records every touch; any interaction is a contract violation for the Noop.</summary>
    private sealed class SpyConnection : DbConnection
    {
        public int Touches { get; private set; }

        private void Touch() => Touches++;

#pragma warning disable CS8765 // nullability of overridden members
        public override string ConnectionString { get => string.Empty; set => Touch(); }
#pragma warning restore CS8765

        public override string Database { get { Touch(); return string.Empty; } }

        public override string DataSource { get { Touch(); return string.Empty; } }

        public override string ServerVersion { get { Touch(); return string.Empty; } }

        public override ConnectionState State { get { Touch(); return ConnectionState.Closed; } }

        public override void ChangeDatabase(string databaseName) => Touch();

        public override void Close() => Touch();

        public override void Open() => Touch();

        public override Task OpenAsync(CancellationToken cancellationToken)
        {
            Touch();
            return Task.CompletedTask;
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            Touch();
            throw new InvalidOperationException("must not begin a transaction");
        }

        protected override DbCommand CreateDbCommand()
        {
            Touch();
            throw new InvalidOperationException("must not create a command");
        }
    }

    [Fact]
    public async Task Noop_DoesNotTouchConnection()
    {
        using var spy = new SpyConnection();
        ITenantSessionInitializer initializer = new NoopTenantSessionInitializer();

        await initializer.InitializeAsync(spy, "TENANT-A", CancellationToken.None);

        Assert.Equal(0, spy.Touches);
    }

    [Fact]
    public void Noop_CompletesSynchronously()
    {
        using var spy = new SpyConnection();

        ValueTask task = new NoopTenantSessionInitializer().InitializeAsync(spy, string.Empty);

        Assert.True(task.IsCompletedSuccessfully);
    }
}
