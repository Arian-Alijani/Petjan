using System.Reflection;
using Animora.Server.Tests.Persistence;
using Xunit;

namespace Animora.Server.Tests;

/// <summary>
/// Anti-escape guard for the mandatory PostgreSQL lane (G07, <c>pg-gates.yml</c>): the ARCH §19 gate
/// tests must exist AND be <see cref="PgFactAttribute"/>-gated (so <c>ANIMORA_REQUIRE_PG=1</c> turns a
/// skip into a failure), and no PostgreSQL-backed class may quietly use a plain <c>[Fact]</c>.
/// Pure reflection / source scan — never needs a database itself.
/// </summary>
public sealed class PgGateManifestTests
{
    // The real class → method map of the G07 gate set. FQN = namespace.Class.Method.
    private static readonly string[] RequiredPgFacts =
    [
        "Animora.Server.Tests.Persistence.PgGatedTests.Version_ChangesOnEveryUpdate_IncludingRawSql",
        "Animora.Server.Tests.Persistence.PgGatedTests.Applies101_OnEmptyDatabase",
        "Animora.Server.Tests.Persistence.PgGatedTests.KeysSurviveContextRecreation",

        "Animora.Server.Tests.Persistence.TenantIsolationTests.A_CannotRead_B_ById",
        "Animora.Server.Tests.Persistence.TenantIsolationTests.A_CannotRead_B_List",
        "Animora.Server.Tests.Persistence.TenantIsolationTests.Write_WithForeignTenant_Throws_TenantMismatch",
        "Animora.Server.Tests.Persistence.TenantIsolationTests.NullContext_Throws_TenantContextMissing",
        "Animora.Server.Tests.Persistence.TenantIsolationTests.SoftDeleteLift_StillHidesOtherTenant",
        "Animora.Server.Tests.Persistence.TenantIsolationTests.PooledConnection_A_B_A_NoLeak",
        "Animora.Server.Tests.Persistence.TenantIsolationTests.ClientSuppliedTenantId_Ignored_AndAudited",

        "Animora.Server.Tests.Replication.JournalOrderingTests.SequenceOrder_EqualsCommitOrder_UnderConcurrency",
        "Animora.Server.Tests.Replication.JournalOrderingTests.RollbackConsumesNoSequence_NoGap",
        "Animora.Server.Tests.Replication.JournalOrderingTests.ChildOnlyChange_CapturesRoot",
        "Animora.Server.Tests.Replication.JournalOrderingTests.Delete_CapturesTombstoneKey",
        "Animora.Server.Tests.Replication.JournalOrderingTests.SameTransactionDuplicates_Coalesced",
        "Animora.Server.Tests.Replication.JournalOrderingTests.DirectGovernedSql_Captured",
        "Animora.Server.Tests.Replication.JournalOrderingTests.TwoTenants_IndependentCounters",
        "Animora.Server.Tests.Replication.JournalOrderingTests.EpochAdvance_FencesOldRows",

        "Animora.Server.Tests.Sync.BatchFreezerTests.WatermarkAndRows_FromOneSnapshot",
        "Animora.Server.Tests.Sync.BatchFreezerTests.SnapshotCommitted_BeforePageMaterialization",
        "Animora.Server.Tests.Sync.BatchFreezerTests.SameRange_SameBatchId_ByteStable",
        "Animora.Server.Tests.Sync.BatchFreezerTests.SnapshotOverMaxLifetime_AbortsAndRetries",
        "Animora.Server.Tests.Sync.GcTests.JournalDeletion_AdvancesFloor_SameTransaction",
        "Animora.Server.Tests.Sync.GcTests.CrashAfterFileDelete_BeforeRowDelete_Recovers",
        "Animora.Server.Tests.Sync.SyncIsolationTests.TenantA_Batch_ContainsNoTenantB_Rows",
        "Animora.Server.Tests.Sync.SyncFlowTests.OpenDownloadResume_FromLastPage",

        "Animora.Server.Tests.Observability.SnapshotAgeMonitorTests.OldSnapshot_ReportsDegraded",
        "Animora.Server.Tests.Observability.QueryBudgetPgTests.StatementTimeout_Enforced",
        "Animora.Server.Tests.Observability.QueryBudgetPgTests.Reporting_Concurrency1_Timeout5s_RowCap",

        // G07 additions.
        "Animora.Server.Tests.RlsLeakTests.A_B_A_PoolReuse_NoLeak",
        "Animora.Server.Tests.RlsLeakTests.MissingContext_Denies",
        "Animora.Server.Tests.RlsLeakTests.NonOwnerNoBypass_CannotSeeAll",
        "Animora.Server.Tests.RlsLeakTests.RollbackCancellation_NoLeak",
        "Animora.Server.Tests.RestoreDrillTests.Restore_ToReadiness_KnownTenantIntact",
        "Animora.Server.Tests.RestoreDrillTests.MissingKeyRing_RefusesReadiness_ServesLiveness",
    ];

