using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Animora.Persistence.PostgreSql.Migrations.Server;

/// <summary>
/// Adds the soft-delete audit columns (who + when) to Owners and Pets so the delete actor/instant replicate to peers.
/// Nullable → additive; existing rows default to NULL.
/// </summary>
[DbContext(typeof(AnimoraServerDbContext))]
[Migration("20261023100000_123_DeleteActorAudit")]
public sealed class _123_DeleteActorAudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE public."Owners" ADD COLUMN "DeletedByUserId" text NULL;
        ALTER TABLE public."Owners" ADD COLUMN "DeletedUtc" timestamp with time zone NULL;
        ALTER TABLE public."Pets" ADD COLUMN "DeletedByUserId" text NULL;
        ALTER TABLE public."Pets" ADD COLUMN "DeletedUtc" timestamp with time zone NULL;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE public."Pets" DROP COLUMN "DeletedUtc";
        ALTER TABLE public."Pets" DROP COLUMN "DeletedByUserId";
        ALTER TABLE public."Owners" DROP COLUMN "DeletedUtc";
        ALTER TABLE public."Owners" DROP COLUMN "DeletedByUserId";
        """);
}
