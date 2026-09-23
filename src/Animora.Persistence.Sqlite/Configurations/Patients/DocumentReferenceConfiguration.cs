using Animora.Domain.Patients;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Patients;

public sealed class DocumentReferenceConfiguration : IEntityTypeConfiguration<DocumentReference>
{
    public void Configure(EntityTypeBuilder<DocumentReference> builder)
    {
        builder.HasKey(e => e.Id);
    }
}
