using Animora.Persistence.PostgreSql.Replication;
using Animora.Server.Tests.Persistence;
using Npgsql;
using Xunit;
namespace Animora.Server.Tests.Replication;

/// <summary>Exercises actual PostgreSQL deferred events in an isolated database, not mocked sequencing.</summary>
public sealed class JournalOrderingTests(PgFixture pg) : IClassFixture<PgFixture>
{
    private async Task<NpgsqlConnection> Setup()
    {
        var c = new NpgsqlConnection(pg.ConnectionString);
        await c.OpenAsync();
        await Exec(c, """
            DROP TABLE IF EXISTS "TestChildren", "TestRoots", "TenantJournal", "TenantFeedState" CASCADE;
            DROP FUNCTION IF EXISTS animora_journal_capture() CASCADE;
            DROP FUNCTION IF EXISTS animora_journal_assign_sequence() CASCADE;
            CREATE TABLE "TenantFeedState" ("TenantId" varchar(26) PRIMARY KEY, "Epoch" bigint DEFAULT 1,
              "Generation" bigint DEFAULT 1, "LastSequence" bigint DEFAULT 0, "OldestRetainedSequence" bigint DEFAULT 0,
              "UpdatedUtc" timestamptz DEFAULT now());
            CREATE TABLE "TenantJournal" ("TenantId" varchar(26), "Id" bigint GENERATED ALWAYS AS IDENTITY,
              "Entity" text, "EntityId" text, "Op" char(1), "Version" uuid, "ChangeSequence" bigint,
              "Epoch" bigint, "CapturedUtc" timestamptz, PRIMARY KEY ("TenantId","Id"));
            CREATE UNIQUE INDEX ON "TenantJournal" ("TenantId","Epoch","ChangeSequence") WHERE "ChangeSequence" IS NOT NULL;
            CREATE TABLE "TestRoots" ("Id" text PRIMARY KEY, "TenantId" text, "Version" uuid DEFAULT gen_random_uuid(), "Value" text);
            CREATE TABLE "TestChildren" ("Id" text PRIMARY KEY, "OwnerId" text REFERENCES "TestRoots"("Id") ON DELETE CASCADE, "Value" text);
            INSERT INTO "TenantFeedState" ("TenantId") VALUES ('A'), ('B');
            """);
        await Exec(c, JournalSqlTemplates.CaptureFunction + JournalSqlTemplates.AssignFunction + JournalSqlTemplates.DeferredTrigger);
        var d = new SyncEntityDescriptor("Product", typeof(Animora.Domain.Inventory.Product), "TestRoots", []);
        await Exec(c, JournalSqlTemplates.CaptureTrigger(d, "TestRoots", "Id", false));
        await Exec(c, JournalSqlTemplates.DeferrableChildCaptureTrigger(d, new("TestChildren", "OwnerId", false)));
        return c;
    }
    private static async Task Exec(NpgsqlConnection c, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync();
    }
    private static async Task<long> Scalar(NpgsqlConnection c, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, c);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
    [PgFact]
    public async Task SequenceOrder_EqualsCommitOrder_UnderConcurrency()
    {
        await using var a = await Setup();
        await using var b = new NpgsqlConnection(pg.ConnectionString); await b.OpenAsync();
        await using var ta = await a.BeginTransactionAsync(); await using var tb = await b.BeginTransactionAsync();
        await Exec(a, "INSERT INTO \"TestRoots\" VALUES ('first','A',gen_random_uuid(),'a')");
        await Exec(b, "INSERT INTO \"TestRoots\" VALUES ('second','A',gen_random_uuid(),'b')");
        await tb.CommitAsync(); await ta.CommitAsync();
        Assert.Equal(1, await Scalar(a, "SELECT \"ChangeSequence\" FROM \"TenantJournal\" WHERE \"EntityId\"='second'"));
        Assert.Equal(2, await Scalar(a, "SELECT \"ChangeSequence\" FROM \"TenantJournal\" WHERE \"EntityId\"='first'"));
    }
    [PgFact]
    public async Task RollbackConsumesNoSequence_NoGap()
    {
        await using var c = await Setup();
        await using (var tx = await c.BeginTransactionAsync()) { await Exec(c, "INSERT INTO \"TestRoots\" VALUES ('r','A',gen_random_uuid(),'x')"); await tx.RollbackAsync(); }
        await Exec(c, "INSERT INTO \"TestRoots\" VALUES ('s','A',gen_random_uuid(),'x')");
        Assert.Equal(1, await Scalar(c, "SELECT \"LastSequence\" FROM \"TenantFeedState\" WHERE \"TenantId\"='A'"));
    }
    [PgFact]
    public async Task ChildOnlyChange_CapturesRoot()
    {
        await using var c = await Setup();
        await Exec(c, "INSERT INTO \"TestRoots\" VALUES ('r','A',gen_random_uuid(),'x'); INSERT INTO \"TestChildren\" VALUES ('child','r','a')");
        var before = await Scalar(c, "SELECT \"LastSequence\" FROM \"TenantFeedState\" WHERE \"TenantId\"='A'");
        await Exec(c, "UPDATE \"TestChildren\" SET \"Value\"='changed'");
        Assert.Equal(before + 1, await Scalar(c, "SELECT \"LastSequence\" FROM \"TenantFeedState\" WHERE \"TenantId\"='A'"));
        Assert.Equal(1, await Scalar(c, "SELECT count(*) FROM \"TenantJournal\" WHERE \"EntityId\"='r' AND \"ChangeSequence\"=(SELECT \"LastSequence\" FROM \"TenantFeedState\" WHERE \"TenantId\"='A')"));
    }
    [PgFact]
    public async Task Delete_CapturesTombstoneKey()
    {
        await using var c = await Setup();
        await Exec(c, "INSERT INTO \"TestRoots\" VALUES ('r','A',gen_random_uuid(),'x')");
        await Exec(c, "DELETE FROM \"TestRoots\" WHERE \"Id\"='r'");
        Assert.Equal(1, await Scalar(c, "SELECT count(*) FROM \"TenantJournal\" WHERE \"Op\"='D' AND \"EntityId\"='r' AND \"Version\" IS NULL"));
    }
    [PgFact]
    public async Task SameTransactionDuplicates_Coalesced()
    {
        await using var c = await Setup(); await using var tx = await c.BeginTransactionAsync();
        await Exec(c, "INSERT INTO \"TestRoots\" VALUES ('r','A',gen_random_uuid(),'x'); UPDATE \"TestRoots\" SET \"Value\"='y'; DELETE FROM \"TestRoots\"");
        await tx.CommitAsync(); Assert.Equal(1, await Scalar(c, "SELECT count(*) FROM \"TenantJournal\""));
        Assert.Equal(1, await Scalar(c, "SELECT count(*) FROM \"TenantJournal\" WHERE \"Op\"='D' AND \"ChangeSequence\"=1"));
    }
    [PgFact]
    public async Task DirectGovernedSql_Captured()
    {
        await using var c = await Setup(); await Exec(c, "INSERT INTO \"TestRoots\" VALUES ('r','A',gen_random_uuid(),'x')");
        await Exec(c, "UPDATE \"TestRoots\" SET \"Value\"='y'");
        Assert.Equal(2, await Scalar(c, "SELECT count(*) FROM \"TenantJournal\""));
    }
    [PgFact]
    public async Task TwoTenants_IndependentCounters()
    {
        await using var c = await Setup(); await Exec(c, "INSERT INTO \"TestRoots\" VALUES ('a','A',gen_random_uuid(),'x'),('b','B',gen_random_uuid(),'y')");
        Assert.Equal(2, await Scalar(c, "SELECT count(*) FROM \"TenantFeedState\" WHERE \"LastSequence\"=1"));
    }
    [PgFact]
    public async Task EpochAdvance_FencesOldRows()
    {
        await using var c = await Setup(); await Exec(c, "INSERT INTO \"TestRoots\" VALUES ('r','A',gen_random_uuid(),'x')");
        await Exec(c, "UPDATE \"TenantFeedState\" SET \"Epoch\"=\"Epoch\"+1, \"OldestRetainedSequence\"=\"LastSequence\" WHERE \"TenantId\"='A'");
        await Exec(c, "UPDATE \"TestRoots\" SET \"Value\"='y'");
        Assert.Equal(1, await Scalar(c, "SELECT count(*) FROM \"TenantJournal\" WHERE \"Epoch\"=2 AND \"ChangeSequence\"=2"));
    }
    [PgFact]
    public async Task DeferredChildren_AllowEitherSqlOrder_WithFinalJournalToken()
    {
        await using var c = await Setup();
        await Exec(c, "INSERT INTO \"TestRoots\" VALUES ('r','A',gen_random_uuid(),'x'); INSERT INTO \"TestChildren\" VALUES ('child','r','a')");
        foreach (var childFirst in new[] { true, false })
        {
            await using var tx = await c.BeginTransactionAsync();
            await Exec(c, """
                SET CONSTRAINTS "trg_TestChildren_journal" DEFERRED;
                CREATE TEMP TABLE original_version ON COMMIT DROP AS SELECT "Version" FROM "TestRoots" WHERE "Id"='r';
                """);
            if (childFirst) await Exec(c, "UPDATE \"TestChildren\" SET \"Value\"='changed'");
            Assert.Equal(1, await Scalar(c, """
                WITH updated AS (UPDATE "TestRoots" SET "Value"='changed', "Version"=gen_random_uuid()
                  WHERE "Id"='r' AND "Version"=(SELECT "Version" FROM original_version) RETURNING 1)
                SELECT count(*) FROM updated
                """));
            if (!childFirst) await Exec(c, "UPDATE \"TestChildren\" SET \"Value\"='changed'");
            await Exec(c, "SET CONSTRAINTS \"trg_TestChildren_journal\" IMMEDIATE");
            // Other deferred events (sequence assignment) have NOT been flushed here.
            Assert.True(await Scalar(c, "SELECT count(*) FROM \"TenantJournal\" WHERE \"ChangeSequence\" IS NULL") > 0);
            await tx.CommitAsync();
            Assert.Equal(1, await Scalar(c, """
                SELECT count(*) FROM "TenantJournal" j JOIN "TestRoots" r ON r."Id"=j."EntityId"
                WHERE j."ChangeSequence"=(SELECT "LastSequence" FROM "TenantFeedState" WHERE "TenantId"='A')
                  AND j."Version"=r."Version"
                """));
        }
    }

    [PgFact]
    public async Task DirectChildSql_RemainsImmediate_AndMovingChildInvalidatesBothRoots()
    {
        await using var c = await Setup();
        await Exec(c, """
            INSERT INTO "TestRoots" VALUES ('r','A',gen_random_uuid(),'x'),('s','A',gen_random_uuid(),'y');
            INSERT INTO "TestChildren" VALUES ('child','r','a');
            """);
        await using var tx = await c.BeginTransactionAsync();
        await Exec(c, """
            CREATE TEMP TABLE previous_versions ON COMMIT DROP AS SELECT "Id", "Version" FROM "TestRoots";
            UPDATE "TestChildren" SET "OwnerId"='s' WHERE "Id"='child';
            """);
        Assert.Equal(2, await Scalar(c, """
            SELECT count(*) FROM "TestRoots" r JOIN previous_versions p ON r."Id"=p."Id" WHERE r."Version"<>p."Version"
            """));
        await tx.CommitAsync();
    }

}
