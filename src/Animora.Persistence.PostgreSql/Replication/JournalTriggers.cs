using Microsoft.EntityFrameworkCore.Migrations;
namespace Animora.Persistence.PostgreSql.Replication;

public static class JournalTriggers
{
    public static void Apply(MigrationBuilder mb, SyncEntityDescriptor d)
    {
        mb.Sql(JournalSqlTemplates.CaptureTrigger(d, d.Table, "Id", false));
        foreach (var child in d.Children)
            mb.Sql(JournalSqlTemplates.CaptureTrigger(d, child.Table, child.RootIdColumn, true));
    }

    public static void Drop(MigrationBuilder mb, SyncEntityDescriptor d)
    {
        foreach (var table in d.ChildTables.Append(d.Table))
            mb.Sql($"DROP TRIGGER IF EXISTS {JournalSqlTemplates.Quote("trg_" + table + "_journal")} ON public.{JournalSqlTemplates.Quote(table)};");
    }
}
