using Animora.Server.Tests.Persistence;
using Npgsql;
using Xunit;

namespace Animora.Server.Tests;

/// <summary>
/// ARCH §19 gate L, hardening lane (G07): PostgreSQL row-level security leak proof under a
/// NON-OWNER, NON-SUPERUSER, NOBYPASSRLS application role. RLS is deferred in the product
/// (<c>NoopTenantSessionInitializer</c>; no RLS initializer in the tree), so each test authors
/// its own fixture-local table + policy — never a product table or migration — and drives the
/// tenant GUC <c>app.tenant_id</c> per transaction exactly as a future RLS initializer would.
/// Requires a role that may CREATE DATABASE and CREATE ROLE (the CI "owner" role).
/// </summary>
public sealed class RlsLeakTests(PgFixture pg) : IClassFixture<PgFixture>
{
    private const string TenantA = "RLS-TENANT-A";
    private const string TenantB = "RLS-TENANT-B";

    /// <summary>One isolated table + policy + app role, dropped on dispose.</summary>
    private sealed class RlsEnv : IAsyncDisposable
    {
        private readonly string _adminConnection;

        private RlsEnv(string adminConnection, string table, string role, string password, NpgsqlDataSource appSource)
        {
            _adminConnection = adminConnection;
            Table = table;
            Role = role;
            Password = password;
            AppSource = appSource;
        }

        public string Table { get; }

        public string Role { get; }

        public string Password { get; }

        /// <summary>Pool of size 1 for the app role: every logical session reuses one physical connection.</summary>
        public NpgsqlDataSource AppSource { get; }

        public static async Task<RlsEnv> CreateAsync(PgFixture pg)
        {
            string id = Guid.NewGuid().ToString("N")[..12];
            string table = "rls_probe_" + id;
            string role = "rls_app_" + id;
            string password = "Pw" + Guid.NewGuid().ToString("N");

            await using (var admin = new NpgsqlConnection(pg.ConnectionString))
            {
                await admin.OpenAsync();
                await ExecAsync(admin, $"CREATE ROLE \"{role}\" LOGIN PASSWORD '{password}' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE");
                await ExecAsync(admin, $"CREATE TABLE public.\"{table}\" (\"Id\" text PRIMARY KEY, \"TenantId\" text NOT NULL, \"Name\" text NOT NULL)");

                // Seed BEFORE enabling RLS: FORCE RLS would also bind the owner.
                await ExecAsync(admin,
                    $"INSERT INTO public.\"{table}\" VALUES ('a1','{TenantA}','a-one'),('a2','{TenantA}','a-two'),('b1','{TenantB}','b-one'),('b2','{TenantB}','b-two')");

                await ExecAsync(admin, $"ALTER TABLE public.\"{table}\" ENABLE ROW LEVEL SECURITY");
                await ExecAsync(admin, $"ALTER TABLE public.\"{table}\" FORCE ROW LEVEL SECURITY");
                await ExecAsync(admin,
                    $"CREATE POLICY tenant_isolation ON public.\"{table}\" " +
                    "USING (\"TenantId\" = current_setting('app.tenant_id', true)) " +
                    "WITH CHECK (\"TenantId\" = current_setting('app.tenant_id', true))");

                // DML only: no DDL, no ownership, no BYPASSRLS.
                await ExecAsync(admin, $"GRANT USAGE ON SCHEMA public TO \"{role}\"");
                await ExecAsync(admin, $"GRANT SELECT, INSERT, UPDATE, DELETE ON public.\"{table}\" TO \"{role}\"");
            }

            var csb = new NpgsqlConnectionStringBuilder(pg.ConnectionString)
            {
                Username = role,
                Password = password,
                Pooling = true,
                MinPoolSize = 0,
                MaxPoolSize = 1,
            };
            return new RlsEnv(pg.ConnectionString, table, role, password, NpgsqlDataSource.Create(csb.ConnectionString));
        }

