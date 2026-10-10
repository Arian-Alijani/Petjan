using Microsoft.EntityFrameworkCore.Migrations;
namespace Animora.Persistence.PostgreSql.Migrations.Server;

public partial class _110_FeedBatches : Migration
{
    protected override void Up(MigrationBuilder mb) => mb.Sql("""
        CREATE TABLE public."FeedBatch" (
          "TenantId" varchar(26) NOT NULL, "BatchId" varchar(26) NOT NULL, "Epoch" bigint NOT NULL,
          "Generation" bigint NOT NULL, "FromSequenceExclusive" bigint NOT NULL, "Watermark" bigint NOT NULL,
          "PageCount" integer NOT NULL, "ContentHash" char(64) NOT NULL, "State" smallint NOT NULL,
          "IsFullSnapshot" boolean NOT NULL, "CreatedUtc" timestamptz(6) NOT NULL, "RetiredUtc" timestamptz(6),
          PRIMARY KEY ("TenantId", "BatchId"));
        CREATE UNIQUE INDEX "IX_FeedBatch_TenantId_Epoch_FromSequenceExclusive_Watermark_IsFullSnapshot"
          ON public."FeedBatch" ("TenantId","Epoch","FromSequenceExclusive","Watermark","IsFullSnapshot");
        CREATE INDEX "IX_FeedBatch_TenantId_State_CreatedUtc" ON public."FeedBatch" ("TenantId","State","CreatedUtc");
        CREATE TABLE public."FeedBatchPage" (
          "TenantId" varchar(26) NOT NULL, "BatchId" varchar(26) NOT NULL, "PageOrdinal" integer NOT NULL,
          "PageHash" char(64) NOT NULL, "SizeBytes" integer NOT NULL, PRIMARY KEY ("TenantId","BatchId","PageOrdinal"));
        CREATE FUNCTION public.animora_feed_batch_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF OLD."State" IN (1,2) THEN
            IF (to_jsonb(OLD) - 'RetiredUtc' - 'State') IS DISTINCT FROM (to_jsonb(NEW) - 'RetiredUtc' - 'State')
              OR NOT (NEW."State" = OLD."State" OR (OLD."State"=1 AND NEW."State"=2 AND NEW."RetiredUtc" IS NOT NULL)) THEN
              RAISE EXCEPTION 'Immutable feed batch';
            END IF;
          END IF;
          RETURN NEW;
        END $$;
        -- Ready->Retired is the sole permitted lifecycle transition; immutable content is never changed.
        CREATE TRIGGER trg_FeedBatch_immutable BEFORE UPDATE ON public."FeedBatch"
          FOR EACH ROW EXECUTE FUNCTION public.animora_feed_batch_immutable();
        CREATE FUNCTION public.animora_feed_page_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'Immutable feed page'; END $$;
        CREATE TRIGGER trg_FeedBatchPage_immutable BEFORE UPDATE ON public."FeedBatchPage"
          FOR EACH ROW EXECUTE FUNCTION public.animora_feed_page_immutable();
        """);
    protected override void Down(MigrationBuilder mb) => mb.Sql("""
        DROP TABLE public."FeedBatchPage";
        DROP FUNCTION public.animora_feed_page_immutable();
        DROP TABLE public."FeedBatch";
        DROP FUNCTION public.animora_feed_batch_immutable();
        """);
}
