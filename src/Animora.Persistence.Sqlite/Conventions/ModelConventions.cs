using System.Reflection;
using Animora.Domain.Primitives;
using Animora.Domain.ValueObjects;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Conventions;

public static class ModelConventions
{
    private static readonly HashSet<string> LocalTypes = new(StringComparer.Ordinal);
    public static IReadOnlySet<string> LocalOnlyEntityTypes => LocalTypes;

    public static void Apply(ModelBuilder modelBuilder, AnimoraLocalDbContext context)
    {
        // Only roots are processed here: owned rows get the same scalar rules via OwnedRows.ApplyOwnedScalarConventions.
        foreach (var type in modelBuilder.Model.GetEntityTypes().Where(t => !t.IsOwned()).ToArray())
        {
            var clr = type.ClrType;
            var table = Pluralize(clr.Name);
            var entity = modelBuilder.Entity(clr);
            entity.ToTable(table);
            if (typeof(AggregateRoot).IsAssignableFrom(clr))
                entity.Ignore(nameof(AggregateRoot.DomainEvents));
            if (clr.IsDefined(typeof(LocalOnlyAttribute), false)) LocalTypes.Add(clr.Name);
            else if (typeof(SyncableEntity).IsAssignableFrom(clr))
            {
                typeof(ModelConventions).GetMethod(nameof(ConfigureFilters), BindingFlags.Static | BindingFlags.NonPublic)!
                    .MakeGenericMethod(clr).Invoke(null, [modelBuilder, context, table]);
            }
            foreach (var property in type.GetProperties().ToArray())
                ApplyScalar(entity.Property(property.ClrType, property.Name), property.ClrType, property.Name, property.IsNullable);
            // Complex value objects are stored as exact INTEGER + TEXT, not SQLite NUMERIC. Configs may pre-map
            // a value explicitly (e.g. summable fixed-scale quantity); those are left untouched here.
            foreach (var property in clr.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!property.CanWrite || type.FindComplexProperty(property.Name) is not null) continue; // computed values are not stored
                if (property.PropertyType == typeof(Money))
                    entity.ComplexProperty<Money>(property.Name, value => OwnedRows.ConfigureMoney(value, property.Name));
                else if (property.PropertyType == typeof(Quantity))
                    entity.ComplexProperty<Quantity>(property.Name, value => OwnedRows.ConfigureQuantity(value, property.Name, summable: false));
            }
        }
    }

    /// <summary>Scalar storage rules shared by roots and owned rows: bounded TEXT, int enums, UTC ticks, exact decimal TEXT.</summary>
    internal static void ApplyScalar(PropertyBuilder builder, Type clrType, string name, bool isNullable)
    {
        if (clrType == typeof(string))
        {
            var id = name == "Id" || name == "TenantId" || name.EndsWith("Id", StringComparison.Ordinal);
            var length = id ? FieldLengths.Id : name.Contains("Phone", StringComparison.Ordinal) ? FieldLengths.Phone
                : name == "NationalId" ? FieldLengths.NationalId : name.EndsWith("Currency", StringComparison.Ordinal) ? FieldLengths.Currency
                : name.EndsWith("Code", StringComparison.Ordinal) ? FieldLengths.Code
                : name.EndsWith("Name", StringComparison.Ordinal) ? FieldLengths.Name
                : name.EndsWith("Title", StringComparison.Ordinal) ? FieldLengths.Title
                : name.EndsWith("Note", StringComparison.Ordinal) || name.EndsWith("Notes", StringComparison.Ordinal)
                  || name.EndsWith("Memo", StringComparison.Ordinal) || name.EndsWith("Description", StringComparison.Ordinal)
                  ? FieldLengths.LongText : FieldLengths.ShortText;
            builder.HasColumnType("TEXT").HasMaxLength(length);
            if (id && !isNullable) builder.IsRequired();
        }
        else if (clrType.IsEnum) builder.HasConversion(typeof(int));
        else if (clrType == typeof(DateTimeOffset)) builder.HasConversion(new UtcTicksConverter()).HasColumnType("INTEGER");
        else if (clrType == typeof(DateTimeOffset?)) builder.HasConversion(new NullableUtcTicksConverter()).HasColumnType("INTEGER");
        else if (clrType == typeof(decimal)) builder.HasConversion(new DecimalStringConverter()).HasColumnType("TEXT");
    }

    private static void ConfigureFilters<TEntity>(ModelBuilder modelBuilder, AnimoraLocalDbContext context, string table)
        where TEntity : SyncableEntity
    {
        var entity = modelBuilder.Entity<TEntity>();
        entity.HasQueryFilter("Tenant", e => e.TenantId == context.CurrentTenantId);
        entity.HasQueryFilter("SoftDelete", e => !e.Deleted);
        entity.HasIndex(e => new { e.TenantId, e.UpdatedAt }).HasDatabaseName($"IX_{table}_TenantId_UpdatedAt");
    }

    private static string Pluralize(string name) => name switch
    {
        "MedicalHistoryEntry" => "MedicalHistoryEntries",
        "ReceptionQueueEntry" => "ReceptionQueueEntries",
        "ReplicaBase" => "repl_base",
        "ReplicaStaging" => "repl_staging",
        "ReplicaCheckpoint" => "repl_checkpoint",
        _ when name.EndsWith('y') && !"aeiou".Contains(char.ToLowerInvariant(name[^2])) => name[..^1] + "ies",
        _ when name.EndsWith('s') => name + "es",
        _ => name + "s"
    };
}