        public async ValueTask DisposeAsync()
        {
            await AppSource.DisposeAsync();
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(_adminConnection);
            await admin.OpenAsync();
            await ExecAsync(admin, $"DROP TABLE IF EXISTS public.\"{Table}\"");
            // The disposable role OWNS nothing — it only received GRANTs on the (now dropped)
            // probe table plus USAGE on schema public. "DROP OWNED BY" needs the caller to be a
            // superuser or an actual MEMBER of the target role; animora_owner (NOSUPERUSER,
            // CREATEROLE) is neither, so it raised 42501 "permission denied to drop objects" and
            // left the role undropped. Dropping the table clears the table-level ACL; revoking the
            // schema grant clears the last residual privilege, so DROP ROLE then succeeds without
            // the privileged DROP OWNED BY.
            await ExecAsync(admin, $"REVOKE ALL ON SCHEMA public FROM \"{Role}\"");
            await ExecAsync(admin, $"DROP ROLE IF EXISTS \"{Role}\"");
        }

        /// <summary>Reads names inside a transaction scoped to <paramref name="tenant"/> (null = no tenant context).</summary>
        public async Task<(int Pid, string[] Names)> ReadAsync(string? tenant, CancellationToken ct = default)
        {
            await using NpgsqlConnection conn = await AppSource.OpenConnectionAsync(ct);
            await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(ct);
            if (tenant is not null)
            {
                await SetTenantAsync(conn, tx, tenant, ct);
            }

            int pid = (int)(await ScalarAsync(conn, tx, "SELECT pg_backend_pid()", ct))!;
            var names = new List<string>();
            await using (var cmd = new NpgsqlCommand($"SELECT \"Name\" FROM public.\"{Table}\" ORDER BY \"Name\"", conn, tx))
            await using (NpgsqlDataReader r = await cmd.ExecuteReaderAsync(ct))
            {
                while (await r.ReadAsync(ct))
                {
                    names.Add(r.GetString(0));
                }
            }

            await tx.CommitAsync(ct);
            return (pid, names.ToArray());
        }

        public static async Task SetTenantAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string tenant, CancellationToken ct = default)
        {
            // is_local = true: the GUC dies with the transaction (the per-transaction contract).
            await using var cmd = new NpgsqlCommand("SELECT set_config('app.tenant_id', @t, true)", conn, tx);
            cmd.Parameters.AddWithValue("t", tenant);
            _ = await cmd.ExecuteScalarAsync(ct);
        }

