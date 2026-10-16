using System.Reflection;
using System.Text.Json;
using Animora.Application;
using Animora.Application.Abstractions;
using Animora.Application.Commands;
using Animora.Application.Dispatch;
using Animora.Application.Ports;
using Animora.Application.Validation;
using Animora.Application.UseCases.Visits;
using Animora.Application.UseCases.Visits.ReadModels;
using Animora.Application.UseCases.Vaccination;
using Animora.Domain.Clinical;
using Animora.Domain.Identity;
using Animora.Domain.Patients;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Scheduling;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;
using Animora.Contracts.Wire.Commands;
using Animora.Server.Commands;
using Animora.Server.Handlers;
using Animora.Server.Tests.Commands;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Server.Tests.HandlersS07;

/// <summary>Real Application handlers over fake ports, driven through the real S06 intake.</summary>
internal sealed class ClinicalHandlerFixture : IDisposable
{
    public IntakeTestFixture Intake { get; } = new();
    public FakeOwners Owners { get; } = new();
    public FakePets Pets { get; } = new();
    public FakeVisits Visits { get; } = new();
    public FakeQueueRepository Queue { get; } = new();
    public FakeAppointmentRepository Appointments { get; } = new();
    public FakeMemberships Memberships { get; } = new();
    public FixturePlans Plans { get; } = new();
    public FixtureVaccinations Vaccinations { get; } = new();
    public FixtureHistory History { get; } = new();
    public ServiceProvider Services { get; }
    public HandlerDispatcher Dispatcher { get; }
    public CommandPayloadCatalog Catalog { get; }
    public Owner Owner { get; }
    public Pet Pet { get; }
    public Visit Visit { get; }
    public ReceptionQueueEntry Ticket { get; }
    public Appointment Appointment { get; }
    public VaccinationRecord Vaccine { get; }
    public TreatmentPlanDraft Plan { get; }
    public string DoctorId { get; } = IdGenerator.NewId();
    public string LineId { get; } = IdGenerator.NewId();
    public string RadiologyId { get; } = IdGenerator.NewId();
    public string LabResultId { get; } = IdGenerator.NewId();

    public ClinicalHandlerFixture(Action<IServiceCollection>? configure = null)
    {
        var clock = Intake.Clock;
        Owner = Animora.Domain.Patients.Owner.Register(Intake.TenantId, "Owner", "09120000000", clock); Owners.Add(Owner);
        Pet = Animora.Domain.Patients.Pet.Register(Intake.TenantId, Owner.Id, "Fluffy", PetSpecies.Dog, clock); Pets.Add(Pet);
        Memberships.Add(StaffMembership.Create(Intake.TenantId, DoctorId, StaffRole.Doctor, clock));
        Ticket = ReceptionQueueEntry.Create(Intake.TenantId, Pet.Id, Owner.Id, QueueSource.WalkIn, 1, clock);
        Ticket.Call(DoctorId, clock); Queue.Add(Ticket);
        Visit = Animora.Domain.Clinical.Visit.Open(Intake.TenantId, Pet.Id, Owner.Id, DoctorId, VisitReason.Checkup, clock);
        Visit.Start(); Visit.SetAssessment("Healthy");
        Visit.AddServiceLine(new VisitServiceLine(LineId, null, "Exam", new Quantity(1, "PCS"), Money.Irt(100), VisitLineSource.Custom));
        Visit.AddRadiology(new RadiologyRecord(RadiologyId, clock.UtcNow, "Chest", null, []));
        Visit.AddLabResult(new LabResult(LabResultId, clock.UtcNow, "CBC", []));
        Visits.Add(Visit);
        Appointment = Animora.Domain.Scheduling.Appointment.Book(Intake.TenantId, Pet.Id, Owner.Id, "CHECKUP", clock.UtcNow,
            30, AppointmentSource.Phone, Intake.UserId, clock, DoctorId).Value!;
        Appointments.Add(Appointment);
        Vaccine = VaccinationRecord.Administer(Intake.TenantId, Pet.Id, "DHPP", "DHPP", 1,
            clock.UtcNow.AddDays(-21), DoctorId, clock, totalDoses: 3).Value!; Vaccinations.Add(Vaccine);
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        Plan = TreatmentPlan.Create(Intake.TenantId, Pet.Id, TreatmentItemType.Medication, TreatmentKind.HomeByOwner,
            "Medication", TreatmentSchedule.OnceOn(today), today, Intake.UserId, clock).Value!; Plans.Add(Plan);
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<IOwnerRepository>(Owners); services.AddSingleton<IPetRepository>(Pets);
        services.AddSingleton<IVisitRepository>(Visits); services.AddSingleton<IReceptionQueueRepository>(Queue);
        services.AddSingleton<IAppointmentRepository>(Appointments); services.AddSingleton<IStaffMembershipRepository>(Memberships);
        services.AddSingleton<ITreatmentPlanRepository>(Plans); services.AddSingleton<IVaccinationRepository>(Vaccinations);
        services.AddSingleton<IMedicalHistoryRepository>(History);
        services.AddSingleton<IVaccinationQueries>(new FixtureVaccinationQueries(Vaccinations, History));
        services.AddSingleton<IReceptionQueueQueries>(new FakeReceptionQueries());
        services.AddSingleton<ITriageQueries>(new FixtureTriage());
        services.AddSingleton<IVisitBillingHandoffWriter>(new FixtureHandoff());
        services.AddSingleton<IWeightEntryRepository>(new FixtureWeights());
        services.AddSingleton<IClinicTimeZoneProvider>(new FixtureZone());
        services.AddAnimoraApplication(ServerHandlerModuleS07.AddHandlers, ApplicationPipelineProfile.Server, registerQueries: false);
        configure?.Invoke(services);
        Services = services.BuildServiceProvider();
        Dispatcher = new HandlerDispatcher(Services);
        var catalog = new CommandPayloadCatalogBuilder(); ServerHandlerModuleS07.AddPayloads(catalog); Catalog = catalog.Build();
    }

