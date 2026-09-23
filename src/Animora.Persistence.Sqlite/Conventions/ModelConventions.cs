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
        // Only roots are processed here: JSON-owned children have their own shape and table name.
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
            {
                var builder = entity.Property(property.ClrType, property.Name);
                var name = property.Name;
                if (property.ClrType == typeof(string))
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
                    if (id && !property.IsNullable) builder.IsRequired();
                }
                else if (property.ClrType.IsEnum) builder.HasConversion(typeof(int));
                else if (property.ClrType == typeof(DateTimeOffset)) builder.HasConversion(new UtcTicksConverter()).HasColumnType("INTEGER");
                else if (property.ClrType == typeof(DateTimeOffset?)) builder.HasConversion(new NullableUtcTicksConverter()).HasColumnType("INTEGER");
                else if (property.ClrType == typeof(decimal)) builder.HasConversion(new DecimalStringConverter()).HasColumnType("TEXT");
            }
            // Complex value objects are stored as exact INTEGER + TEXT, not SQLite NUMERIC.
            foreach (var property in clr.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.PropertyType == typeof(Money))
                    entity.ComplexProperty<Money>(property.Name, value =>
                    {
                        value.Property(v => v.Minor).HasColumnName(property.Name + "Minor").HasColumnType("INTEGER");
                        value.Property(v => v.Currency).HasColumnName(property.Name + "Currency").HasColumnType("TEXT").HasMaxLength(FieldLengths.Currency);
                        value.Ignore(v => v.Scale);
                        value.Ignore(v => v.IsZero);
                        value.Ignore(v => v.IsNegative);
                    });
                else if (property.PropertyType == typeof(Quantity))
                    entity.ComplexProperty<Quantity>(property.Name, value =>
                    {
                        value.Property(v => v.Value).HasColumnName(property.Name + "Value").HasConversion(new DecimalStringConverter()).HasColumnType("TEXT");
                        value.Property(v => v.UnitCode).HasColumnName(property.Name + "Unit").HasColumnType("TEXT").HasMaxLength(FieldLengths.UnitCode);
                        value.Ignore(v => v.IsZero);
                        value.Ignore(v => v.IsNegative);
                    });
            }
        }
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
        _ when name.EndsWith('y') && !"aeiou".Contains(char.ToLowerInvariant(name[^2])) => name[..^1] + "ies",
        _ when name.EndsWith('s') => name + "es",
        _ => name + "s"
    };
}
