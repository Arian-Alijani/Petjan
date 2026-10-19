namespace Animora.Persistence.PostgreSql.Replication;

/// <summary>Capture is transactional; the deferred feed-state lock is the commit serialization point.</summary>
public static class JournalSqlTemplates
{
    public const string CaptureFunction = """
        CREATE FUNCTION public.animora_journal_capture() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE image jsonb; root_image jsonb; tenant text; root_id text; operation char(1); token uuid;
        BEGIN
          image := CASE WHEN TG_OP = 'DELETE' THEN to_jsonb(OLD) ELSE to_jsonb(NEW) END;
          root_id := image ->> TG_ARGV[2];
          operation := CASE WHEN TG_OP = 'DELETE' AND TG_ARGV[3] = 'root' THEN 'D' ELSE 'U' END;
          IF TG_ARGV[3] = 'child' THEN
            -- A child-only write must also rotate the aggregate's concurrency token. On a
            -- cascading root deletion the root is already absent: its own trigger emits D.
            EXECUTE format('UPDATE public.%I SET "Version" = gen_random_uuid() WHERE "Id" = $1 RETURNING to_jsonb(%I.*)',
                           TG_ARGV[1], TG_ARGV[1]) INTO root_image USING root_id;
            IF root_image IS NULL THEN RETURN NULL; END IF;
            image := root_image;
          END IF;
          tenant := image ->> 'TenantId';
          token := CASE WHEN operation = 'D' THEN NULL ELSE (image ->> 'Version')::uuid END;
          -- Provisioning inserts Tenants before its explicit feed-state row. Bootstrap only
          -- that new tenant; ordinary writes never acquire a feed-state lock during capture.
          IF TG_ARGV[0] = 'Tenant' AND TG_OP = 'INSERT' THEN
            INSERT INTO public."TenantFeedState" ("TenantId") VALUES (tenant) ON CONFLICT DO NOTHING;
          END IF;
          INSERT INTO public."TenantJournal" ("TenantId", "Entity", "EntityId", "Op", "Version", "Epoch", "CapturedUtc")
            SELECT tenant, TG_ARGV[0], root_id, operation, token, "Epoch", clock_timestamp()
            FROM public."TenantFeedState" WHERE "TenantId" = tenant;
          IF NOT FOUND THEN RAISE EXCEPTION 'Missing tenant feed state'; END IF;
          -- Moving a child between aggregates invalidates the previous root as well.
          IF TG_ARGV[3] = 'child' AND TG_OP = 'UPDATE' AND (to_jsonb(OLD) ->> TG_ARGV[2]) IS DISTINCT FROM root_id THEN
            EXECUTE format('UPDATE public.%I SET "Version" = gen_random_uuid() WHERE "Id" = $1', TG_ARGV[1])
              USING to_jsonb(OLD) ->> TG_ARGV[2];
          END IF;
          RETURN NULL;
        END $$;
        """;

    public const string AssignFunction = """
        CREATE FUNCTION public.animora_journal_assign_sequence() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE assigned bigint; current_epoch bigint;
        BEGIN
          IF pg_trigger_depth() <> 1 THEN RETURN NULL; END IF;
          IF NOT EXISTS (SELECT 1 FROM public."TenantJournal" WHERE "TenantId" = NEW."TenantId" AND "Id" = NEW."Id" AND "ChangeSequence" IS NULL) THEN
            RETURN NULL;
          END IF;
          -- All captures have run before deferred events fire. Skip older pending events,
          -- preserving the final operation for each root without consuming a sequence.
          IF EXISTS (SELECT 1 FROM public."TenantJournal" WHERE "TenantId" = NEW."TenantId"
              AND "Entity" = NEW."Entity" AND "EntityId" = NEW."EntityId"
              AND "ChangeSequence" IS NULL AND "Id" > NEW."Id") THEN
            DELETE FROM public."TenantJournal" WHERE "TenantId" = NEW."TenantId" AND "Id" = NEW."Id";
            RETURN NULL;
          END IF;
          UPDATE public."TenantFeedState" SET "LastSequence" = "LastSequence" + 1, "UpdatedUtc" = clock_timestamp()
            WHERE "TenantId" = NEW."TenantId" RETURNING "LastSequence", "Epoch" INTO assigned, current_epoch;
          IF NOT FOUND THEN RAISE EXCEPTION 'Missing tenant feed state'; END IF;
          UPDATE public."TenantJournal" SET "ChangeSequence" = assigned, "Epoch" = current_epoch
            WHERE "TenantId" = NEW."TenantId" AND "Id" = NEW."Id";
          RETURN NULL;
        END $$;
        """;

    public const string DeferredTrigger = """
        CREATE CONSTRAINT TRIGGER trg_TenantJournal_assign
        AFTER INSERT ON public."TenantJournal" DEFERRABLE INITIALLY DEFERRED
        FOR EACH ROW EXECUTE FUNCTION public.animora_journal_assign_sequence();
        -- Runtime-role TRUNCATE must be revoked for all governed tables by the G09 grants.
        """;

    public static string CaptureTrigger(SyncEntityDescriptor d, string table, string rootColumn, bool child) =>
        $"CREATE TRIGGER {Quote("trg_" + table + "_journal")} AFTER INSERT OR UPDATE OR DELETE ON public.{Quote(table)} " +
        $"FOR EACH ROW EXECUTE FUNCTION public.animora_journal_capture('{d.Name}', '{d.Table}', '{rootColumn}', '{(child ? "child" : "root")}');";

    /// <summary>
    /// Immediate for direct SQL by default; EF defers these within one atomic SaveChanges scope.
    /// Keep CaptureTrigger unchanged so historical migration 109 retains its original SQL.
    /// </summary>
    public static string DeferrableChildCaptureTrigger(SyncEntityDescriptor descriptor, SyncChildDescriptor child) =>
        $"CREATE CONSTRAINT TRIGGER {Quote("trg_" + child.Table + "_journal")} " +
        $"AFTER INSERT OR UPDATE OR DELETE ON public.{Quote(child.Table)} DEFERRABLE INITIALLY IMMEDIATE " +
        $"FOR EACH ROW EXECUTE FUNCTION public.animora_journal_capture('{descriptor.Name}', '{descriptor.Table}', '{child.RootIdColumn}', 'child');";

    internal static string Quote(string identifier)
    {
        if (string.IsNullOrEmpty(identifier) || !identifier.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            throw new ArgumentException("Invalid SQL identifier.", nameof(identifier));
        return "\"" + identifier + "\"";
    }
}