    private static IEnumerable<MethodInfo> TestMethods() =>
        typeof(PgGateManifestTests).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));

    private static string Fqn(MethodInfo m) => $"{m.DeclaringType!.FullName}.{m.Name}";

    [Fact]
    public void EveryPgGatedTest_IsInTheRequiredList()
    {
        HashSet<string> discovered = TestMethods()
            .Where(m => m.GetCustomAttribute<PgFactAttribute>() is not null)
            .Select(Fqn)
            .ToHashSet(StringComparer.Ordinal);

        // 1) Every required gate test still exists and is PgFact-gated (renaming/removing one fails here).
        string[] missing = RequiredPgFacts.Where(r => !discovered.Contains(r)).ToArray();
        Assert.True(missing.Length == 0, "Required PG gate tests missing or no longer [PgFact]: " + string.Join(", ", missing));

        // 2) The required list has no duplicates (a typo cannot hide a gap).
        Assert.Equal(RequiredPgFacts.Length, RequiredPgFacts.Distinct(StringComparer.Ordinal).Count());

        // 3) Nothing PG-gated exists outside the lane's test classes' assembly — every discovered test
        //    runs under pg-gate.sh (suite filter "Server"), so a new PgFact is automatically mandatory.
        Assert.All(discovered, fqn => Assert.StartsWith("Animora.Server.Tests.", fqn, StringComparison.Ordinal));
        Assert.True(discovered.Count >= RequiredPgFacts.Length);
    }

    [Fact]
    public void NoPgBackedClass_UsesPlainFact()
    {
        // A class that takes PgFixture talks to PostgreSQL; a plain [Fact] there would run (and fail)
        // without a database, or silently skip semantics under REQUIRE_PG. Every test method must be [PgFact].
        var offenders = new List<string>();
        foreach (Type type in typeof(PgGateManifestTests).Assembly.GetTypes())
        {
            bool takesFixture = type.GetConstructors().Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(PgFixture)));
            if (!takesFixture)
            {
                continue;
            }

            foreach (MethodInfo m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                bool isPlainFact = m.GetCustomAttributes<FactAttribute>(inherit: false).Any(a => a is not PgFactAttribute);
                if (isPlainFact)
                {
                    offenders.Add(Fqn(m));
                }
            }
        }

        Assert.True(offenders.Count == 0, "PG-backed tests using plain [Fact]: " + string.Join(", ", offenders));
    }

    [Fact]
    public void NativeSqliteGate_ExistsInPersistenceTests_AndNoOtherProjectOwnsPgTests()
    {
        string root = RepoRoot.Find();

        // The native-SQLite ≥ 3.50.2 gate lives in Persistence.Tests (not Server.Tests): assert it by source.
        string native = File.ReadAllText(Path.Combine(root, "tests", "Animora.Persistence.Tests", "NativeSqliteVersionTests.cs"));
        Assert.Contains("class NativeSqliteVersionTests", native, StringComparison.Ordinal);
        Assert.Contains("LoadedNativeVersion_IsAtLeastTheMinimum", native, StringComparison.Ordinal);
        Assert.Contains("NativeSqliteAssertion.Minimum", native, StringComparison.Ordinal);

        // No other test project may start using the PG env contract without being added to this manifest.
        foreach (string project in Directory.EnumerateDirectories(Path.Combine(root, "tests")))
        {
            if (Path.GetFileName(project) == "Animora.Server.Tests")
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                         && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
            {
                string text = File.ReadAllText(file);
                Assert.DoesNotContain(PgFixture.ConnectionEnvVar, text, StringComparison.Ordinal);
                Assert.DoesNotContain("PgFact", text, StringComparison.Ordinal);
            }
        }
    }
}
