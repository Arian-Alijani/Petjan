using System.Reflection;
using System.Text.Json;
using Animora.Application;
using Animora.Application.Dispatch;
using Animora.Application.Abstractions;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.UseCases.Patients;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Patients;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.DependencyInjection;
using Animora.Persistence.PostgreSql.Repositories;
using Animora.Server.Commands;
using Animora.Server.Handlers;
using Animora.Server.Tests.Commands;
using Animora.Server.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Server.Tests.HandlersS07;

public sealed class S07MigrationTests(PgFixture pg) : IClassFixture<PgFixture>
{
    [PgFact]
    public async Task Applies_OnTopOfPreviousHead()
    {
        await using var db = TestSupport.CreateContext(new FixedTenantContext(IdGenerator.NewId()), pg.ConnectionString);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261009160000_104_CommandIntake");
        await migrator.MigrateAsync("20261009170000_105_ClinicalCore");
        Assert.Contains("20261009170000_105_ClinicalCore", await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Set<Owner>().AsNoTracking().ToListAsync());
    }
}

public sealed class S07EndToEndTests(PgFixture pg) : IClassFixture<PgFixture>
{
    [PgFact]
    public async Task CommandsRoundTrip_ThroughIntake_WithTenantIsolation()
    {
        var first = new IntakeTestFixture(); var second = new IntakeTestFixture();
        var firstId = await CreateOwnerAsync(first);
        var secondId = await CreateOwnerAsync(second);
        await using var one = TestSupport.CreateContext(new FixedTenantContext(first.TenantId), pg.ConnectionString);
        await using var two = TestSupport.CreateContext(new FixedTenantContext(second.TenantId), pg.ConnectionString);
        Assert.NotNull(await new OwnerRepository(one).FindAsync(firstId));
        Assert.Null(await new OwnerRepository(one).FindAsync(secondId));
        Assert.Null(await new OwnerRepository(two).FindAsync(firstId));
        Assert.NotNull(await new OwnerRepository(two).FindAsync(secondId));
    }
    private async Task<string> CreateOwnerAsync(IntakeTestFixture f)
    {
        await using var db = TestSupport.CreateContext(new FixedTenantContext(f.TenantId), pg.ConnectionString);
        await db.MigrateAndSeedAsync(f.TenantId);
        var services = new ServiceCollection(); services.AddSingleton(db);
        services.AddSingleton<Animora.Domain.Time.IClock>(f.Clock);
        services.AddAnimoraClinicalPersistence();
        services.AddAnimoraApplication(ServerHandlerModuleS07.AddHandlers, ApplicationPipelineProfile.Server, registerQueries: false);
        using var provider = services.BuildServiceProvider();
        var builder = new CommandPayloadCatalogBuilder(); ServerHandlerModuleS07.AddPayloads(builder);
        var command = new CreateOwnerCommand("Owner", "09120000000", OwnerGender.Unspecified, null, null, null,
            null, null, null, null, null, null, null, null, false);
        var json = JsonSerializer.Serialize(new { command.FullName, command.PhonePrimary, command.Gender,
            command.PhoneSecondary, command.NationalId, command.Address, command.PetName, command.PetSpecies,
            command.PetBreed, command.PetColor, command.PetGender, command.PetBirthUtc, command.PetMicrochip,
            command.PetMicrochipUtc, command.PetNeutered }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var envelope = CommandEnvelope.Create(IdGenerator.NewId(), f.TenantId, f.UserId, f.DeviceId,
            command.CommandTypeName, "Owner", null, json, CommandFingerprint.Compute(command.CommandTypeName, json), f.Clock.UtcNow, 1);
        var dispatcher = new HandlerDispatcher(provider);
        var service = f.CreateService(builder.Build(), dispatcher, new PgFixtureUnitOfWork(db));
        var result = await service.SubmitAsync(envelope);
        Assert.Equal(SettlementOutcome.Accepted, result.Value!.Outcome);
        Assert.Equal(result.Value, (await service.SubmitAsync(envelope)).Value);
        Assert.Equal(1, dispatcher.Calls);
        return result.Value.ServerEntityId!;
    }
}

/// <summary>PG test adapter: production repositories/save semantics, fake intake receipt store.</summary>
internal sealed class PgFixtureUnitOfWork(AnimoraServerDbContext db) : IUnitOfWork
{
    private int _depth;
    public async Task<Result> SaveAsync(CancellationToken ct = default)
    { await db.SaveChangesAsync(ct); return Result.Ok(); }
    public async Task<Result<T>> ExecuteAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken ct = default)
    {
        if (_depth > 0) return await work(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            _depth++;
            var result = await work(ct);
            if (result.IsSuccess) await tx.CommitAsync(ct); else await tx.RollbackAsync(ct);
            return result;
        }
        finally { _depth--; db.ChangeTracker.Clear(); }
    }
}
