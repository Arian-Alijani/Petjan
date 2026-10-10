using Microsoft.EntityFrameworkCore.Migrations;
namespace Animora.Persistence.PostgreSql.Migrations.Server;

public partial class _111_ServerOutbox : Migration
{
    protected override void Up(MigrationBuilder mb) => mb.Sql("""
        CREATE TABLE public."ServerOutbox" (
          "TenantId" varchar(26) NOT NULL, "Id" varchar(26) NOT NULL, "MessageType" varchar(64) NOT NULL,
          "PayloadJson" jsonb NOT NULL, "Status" smallint NOT NULL, "Attempts" integer NOT NULL,
          "NextAttemptUtc" timestamptz(6) NOT NULL, "ClaimToken" varchar(26), "ClaimedUntilUtc" timestamptz(6),
          "LastError" varchar(512), "CreatedUtc" timestamptz(6) NOT NULL, "CompletedUtc" timestamptz(6),
          "CorrelationId" varchar(64) NOT NULL, PRIMARY KEY ("TenantId","Id"),
          CHECK ("Status" <> 1 OR ("ClaimToken" IS NOT NULL AND "ClaimedUntilUtc" IS NOT NULL)));
        -- Documented global relay exception to tenant-leading indexes: work distribution only.
        CREATE INDEX "IX_ServerOutbox_Status_NextAttemptUtc" ON public."ServerOutbox" ("Status","NextAttemptUtc") WHERE "Status" IN (0,1);
        CREATE INDEX "IX_ServerOutbox_TenantId_CreatedUtc" ON public."ServerOutbox" ("TenantId","CreatedUtc");
        CREATE TABLE public."ConsumedMessages" ("ConsumerName" varchar(48) NOT NULL, "TenantId" varchar(26) NOT NULL,
          "MessageId" varchar(26) NOT NULL, "ConsumedUtc" timestamptz(6) NOT NULL,
          PRIMARY KEY ("ConsumerName","TenantId","MessageId"));
        CREATE FUNCTION public.animora_outbox_feed_position() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE last_position bigint;
        BEGIN
          IF NEW."MessageType" = 'feed.advanced' THEN
            -- Intake saves outbox only after the effect has captured its journal rows. Deferred
            -- journal events precede this event, and retain the feed-state lock until commit.
            SELECT "LastSequence" INTO STRICT last_position FROM public."TenantFeedState" WHERE "TenantId" = NEW."TenantId";
            UPDATE public."ServerOutbox" SET "PayloadJson" = jsonb_set("PayloadJson", '{lastSequence}', to_jsonb(last_position))
              WHERE "TenantId"=NEW."TenantId" AND "Id"=NEW."Id";
          END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER trg_ServerOutbox_feed_position AFTER INSERT ON public."ServerOutbox"
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.animora_outbox_feed_position();
        """);
    protected override void Down(MigrationBuilder mb) => mb.Sql("""
        DROP TABLE public."ConsumedMessages";
        DROP TABLE public."ServerOutbox";
        DROP FUNCTION public.animora_outbox_feed_position();
        """);
}
