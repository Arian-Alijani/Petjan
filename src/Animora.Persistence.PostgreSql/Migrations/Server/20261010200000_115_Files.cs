using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace Animora.Persistence.PostgreSql.Migrations.Server;

// Authored without invoking EF tooling because migrations-add would build the project.
[DbContext(typeof(AnimoraServerDbContext))]
[Migration("20261010200000_115_Files")]
public sealed class _115_Files : Migration
{
    protected override void BuildTargetModel(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder) =>
        Files115TargetModel.BuildFrozenModel(modelBuilder);
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE public."FileObjects" (
            "TenantId" varchar(26) NOT NULL, "Id" varchar(26) NOT NULL, "Sha256" char(64),
            "SizeBytes" bigint NOT NULL, "DeclaredSizeBytes" bigint NOT NULL,
            "ContentType" varchar(128) NOT NULL, "OriginalName" varchar(256) NOT NULL,
            "State" smallint NOT NULL, "CreatedUtc" timestamptz(6) NOT NULL, "UpdatedUtc" timestamptz(6) NOT NULL,
            "CreatedByUserId" varchar(26) NOT NULL, "UploadId" varchar(26),
            CONSTRAINT "PK_FileObjects" PRIMARY KEY ("TenantId", "Id"));
        CREATE INDEX "IX_FileObjects_TenantId_State_UpdatedUtc" ON public."FileObjects" ("TenantId","State","UpdatedUtc");
        CREATE INDEX "IX_FileObjects_TenantId_Sha256" ON public."FileObjects" ("TenantId","Sha256");
        CREATE UNIQUE INDEX "IX_FileObjects_TenantId_UploadId" ON public."FileObjects" ("TenantId","UploadId");
        CREATE TABLE public."FileUploadChunks" (
            "TenantId" varchar(26) NOT NULL, "UploadId" varchar(26) NOT NULL,
            "Offset" bigint NOT NULL, "Length" integer NOT NULL,
            CONSTRAINT "PK_FileUploadChunks" PRIMARY KEY ("TenantId","UploadId","Offset"));
        CREATE TABLE public."BlobRefs" (
            "Sha256" char(64) PRIMARY KEY, "RefCount" integer NOT NULL, "SizeBytes" bigint NOT NULL,
            "FirstSeenUtc" timestamptz(6) NOT NULL, "UnreferencedUtc" timestamptz(6));
        """);
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("FileUploadChunks", "public");
        migrationBuilder.DropTable("FileObjects", "public");
        migrationBuilder.DropTable("BlobRefs", "public");
    }
}
