using Animora.Application.Abstractions;
using Animora.Application.Dispatch;
using Animora.Application.UseCases.Reception.ReadModels;

namespace Animora.Application.UseCases.Reception;

/// <summary>Registers the C02 reception use cases (2 queries + 6 journaled commands + 1 validator).</summary>
public static class ReceptionRegistration
{
    public static ApplicationRegistrationBuilder AddReceptionUseCases(this ApplicationRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddQuery<ReceptionQueueQuery, IReadOnlyList<QueueTicketReadModel>, ReceptionQueueQueryHandler>()
            .AddQuery<ReceptionDoctorsQuery, IReadOnlyList<ReceptionDoctorReadModel>, ReceptionDoctorsQueryHandler>()
            .AddCommand<AdmitTicketCommand, Unit, AdmitTicketCommandHandler>()
            .AddCommand<EnterVisitCommand, Unit, EnterVisitCommandHandler>()
            .AddCommand<LeaveQueueCommand, Unit, LeaveQueueCommandHandler>()
            .AddCommand<MarkNoShowCommand, Unit, MarkNoShowCommandHandler>()
            .AddCommand<ToggleTicketUrgentCommand, Unit, ToggleTicketUrgentCommandHandler>()
            .AddCommand<CreateWalkInCommand, string, CreateWalkInCommandHandler>()
            .AddValidator<CreateWalkInCommand, CreateWalkInCommandValidator>();
    }
}
