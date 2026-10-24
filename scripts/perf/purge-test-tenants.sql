-- Controlled purge of the two TEST tenants (royan untouched).
-- The append-only guards are disabled only inside this transaction and restored before COMMIT,
-- so either the whole purge applies or nothing does.
BEGIN;

ALTER TABLE public."StockMovements"             DISABLE TRIGGER trg_stockmovements_immutable;
ALTER TABLE public."GoodsReceipts"              DISABLE TRIGGER trg_goodsreceipts_immutable;
ALTER TABLE public."JournalEntries"             DISABLE TRIGGER trg_journalentries_immutable;
ALTER TABLE public."RecurringExpensePayments"   DISABLE TRIGGER trg_recurringexpensepayments_immutable;
ALTER TABLE public."GoodsReceiptLines"          DISABLE TRIGGER trg_goodsreceiptlines_immutable;
ALTER TABLE public."JournalLines"               DISABLE TRIGGER trg_journallines_immutable;
ALTER TABLE public."OperatorAudit"              DISABLE TRIGGER "trg_OperatorAudit_immutable";
ALTER TABLE public."SecurityAudit"              DISABLE TRIGGER trg_securityaudit_immutable;
ALTER TABLE public."ChequeEvents"               DISABLE TRIGGER trg_chequeevents_immutable;
ALTER TABLE public."FeedBatch"                  DISABLE TRIGGER trg_feedbatch_immutable;
ALTER TABLE public."FeedBatchPage"              DISABLE TRIGGER trg_feedbatchpage_immutable;
ALTER TABLE public."SubscriptionEvents"         DISABLE TRIGGER "trg_SubscriptionEvents_immutable";

DO $$
DECLARE
  v_tenant  text;
  r         record;
  v_pass    int;
  v_deleted bigint;
  v_total   bigint;
BEGIN
  FOREACH v_tenant IN ARRAY ARRAY[
      '01M5BYHW2ZCHVRQZ133P0EXXBA',   -- animora-loadtest
      '01M59QQH9SARMD5GEFZWG38249'    -- test-clinic
  ]
  LOOP
    IF NOT EXISTS (SELECT 1 FROM public."Tenants" WHERE "Id" = v_tenant) THEN
      RAISE NOTICE 'tenant % not found - skipped', v_tenant;
      CONTINUE;
    END IF;

    v_pass := 0;
    LOOP
      v_pass  := v_pass + 1;
      v_total := 0;
      FOR r IN
        SELECT c.table_name
        FROM information_schema.columns c
        JOIN information_schema.tables t
          ON t.table_schema = c.table_schema AND t.table_name = c.table_name
        WHERE c.table_schema = 'public'
          AND c.column_name = 'TenantId'
          AND t.table_type = 'BASE TABLE'
          AND c.table_name NOT IN ('Tenants', 'TenantFeedState', 'TenantJournal')
      LOOP
        BEGIN
          EXECUTE format('DELETE FROM public.%I WHERE %I = $1', r.table_name, 'TenantId') USING v_tenant;
          GET DIAGNOSTICS v_deleted = ROW_COUNT;
          v_total := v_total + v_deleted;
        EXCEPTION WHEN OTHERS THEN
          RAISE NOTICE 'skip %.%: %', v_tenant, r.table_name, SQLERRM;
        END;
      END LOOP;
      EXIT WHEN v_total = 0 OR v_pass > 20;
    END LOOP;

    DELETE FROM public."BillingPeriods"
     WHERE "SubscriptionId" IN (SELECT "Id" FROM public."Subscriptions" WHERE "TenantId" = v_tenant);
    DELETE FROM public."SubscriptionEvents"
     WHERE "SubscriptionId" IN (SELECT "Id" FROM public."Subscriptions" WHERE "TenantId" = v_tenant);
    DELETE FROM public."LicenseGrants" WHERE "TenantId" = v_tenant;
    DELETE FROM public."Subscriptions" WHERE "TenantId" = v_tenant;

    INSERT INTO public."TenantFeedState" ("TenantId") VALUES (v_tenant) ON CONFLICT DO NOTHING;
    DELETE FROM public."Tenants" WHERE "Id" = v_tenant;
    DELETE FROM public."TenantJournal"   WHERE "TenantId" = v_tenant;
    DELETE FROM public."TenantFeedState" WHERE "TenantId" = v_tenant;

    RAISE NOTICE 'tenant % purged (passes=%)', v_tenant, v_pass;
  END LOOP;
END $$;

ALTER TABLE public."StockMovements"             ENABLE TRIGGER trg_stockmovements_immutable;
ALTER TABLE public."GoodsReceipts"              ENABLE TRIGGER trg_goodsreceipts_immutable;
ALTER TABLE public."JournalEntries"             ENABLE TRIGGER trg_journalentries_immutable;
ALTER TABLE public."RecurringExpensePayments"   ENABLE TRIGGER trg_recurringexpensepayments_immutable;
ALTER TABLE public."GoodsReceiptLines"          ENABLE TRIGGER trg_goodsreceiptlines_immutable;
ALTER TABLE public."JournalLines"               ENABLE TRIGGER trg_journallines_immutable;
ALTER TABLE public."OperatorAudit"              ENABLE TRIGGER "trg_OperatorAudit_immutable";
ALTER TABLE public."SecurityAudit"              ENABLE TRIGGER trg_securityaudit_immutable;
ALTER TABLE public."ChequeEvents"               ENABLE TRIGGER trg_chequeevents_immutable;
ALTER TABLE public."FeedBatch"                  ENABLE TRIGGER trg_feedbatch_immutable;
ALTER TABLE public."FeedBatchPage"              ENABLE TRIGGER trg_feedbatchpage_immutable;
ALTER TABLE public."SubscriptionEvents"         ENABLE TRIGGER "trg_SubscriptionEvents_immutable";

COMMIT;
