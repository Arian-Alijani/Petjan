using Microsoft.EntityFrameworkCore.Migrations;
namespace Animora.Persistence.PostgreSql.Migrations.Server;

public partial class _112_Subscription : Migration
{
    protected override void Up(MigrationBuilder mb) => mb.Sql("""
        CREATE TABLE public."Plans" (
          "Id" varchar(26) PRIMARY KEY, "Code" varchar(32) NOT NULL UNIQUE, "Name" varchar(100) NOT NULL,
          "DefaultSeatLimit" integer NOT NULL CHECK ("DefaultSeatLimit">0), "MonthlyAmountMinor" bigint NOT NULL CHECK ("MonthlyAmountMinor">=0),
          "Currency" varchar(3) NOT NULL, "TrialDays" integer NOT NULL CHECK ("TrialDays">=0), "IsActive" boolean NOT NULL,
          "CreatedUtc" timestamptz(6) NOT NULL);
        CREATE TABLE public."Subscriptions" (
          "Id" varchar(26) PRIMARY KEY, "TenantId" varchar(26) NOT NULL UNIQUE, "PlanId" varchar(26) NOT NULL REFERENCES public."Plans"("Id"),
          "Status" smallint NOT NULL, "SeatLimit" integer NOT NULL CHECK ("SeatLimit">0), "CurrentPeriodStartUtc" timestamptz(6) NOT NULL,
          "CurrentPeriodEndUtc" timestamptz(6) NOT NULL, "GraceEndsUtc" timestamptz(6), "CancelRequestedUtc" timestamptz(6),
          "CancelAtPeriodEnd" boolean NOT NULL, "NextPeriodSequence" integer NOT NULL, "NextEventSequence" integer NOT NULL,
          "ConcurrencyStamp" uuid NOT NULL, "CreatedUtc" timestamptz(6) NOT NULL, "UpdatedUtc" timestamptz(6) NOT NULL,
          CHECK ("Status" BETWEEN 0 AND 5), CHECK ("CurrentPeriodEndUtc">="CurrentPeriodStartUtc"));
        CREATE INDEX "IX_Subscriptions_PlanId" ON public."Subscriptions"("PlanId");
        CREATE INDEX "IX_Subscriptions_Status_CurrentPeriodEndUtc" ON public."Subscriptions"("Status","CurrentPeriodEndUtc");
        CREATE TABLE public."BillingPeriods" (
          "SubscriptionId" varchar(26) NOT NULL REFERENCES public."Subscriptions"("Id"), "Sequence" integer NOT NULL,
          "StartUtc" timestamptz(6) NOT NULL, "EndUtc" timestamptz(6) NOT NULL,
          "AmountMinor" bigint NOT NULL, "Currency" varchar(3) NOT NULL, PRIMARY KEY("SubscriptionId","Sequence"),
          CHECK ("Sequence">0 AND "AmountMinor">=0 AND "EndUtc">"StartUtc"));
        CREATE TABLE public."SubscriptionEvents" (
          "SubscriptionId" varchar(26) NOT NULL REFERENCES public."Subscriptions"("Id"), "Sequence" integer NOT NULL,
          "Type" varchar(48) NOT NULL, "OccurredUtc" timestamptz(6) NOT NULL, "ActorKind" smallint NOT NULL,
          "ActorId" varchar(26), "PayloadJson" jsonb NOT NULL, PRIMARY KEY("SubscriptionId","Sequence"), CHECK("Sequence">0));
        CREATE TABLE public."OperatorAccounts" (
          "Id" varchar(26) PRIMARY KEY, "UserName" varchar(64) NOT NULL, "NormalizedUserName" varchar(64) NOT NULL UNIQUE,
          "PasswordHash" text NOT NULL, "SecurityStamp" varchar(26) NOT NULL, "Status" smallint NOT NULL,
          "FailedAttempts" integer NOT NULL, "LockoutEndUtc" timestamptz(6), "MustChangePassword" boolean NOT NULL,
          "CreatedUtc" timestamptz(6) NOT NULL, "LastLoginUtc" timestamptz(6));
        CREATE TABLE public."OperatorAudit" (
          "Id" varchar(26) PRIMARY KEY, "OccurredUtc" timestamptz(6) NOT NULL, "OperatorId" varchar(26), "ActorKind" smallint NOT NULL,
          "Action" varchar(64) NOT NULL, "TargetTenantId" varchar(26), "Outcome" varchar(16) NOT NULL,
          "CorrelationId" varchar(64) NOT NULL, "DetailJson" jsonb NOT NULL);
        CREATE TABLE public."ProvisioningLedger" (
          "IdempotencyKey" varchar(128) PRIMARY KEY, "RequestFingerprint" character(64) NOT NULL,
          "TenantId" varchar(26) NOT NULL, "SubscriptionId" varchar(26) NOT NULL, "ActorKind" smallint NOT NULL, "CreatedUtc" timestamptz(6) NOT NULL);
        CREATE FUNCTION public.animora_subscription_history_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'Subscription/operator history is append-only'; END $$;
        CREATE TRIGGER "trg_SubscriptionEvents_immutable" BEFORE UPDATE OR DELETE ON public."SubscriptionEvents"
          FOR EACH ROW EXECUTE FUNCTION public.animora_subscription_history_immutable();
        CREATE TRIGGER "trg_OperatorAudit_immutable" BEFORE UPDATE OR DELETE ON public."OperatorAudit"
          FOR EACH ROW EXECUTE FUNCTION public.animora_subscription_history_immutable();
        INSERT INTO public."Plans" ("Id","Code","Name","DefaultSeatLimit","MonthlyAmountMinor","Currency","TrialDays","IsActive","CreatedUtc") VALUES
          ('01K75C0RK00000000000000001','trial','Trial',3,0,'IRT',14,true,'2026-10-10T00:00:00Z'),
          ('01K75C0RK00000000000000002','standard','Standard',5,0,'IRT',0,true,'2026-10-10T00:00:00Z');
        """);
    protected override void Down(MigrationBuilder mb) => mb.Sql("""
        DROP TABLE public."ProvisioningLedger", public."OperatorAudit", public."OperatorAccounts", public."SubscriptionEvents", public."BillingPeriods";
        DROP TABLE public."Subscriptions", public."Plans";
        DROP FUNCTION public.animora_subscription_history_immutable();
        """);
}