    public object Command(Type type, bool reject)
    {
        var constructor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        if (type.Name == "AdmitTicketCommand")
            typeof(ReceptionQueueEntry).GetProperty(nameof(Ticket.State))!.SetValue(Ticket, QueueState.Waiting);
        if (type.Name == "EnterVisitCommand" && !reject)
            typeof(ReceptionQueueEntry).GetProperty(nameof(Ticket.VisitId))!.SetValue(Ticket, Visit.Id);
        if (reject && type.Name == "CreateWalkInCommand") return new Animora.Application.UseCases.Reception.CreateWalkInCommand("Pet", "Owner", "", DoctorId, false);
        return constructor.Invoke(constructor.GetParameters().Select(p => Argument(p, reject)).ToArray());
    }
    private object? Argument(ParameterInfo p, bool reject)
    {
        var missing = IdGenerator.NewId();
        return p.Name switch
        {
            "OwnerId" => reject ? missing : Owner.Id, "PetId" => reject ? missing : Pet.Id,
            "VisitId" => reject ? missing : Visit.Id, "DoctorId" => reject ? missing : DoctorId,
            "TicketId" => "wlk:" + (reject ? missing : Ticket.Id),
            "RecordId" => reject ? missing : Vaccine.Id, "PlanId" => reject ? missing : Plan.Plan.Id,
            "OccurrenceId" => reject ? missing : Plan.Occurrences[0].Id,
            "RxLineId" => reject ? missing : LineId,
            "RadiologyId" => reject ? missing : RadiologyId, "LabResultId" => reject ? missing : LabResultId,
            "BodyPart" => reject ? "" : "Chest",
            "Phone" or "PhonePrimary" or "OwnerPhone" => "09120000000",
            "PatientKind" => "Dog", "TypeId" => "CHECKUP",
            "PatientName" or "OwnerName" or "FullName" or "Name" or "Title" => reject ? "" : "Fluffy",
            "Code" => reject ? "" : "CUSTOM", "Field" => VisitNoteField.Diagnosis,
            "Schedule" => TreatmentSchedule.OnceOn(DateOnly.FromDateTime(Intake.Clock.UtcNow.UtcDateTime)),
            "TotalDoses" => 1, "BoosterDays" => 365, "Quantity" => 1,
            "WeightKg" => (decimal?)5m, "MeasuredUtc" => Intake.Clock.UtcNow,
            _ => DefaultArgument(p)
        };
    }
    private static object? DefaultArgument(ParameterInfo p)
    {
        if (p.HasDefaultValue) return p.DefaultValue;
        var type = Nullable.GetUnderlyingType(p.ParameterType) ?? p.ParameterType;
        if (p.ParameterType == typeof(string)) return "value";
        if (Nullable.GetUnderlyingType(p.ParameterType) is not null) return null;
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
    public CommandEnvelope Envelope(object command)
    {
        var journaled = (IJournaledCommand)command;
        var type = command.GetType();
        var parameters = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First().GetParameters();
        var values = parameters.ToDictionary(p => p.Name!, p => type.GetProperty(p.Name!, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)!.GetValue(command));
        var json = JsonSerializer.Serialize(values, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return CommandEnvelope.Create(IdGenerator.NewId(), Intake.TenantId, Intake.UserId, Intake.DeviceId,
            journaled.CommandTypeName, journaled.AggregateTypeName, journaled.AggregateIdOrNull, json,
            Animora.Application.Commands.CommandFingerprint.Compute(journaled.CommandTypeName, json), Intake.Clock.UtcNow, 1);
    }
    public void Dispose() => Services.Dispose();
}

internal sealed class HandlerDispatcher(IServiceProvider services) : IDispatcher
{
    public int Calls { get; private set; }
    public async Task<Result<T>> SendAsync<T>(ICommand<T> command, CommandContext context, CancellationToken ct = default)
    {
        Calls++;
        var validatorType = typeof(IValidator<>).MakeGenericType(command.GetType());
        foreach (var validator in services.GetServices(validatorType))
        {
            var pending = (ValueTask<IReadOnlyList<ValidationFailure>>)validatorType.GetMethod("ValidateAsync")!.Invoke(validator, [command, ct])!;
            if ((await pending).Count != 0) return Result<T>.Fail(ErrorCodes.ValidationFailed);
        }
        var registry = services.GetRequiredService<HandlerRegistry>();
        if (!registry.TryGet(command.GetType(), out var entry)) return Result<T>.Fail(ErrorCodes.HandlerNotRegistered);
        var handler = services.GetRequiredService(entry.HandlerType);
        var contract = typeof(ICommandHandler<,>).MakeGenericType(command.GetType(), typeof(T));
        return await (Task<Result<T>>)contract.GetMethod("HandleAsync")!.Invoke(handler, [command, context, ct])!;
    }
    public Task<Result<T>> QueryAsync<T>(IQuery<T> query, QueryContext context, CancellationToken ct = default) =>
        Task.FromResult(Result<T>.Fail(ErrorCodes.HandlerNotRegistered));
}

internal sealed class FixturePlans : ITreatmentPlanRepository
{
    public List<TreatmentPlan> Plans { get; } = [];
    public List<TreatmentOccurrence> Occurrences { get; } = [];
    public List<VaccineCatalogItem> Catalog { get; } = [];
    public Task<TreatmentPlan?> FindPlanAsync(string id, CancellationToken ct = default) => Task.FromResult(Plans.FirstOrDefault(p => p.Id == id));
    public Task<TreatmentOccurrence?> FindOccurrenceAsync(string id, CancellationToken ct = default) => Task.FromResult(Occurrences.FirstOrDefault(p => p.Id == id));
    public Task<IReadOnlyList<TreatmentOccurrence>> ListPendingOccurrencesAsync(string id, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TreatmentOccurrence>>(Occurrences.Where(p => p.PlanId == id && p.State == TreatmentOccurrenceState.Pending).ToArray());
    public Task<int> CountPendingExceptAsync(string id, string except, CancellationToken ct = default) => Task.FromResult(Occurrences.Count(p => p.PlanId == id && p.Id != except && p.State == TreatmentOccurrenceState.Pending));
    public void Add(TreatmentPlanDraft draft) { Plans.Add(draft.Plan); Occurrences.AddRange(draft.Occurrences); }
    public Task<bool> CatalogCodeExistsAsync(string code, CancellationToken ct = default) => Task.FromResult(Catalog.Any(p => p.Code == code));
    public void AddCatalogItem(VaccineCatalogItem item) => Catalog.Add(item);
}
internal sealed class FixtureWeights : IWeightEntryRepository { public void Add(WeightEntry entry) { } }
internal sealed class FixtureZone : IClinicTimeZoneProvider { public TimeZoneInfo GetClinicTimeZone() => TimeZoneInfo.Utc; }
internal sealed class FixtureHistory : IMedicalHistoryRepository
{
    public List<MedicalHistoryEntry> Rows { get; } = [];
    public void Add(MedicalHistoryEntry entry) => Rows.Add(entry);
}
internal sealed class FixtureHandoff : IVisitBillingHandoffWriter
{
    public void Add(string id, string tenantId, string visitId, string ownerId, string petId, string doctorId, DateTimeOffset completedUtc, string linesJson) { }
}
internal sealed class FixtureVaccinations : IVaccinationRepository
{
    public List<VaccinationRecord> Rows { get; } = [];
    public Task<VaccinationRecord?> FindAsync(string id, CancellationToken ct = default) => Task.FromResult(Rows.FirstOrDefault(p => p.Id == id));
    public async Task<VaccinationRecord> RequireAsync(string id, CancellationToken ct = default) => await FindAsync(id, ct) ?? throw new InvalidOperationException();
    public void Add(VaccinationRecord row) => Rows.Add(row);
    public void AddRange(IEnumerable<VaccinationRecord> rows) => Rows.AddRange(rows);
}
internal sealed class FixtureTriage : ITriageQueries
{
    public Task<IReadOnlyList<DoctorRailReadModel>> GetDoctorRailAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct) => Task.FromResult<IReadOnlyList<DoctorRailReadModel>>([]);
    public Task<IReadOnlyList<BookedAppointmentReadModel>> GetBookedForDoctorAsync(string id, DateTimeOffset start, DateTimeOffset end, CancellationToken ct) => Task.FromResult<IReadOnlyList<BookedAppointmentReadModel>>([]);
    public Task<IReadOnlyList<TriagePatientReadModel>> GetRecentPatientsAsync(string? search, CancellationToken ct) => Task.FromResult<IReadOnlyList<TriagePatientReadModel>>([]);
}