        private static async Task<object?> ScalarAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string sql, CancellationToken ct)
        {
            await using var cmd = new NpgsqlCommand(sql, conn, tx);
            return await cmd.ExecuteScalarAsync(ct);
        }

        private static async Task ExecAsync(NpgsqlConnection conn, string sql)
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            _ = await cmd.ExecuteNonQueryAsync();
        }
    }

    [PgFact]
    public async Task A_B_A_PoolReuse_NoLeak()
    {
        await using RlsEnv env = await RlsEnv.CreateAsync(pg);

        (int pidA1, string[] namesA1) = await env.ReadAsync(TenantA);
        (int pidB, string[] namesB) = await env.ReadAsync(TenantB);
        (int pidA2, string[] namesA2) = await env.ReadAsync(TenantA);

        // Pool size 1: one physical connection served all three tenant switches.
        Assert.Equal(pidA1, pidB);
        Assert.Equal(pidA1, pidA2);

        Assert.Equal(new[] { "a-one", "a-two" }, namesA1);
        Assert.Equal(new[] { "b-one", "b-two" }, namesB);
        Assert.Equal(namesA1, namesA2);
    }

    [PgFact]
    public async Task MissingContext_Denies()
    {
        await using RlsEnv env = await RlsEnv.CreateAsync(pg);

        // Never-set GUC on a fresh connection: nothing visible.
        Assert.Empty((await env.ReadAsync(tenant: null)).Names);

        // A previous tenant transaction must not leave its context behind on the pooled connection.
        Assert.NotEmpty((await env.ReadAsync(TenantA)).Names);
        Assert.Empty((await env.ReadAsync(tenant: null)).Names);

        // Writing without a tenant context is refused by the WITH CHECK policy (42501).
        await using NpgsqlConnection conn = await env.AppSource.OpenConnectionAsync();
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync();
        await using var insert = new NpgsqlCommand(
            $"INSERT INTO public.\"{env.Table}\" VALUES ('x1','{TenantA}','forged-no-context')", conn, tx);
        PostgresException ex = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
    }

    [PgFact]
    public async Task NonOwnerNoBypass_CannotSeeAll()
    {
        await using RlsEnv env = await RlsEnv.CreateAsync(pg);

        await using NpgsqlConnection conn = await env.AppSource.OpenConnectionAsync();

        // The role is exactly what the hardening lane promises: not super, no BYPASSRLS, not the table owner.
        await using (var cmd = new NpgsqlCommand(
            "SELECT r.rolsuper, r.rolbypassrls, (c.relowner = r.oid) " +
            "FROM pg_roles r, pg_class c WHERE r.rolname = current_user AND c.oid = @t::regclass", conn))
        {
            cmd.Parameters.AddWithValue("t", $"public.\"{env.Table}\"");
            await using NpgsqlDataReader r = await cmd.ExecuteReaderAsync();
            Assert.True(await r.ReadAsync());
            Assert.False(r.GetBoolean(0), "app role must not be superuser");
            Assert.False(r.GetBoolean(1), "app role must not have BYPASSRLS");
            Assert.False(r.GetBoolean(2), "app role must not own the table");
        }

        await using (NpgsqlTransaction tx = await conn.BeginTransactionAsync())
        {
            await RlsEnv.SetTenantAsync(conn, tx, TenantA);

            // Even an unfiltered count only sees the caller's tenant (2 of the 4 seeded rows).
            await using var count = new NpgsqlCommand($"SELECT count(*) FROM public.\"{env.Table}\"", conn, tx);
            Assert.Equal(2L, (long)(await count.ExecuteScalarAsync())!);

            // A cross-tenant write is rejected by WITH CHECK.
            await using var forged = new NpgsqlCommand(
                $"INSERT INTO public.\"{env.Table}\" VALUES ('x2','{TenantB}','forged-cross')", conn, tx);
            PostgresException ex = await Assert.ThrowsAsync<PostgresException>(() => forged.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
        }

        // Opting out of RLS is an error for a non-bypass role, never a silent full read.
        await using (NpgsqlTransaction tx = await conn.BeginTransactionAsync())
        {
            await using var off = new NpgsqlCommand("SET LOCAL row_security = off", conn, tx);
            _ = await off.ExecuteNonQueryAsync();
            await using var all = new NpgsqlCommand($"SELECT count(*) FROM public.\"{env.Table}\"", conn, tx);
            PostgresException ex = await Assert.ThrowsAsync<PostgresException>(() => all.ExecuteScalarAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
        }
    }

    [PgFact]
    public async Task RollbackCancellation_NoLeak()
    {
        await using RlsEnv env = await RlsEnv.CreateAsync(pg);

        // 1) A rolled-back tenant-A write leaves no trace and no context behind.
        await using (NpgsqlConnection conn = await env.AppSource.OpenConnectionAsync())
        await using (NpgsqlTransaction tx = await conn.BeginTransactionAsync())
        {
            await RlsEnv.SetTenantAsync(conn, tx, TenantA);
            await using var insert = new NpgsqlCommand(
                $"INSERT INTO public.\"{env.Table}\" VALUES ('r1','{TenantA}','a-rolled-back')", conn, tx);
            _ = await insert.ExecuteNonQueryAsync();
            await tx.RollbackAsync();
        }

        Assert.DoesNotContain("a-rolled-back", (await env.ReadAsync(TenantA)).Names);
        Assert.Empty((await env.ReadAsync(tenant: null)).Names);

        // 2) A cancelled in-flight statement (tenant A) must not poison the single pooled connection.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using NpgsqlConnection conn = await env.AppSource.OpenConnectionAsync(cts.Token);
            await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cts.Token);
            await RlsEnv.SetTenantAsync(conn, tx, TenantA, cts.Token);
            await using var sleep = new NpgsqlCommand("SELECT pg_sleep(30)", conn, tx);
            _ = await sleep.ExecuteScalarAsync(cts.Token);
        });

        // Same pool slot, next caller: tenant B sees only B; no-context sees nothing.
        Assert.Equal(new[] { "b-one", "b-two" }, (await env.ReadAsync(TenantB)).Names);
        Assert.Empty((await env.ReadAsync(tenant: null)).Names);
    }
}
