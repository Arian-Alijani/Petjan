using Animora.Persistence.PostgreSql.Replication;
using Microsoft.EntityFrameworkCore.Migrations;
namespace Animora.Persistence.PostgreSql.Migrations.Server;

public partial class _109_ChangeJournal : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE public."TenantFeedState" (
              "TenantId" varchar(26) PRIMARY KEY, "Epoch" bigint NOT NULL DEFAULT 1,
              "Generation" bigint NOT NULL DEFAULT 1, "LastSequence" bigint NOT NULL DEFAULT 0,
              "OldestRetainedSequence" bigint NOT NULL DEFAULT 0,
              "UpdatedUtc" timestamptz(6) NOT NULL DEFAULT clock_timestamp());
            CREATE TABLE public."TenantJournal" (
              "TenantId" varchar(26) NOT NULL, "Id" bigint GENERATED ALWAYS AS IDENTITY,
              "Entity" varchar(48) NOT NULL, "EntityId" varchar(26) NOT NULL,
              "Op" char(1) NOT NULL CHECK ("Op" IN ('U','D')), "Version" uuid,
              "ChangeSequence" bigint, "Epoch" bigint NOT NULL, "CapturedUtc" timestamptz(6) NOT NULL,
              PRIMARY KEY ("TenantId", "Id"));
            CREATE UNIQUE INDEX "IX_TenantJournal_TenantId_Epoch_ChangeSequence"
              ON public."TenantJournal" ("TenantId", "Epoch", "ChangeSequence") WHERE "ChangeSequence" IS NOT NULL;
            CREATE INDEX "IX_TenantJournal_TenantId_Entity_EntityId_ChangeSequence"
              ON public."TenantJournal" ("TenantId", "Entity", "EntityId", "ChangeSequence");
            INSERT INTO public."TenantFeedState" ("TenantId") SELECT "Id" FROM public."Tenants" ON CONFLICT DO NOTHING;
            -- G09 MUST revoke TRUNCATE from the runtime role on all replicated tables.
            """);
        migrationBuilder.Sql(JournalSqlTemplates.CaptureFunction);
        migrationBuilder.Sql(JournalSqlTemplates.AssignFunction);
        migrationBuilder.Sql(JournalSqlTemplates.DeferredTrigger);
        foreach (var descriptor in SyncContractCatalog.ProjectionOrder)
            JournalTriggers.Apply(migrationBuilder, descriptor);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var descriptor in SyncContractCatalog.ProjectionOrder.Reverse())
            JournalTriggers.Drop(migrationBuilder, descriptor);
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS trg_TenantJournal_assign ON public."TenantJournal";
            DROP FUNCTION public.animora_journal_assign_sequence();
            DROP FUNCTION public.animora_journal_capture();
            DROP TABLE public."TenantJournal";
            DROP TABLE public."TenantFeedState";
            """);
    }
}
