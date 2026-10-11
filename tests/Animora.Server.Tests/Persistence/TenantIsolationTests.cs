using Animora.Domain.Primitives;
using Animora.Persistence.PostgreSql.Conventions;
using Animora.Persistence.PostgreSql.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Animora.Server.Tests.Persistence;

/// <summary>
/// ARCH gate L: cross-tenant isolation proven with hardening (RLS) OFF — the default
/// <see cref="NoopTenantSessionInitializer"/> — using a fixture-local probe entity and a test-only context
/// that applies the production tenancy conventions and interceptors. Never a product table/migration.
/// </summary>
public sealed class TenantIsolationTests(PgFixture pg) : IClassFixture<PgFixture>
{
    private const string TenantA = "TENANT-A";
    private const string TenantB = "TENANT-B";

    /// <summary>Probe aggregate used only by this suite.</summary>
    internal sealed class IsolationProbeEntity : SyncableEntity
    {
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>Test-only context: production conventions (named filters, stamping) over the probe entity.</summary>
    private sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options, ITenantContext tenant) : DbContext(options)
    {
        public string CurrentTenantId => tenant.TenantId ?? throw new TenantContextMissingException();

        public DbSet<IsolationProbeEntity> Probes => Set<IsolationProbeEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<IsolationProbeEntity>().ToTable("IsolationProbes").HasKey(e => e.Id);
            ServerModelConventions.ApplyTenancy(modelBuilder, () => CurrentTenantId);
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (TenantStampingInterceptor, NoUnnamedIgnoreFiltersInterceptor)> Interceptors = new();

    private ProbeDbContext Create(ITenantContext tenant, NpgsqlDataSource? dataSource = null)
    {
        var builder = new DbContextOptionsBuilder<ProbeDbContext>();
        _ = dataSource is null ? builder.UseNpgsql(pg.ConnectionString) : builder.UseNpgsql(dataSource);
        // One interceptor pair per distinct tenant (same id => same behaviour): EF keys its internal service provider on the interceptor
        // instances, so a fresh pair per context would exhaust its 20-provider cache across the whole run.
        var (stamping, ignoreFilters) = Interceptors.GetOrAdd(tenant.TenantId + "|" + tenant.IsOperator, _ => (new TenantStampingInterceptor(tenant), new NoUnnamedIgnoreFiltersInterceptor(tenant)));
        builder.AddInterceptors(stamping, ignoreFilters);
        // The suite shares one process-wide EF provider cache; other fixtures may already have used most of its 20 slots.
        builder.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning));
        return new ProbeDbContext(builder.Options, tenant);
    }

    private async Task EnsureSchemaAsync()
    {
        // EnsureCreated is idempotent on an existing database and allowed in a test fixture only.
        await using ProbeDbContext db = Create(new FixedTenantContext(TenantA));
        await db.Database.EnsureCreatedAsync();
    }

    private async Task<(string IdA, string IdB)> SeedAsync()
    {
        await EnsureSchemaAsync();
        string idA = IdGenerator.NewId();
        string idB = IdGenerator.NewId();

        await using (ProbeDbContext db = Create(new FixedTenantContext(TenantA)))
        {
            db.Probes.Add(new IsolationProbeEntity { Id = idA, Name = "a-visible", UpdatedAt = DateTimeOffset.UtcNow });
            db.Probes.Add(new IsolationProbeEntity { Id = IdGenerator.NewId(), Name = "a-deleted", Deleted = true, UpdatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        await using (ProbeDbContext db = Create(new FixedTenantContext(TenantB)))
        {
            db.Probes.Add(new IsolationProbeEntity { Id = idB, Name = "b-visible", UpdatedAt = DateTimeOffset.UtcNow });
            db.Probes.Add(new IsolationProbeEntity { Id = IdGenerator.NewId(), Name = "b-deleted", Deleted = true, UpdatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        return (idA, idB);
    }

    private static async Task<TException> ThrowsInChainAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        Exception thrown = await Assert.ThrowsAnyAsync<Exception>(action);
        for (Exception? e = thrown; e is not null; e = e.InnerException)
        {
            if (e is TException match)
            {
                return match;
            }
        }

        throw new Xunit.Sdk.XunitException($"Expected {typeof(TException).Name} in exception chain but got: {thrown}");
    }

    [PgFact]
    public async Task A_CannotRead_B_ById()
    {
        (string idA, string idB) = await SeedAsync();

        await using ProbeDbContext db = Create(new FixedTenantContext(TenantA));

        Assert.Null(await db.Probes.AsNoTracking().FirstOrDefaultAsync(e => e.Id == idB));
        Assert.NotNull(await db.Probes.AsNoTracking().FirstOrDefaultAsync(e => e.Id == idA));
    }

    [PgFact]
    public async Task A_CannotRead_B_List()
    {
        await SeedAsync();

        await using ProbeDbContext db = Create(new FixedTenantContext(TenantA));
        List<IsolationProbeEntity> rows = await db.Probes.AsNoTracking().Take(200).ToListAsync();

        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal(TenantA, r.TenantId));
        Assert.DoesNotContain(rows, r => r.Name.StartsWith("b-", StringComparison.Ordinal));
    }

    [PgFact]
    public async Task Write_WithForeignTenant_Throws_TenantMismatch()
    {
        await EnsureSchemaAsync();

        await using ProbeDbContext db = Create(new FixedTenantContext(TenantA));
        db.Probes.Add(new IsolationProbeEntity
        {
            Id = IdGenerator.NewId(),
            TenantId = TenantB,
            Name = "forged",
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        await ThrowsInChainAsync<TenantMismatchException>(() => db.SaveChangesAsync());
    }

    [PgFact]
    public async Task NullContext_Throws_TenantContextMissing()
    {
        await EnsureSchemaAsync();

        await using ProbeDbContext db = Create(new NullTenantContext());

        await ThrowsInChainAsync<TenantContextMissingException>(() => db.Probes.AsNoTracking().Take(1).ToListAsync());
    }

    [PgFact]
    public async Task SoftDeleteLift_StillHidesOtherTenant()
    {
        await SeedAsync();

        await using ProbeDbContext db = Create(new FixedTenantContext(TenantA));
        List<IsolationProbeEntity> rows = await db.Probes
            .IgnoreQueryFilters([ServerModelConventions.SoftDeleteFilterName])
            .AsNoTracking()
            .Take(200)
            .ToListAsync();

        Assert.Contains(rows, r => r.Name == "a-deleted");
        Assert.All(rows, r => Assert.Equal(TenantA, r.TenantId));
        Assert.DoesNotContain(rows, r => r.Name.StartsWith("b-", StringComparison.Ordinal));
    }

    [PgFact]
    public async Task PooledConnection_A_B_A_NoLeak()
    {
        await SeedAsync();

        var csb = new NpgsqlConnectionStringBuilder(pg.ConnectionString) { Pooling = true, MaxPoolSize = 1, MinPoolSize = 0 };
        await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(csb.ConnectionString);

        async Task<(int Pid, string[] Names)> ReadAsync(string tenant)
        {
            await using ProbeDbContext db = Create(new FixedTenantContext(tenant), dataSource);
            int pid = await db.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync();
            string[] names = await db.Probes.AsNoTracking().OrderBy(e => e.Name).Take(200).Select(e => e.Name).ToArrayAsync();
            return (pid, names);
        }

        (int pidA1, string[] namesA1) = await ReadAsync(TenantA);
        (int pidB, string[] namesB) = await ReadAsync(TenantB);
        (int pidA2, string[] namesA2) = await ReadAsync(TenantA);

        // Same physical pooled connection served all three tenant switches (pool size 1).
        Assert.Equal(pidA1, pidB);
        Assert.Equal(pidA1, pidA2);

        Assert.All(namesA1, n => Assert.StartsWith("a-", n, StringComparison.Ordinal));
        Assert.All(namesB, n => Assert.StartsWith("b-", n, StringComparison.Ordinal));
        Assert.Equal(namesA1, namesA2);
        Assert.NotEmpty(namesB);
    }

    /// <summary>
    /// A-RULE 012/013: the tenant on a write is always the server's own. A pre-set, client-supplied
    /// <c>TenantId</c> is never copied or trusted — a foreign value is rejected, nothing is persisted, and the
    /// rejection is audited; a blank value is stamped from the authenticated context.
    /// </summary>
    [PgFact]
    public async Task ClientSuppliedTenantId_Ignored_AndAudited()
    {
        await EnsureSchemaAsync();

        // 1) Blank TenantId (what a client DTO can express): the server stamps ITS tenant, whatever else is sent.
        string stampedId = IdGenerator.NewId();
        await using (ProbeDbContext db = Create(new FixedTenantContext(TenantA)))
        {
            db.Probes.Add(new IsolationProbeEntity { Id = stampedId, Name = "a-stamped", UpdatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        // 2) Client-supplied foreign TenantId: refused, never persisted under either tenant.
        string forgedId = IdGenerator.NewId();
        TenantMismatchException? rejected = null;
        await using (ProbeDbContext db = Create(new FixedTenantContext(TenantA)))
        {
            db.Probes.Add(new IsolationProbeEntity { Id = forgedId, TenantId = TenantB, Name = "forged-client-tenant", UpdatedAt = DateTimeOffset.UtcNow });
            rejected = await ThrowsInChainAsync<TenantMismatchException>(() => db.SaveChangesAsync());
        }

        Assert.NotNull(rejected);

        await using (ProbeDbContext verifyA = Create(new FixedTenantContext(TenantA)))
        {
            Assert.Equal(TenantA, (await verifyA.Probes.AsNoTracking().SingleAsync(e => e.Id == stampedId)).TenantId);
            Assert.Null(await verifyA.Probes.AsNoTracking().FirstOrDefaultAsync(e => e.Id == forgedId));
        }

        await using (ProbeDbContext verifyB = Create(new FixedTenantContext(TenantB)))
        {
            Assert.Null(await verifyB.Probes.AsNoTracking().FirstOrDefaultAsync(e => e.Id == forgedId));
        }

        // 3) The rejection is recorded in the append-only security audit (separate migrated database: the probe
        //    schema above is fixture-local and EnsureCreated-built, so it never shares a database with migrations).
        string auditDb = "animora_audit_" + Guid.NewGuid().ToString("N");
        string admin = PgFixture.AdminConnectionFromEnv ?? pg.ConnectionString;
        await using (var conn = new NpgsqlConnection(admin))
        {
            await conn.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{auditDb}\"", conn);
            _ = await create.ExecuteNonQueryAsync();
        }

        try
        {
            string auditConn = new NpgsqlConnectionStringBuilder(admin) { Database = auditDb, Pooling = false }.ConnectionString;
            await using (var migrate = TestSupport.CreateContext(connection: auditConn))
            {
                await migrate.Database.MigrateAsync();
            }

            string correlation = IdGenerator.NewId();
            await using (var auditContext = TestSupport.CreateContext(new FixedTenantContext(TenantA), auditConn))
            {
                var writer = new Animora.Persistence.PostgreSql.Identity.SecurityAuditWriter(auditContext);
                Animora.Domain.Results.Result written = await writer.WriteAsync(
                    Animora.Persistence.PostgreSql.Identity.SecurityAuditEntry.Create(
                        IdGenerator.NewId(),
                        TenantA,
                        DateTimeOffset.UtcNow,
                        "tenancy",
                        "tenant.mismatch.rejected",
                        Animora.Persistence.PostgreSql.Identity.SecurityAuditActions.OutcomeFailure,
                        correlation,
                        detailJson: Animora.Persistence.PostgreSql.Identity.SecurityAuditDetail.Create(("supplied", TenantB), ("context", TenantA))));
                Assert.True(written.IsSuccess);
            }

            await using var readBack = TestSupport.CreateContext(new FixedTenantContext(TenantA), auditConn);
            var entry = await readBack.SecurityAudit.AsNoTracking().SingleAsync(a => a.CorrelationId == correlation);
            Assert.Equal(TenantA, entry.TenantId);
            Assert.Equal("tenant.mismatch.rejected", entry.Action);
            Assert.Contains(TenantB, entry.DetailJson, StringComparison.Ordinal);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var conn = new NpgsqlConnection(admin);
            await conn.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{auditDb}\" WITH (FORCE)", conn);
            _ = await drop.ExecuteNonQueryAsync();
        }
    }
}
