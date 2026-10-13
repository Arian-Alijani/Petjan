using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Animora.Persistence.PostgreSql.Migrations.Server;

/// <summary>Database-owned sync timestamps and non-destructive recovery from false tombstones.</summary>
[DbContext(typeof(AnimoraServerDbContext))]
[Migration("20261013194500_116_SyncTimestamps")]
public sealed class _116_SyncTimestamps : Migration
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        // Data/function-only migration: preserve the frozen physical model, changing generation metadata only.
        Files115TargetModel.BuildFrozenModel(modelBuilder);
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
            if (entity.FindProperty("UpdatedAt") is { } property)
                property.ValueGenerated = ValueGenerated.OnAddOrUpdate;
    }

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE OR REPLACE FUNCTION public.animora_set_version() RETURNS trigger AS $$
        BEGIN
          NEW."Version" := gen_random_uuid();
          NEW."UpdatedAt" := clock_timestamp();
          RETURN NEW;
        END;
        $$ LANGUAGE plpgsql;

        -- Repair every table governed by the existing version trigger, including non-replicated
        -- SyncableEntity tables. Do not change Deleted or business dates such as RegisteredUtc.
        -- Ordinary UPDATE triggers rotate Version and capture replicated repairs in TenantJournal.
        DO $$
        DECLARE governed record;
        BEGIN
          FOR governed IN
            SELECT DISTINCT n.nspname AS schema_name, c.relname AS table_name
            FROM pg_trigger t
            JOIN pg_class c ON c.oid = t.tgrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            JOIN pg_proc p ON p.oid = t.tgfoid
            JOIN pg_namespace pn ON pn.oid = p.pronamespace
            WHERE NOT t.tgisinternal AND n.nspname = 'public'
              AND pn.nspname = 'public' AND p.proname = 'animora_set_version'
          LOOP
            EXECUTE format('UPDATE %I.%I SET "UpdatedAt" = clock_timestamp() WHERE "UpdatedAt" IS NULL OR NOT isfinite("UpdatedAt")',
                           governed.schema_name, governed.table_name);
          END LOOP;
        END $$;

        -- Old immutable batches may already contain false tombstones, even for finite timestamps.
        -- Change epoch/generation so old pages are fenced out and clients request a fresh snapshot.
        -- Full snapshots preserve pending local intent and restore only server-active rows; actual
        -- soft-deletions remain deletions. Sequence assignment of repair captures stays deferred.
        UPDATE public."TenantFeedState"
        SET "Epoch" = "Epoch" + 1, "Generation" = "Generation" + 1,
            "OldestRetainedSequence" = "LastSequence", "UpdatedUtc" = clock_timestamp();
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE OR REPLACE FUNCTION public.animora_set_version() RETURNS trigger AS $$
        BEGIN
          NEW."Version" := gen_random_uuid();
          RETURN NEW;
        END;
        $$ LANGUAGE plpgsql;
        -- Repaired data and the forward-only feed epoch are intentionally not rolled back.
        """);
}
