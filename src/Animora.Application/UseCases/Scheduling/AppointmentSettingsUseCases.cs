using Animora.Application.Abstractions;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.Validation;
using Animora.Contracts.Appointments;
using Animora.Domain.Identity;
using Animora.Domain.Results;

namespace Animora.Application.UseCases.Scheduling;

// A17: appointment settings are clinic-wide configuration. Reads need appointment visibility (the calendar cannot
// render types / day-only mode without them); every write needs SettingsWrite, enforced by the dispatcher's
// authorization stage so a direct Contracts call is gated exactly like the settings tab. They are preferences, not
// replicated business intent: not journaled, and self-transacted because no database row is written.

[RequiresPermission(PermissionCodes.AppointmentsRead)]
public sealed record GetAppointmentSettingsQuery : IQuery<AppointmentSettings>;

public sealed class GetAppointmentSettingsQueryHandler(IAppointmentSettingsStore store)
    : IQueryHandler<GetAppointmentSettingsQuery, AppointmentSettings>
{
    public async Task<Result<AppointmentSettings>> HandleAsync(GetAppointmentSettingsQuery q, QueryContext context,
        CancellationToken ct) => Result<AppointmentSettings>.Ok(await store.GetAsync(ct).ConfigureAwait(false));
}

[RequiresPermission(PermissionCodes.SettingsWrite)]
public sealed record SaveAppointmentSettingsCommand(AppointmentSettings Settings)
    : ICommand<Unit>, ISelfTransactedCommand;

public sealed class SaveAppointmentSettingsCommandValidator : IValidator<SaveAppointmentSettingsCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(SaveAppointmentSettingsCommand c, CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(
            c.Settings is null ? [new ValidationFailure(nameof(c.Settings), "required")] : []);
}

public sealed class SaveAppointmentSettingsCommandHandler(IAppointmentSettingsStore store)
    : ICommandHandler<SaveAppointmentSettingsCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(SaveAppointmentSettingsCommand c, CommandContext context, CancellationToken ct)
    {
        await store.SaveAsync(c.Settings, ct).ConfigureAwait(false);
        return Result<Unit>.Ok(Unit.Value);
    }
}

[RequiresPermission(PermissionCodes.SettingsWrite)]
public sealed record AddAppointmentTypeCommand(string Label, int DurationMinutes, bool RequiresDoctor)
    : ICommand<CustomAppointmentType>, ISelfTransactedCommand;

public sealed class AddAppointmentTypeCommandValidator : IValidator<AddAppointmentTypeCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(AddAppointmentTypeCommand c, CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(
            Rules.Required(c.Label, nameof(c.Label)) is { } error ? [error] : []);
}

public sealed class AddAppointmentTypeCommandHandler(IAppointmentSettingsStore store)
    : ICommandHandler<AddAppointmentTypeCommand, CustomAppointmentType>
{
    public async Task<Result<CustomAppointmentType>> HandleAsync(AddAppointmentTypeCommand c, CommandContext context,
        CancellationToken ct) => Result<CustomAppointmentType>.Ok(
            await store.AddTypeAsync(c.Label, c.DurationMinutes, c.RequiresDoctor, ct).ConfigureAwait(false));
}

[RequiresPermission(PermissionCodes.SettingsWrite)]
public sealed record RemoveAppointmentTypeCommand(string TypeId) : ICommand<Unit>, ISelfTransactedCommand;

public sealed class RemoveAppointmentTypeCommandValidator : IValidator<RemoveAppointmentTypeCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(RemoveAppointmentTypeCommand c, CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(
            Rules.Required(c.TypeId, nameof(c.TypeId)) is { } error ? [error] : []);
}

public sealed class RemoveAppointmentTypeCommandHandler(IAppointmentSettingsStore store)
    : ICommandHandler<RemoveAppointmentTypeCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RemoveAppointmentTypeCommand c, CommandContext context, CancellationToken ct)
    {
        await store.RemoveTypeAsync(c.TypeId, ct).ConfigureAwait(false);
        return Result<Unit>.Ok(Unit.Value);
    }
}

[RequiresPermission(PermissionCodes.SettingsWrite)]
public sealed record SetCalendarModeCommand(CalendarMode Mode) : ICommand<Unit>, ISelfTransactedCommand;

public sealed class SetCalendarModeCommandHandler(IAppointmentSettingsStore store)
    : ICommandHandler<SetCalendarModeCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(SetCalendarModeCommand c, CommandContext context, CancellationToken ct)
    {
        await store.SetCalendarModeAsync(c.Mode, ct).ConfigureAwait(false);
        return Result<Unit>.Ok(Unit.Value);
    }
}

[RequiresPermission(PermissionCodes.SettingsWrite)]
public sealed record SetAppointmentSmsCommand(AppointmentSmsSettings Sms) : ICommand<Unit>, ISelfTransactedCommand;

public sealed class SetAppointmentSmsCommandValidator : IValidator<SetAppointmentSmsCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(SetAppointmentSmsCommand c, CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(
            c.Sms is null ? [new ValidationFailure(nameof(c.Sms), "required")] : []);
}

public sealed class SetAppointmentSmsCommandHandler(IAppointmentSettingsStore store)
    : ICommandHandler<SetAppointmentSmsCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(SetAppointmentSmsCommand c, CommandContext context, CancellationToken ct)
    {
        await store.SetSmsAsync(c.Sms, ct).ConfigureAwait(false);
        return Result<Unit>.Ok(Unit.Value);
    }
}
