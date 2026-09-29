using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Domain.Primitives;
using Animora.Domain.Results;

namespace Animora.Application.UseCases.Profile;

/// <summary>
/// C10: stages one preference row (insert or last-writer-wins update) inside the caller's transaction. The
/// local store implements it; the Application layer owns the key catalog and the decision.
/// </summary>
public interface IProfilePreferenceWriter
{
    Task UpsertAsync(string tenantId, string userId, string key, string valueJson, CancellationToken ct);
}

/// <summary>
/// C10: persists the editable profile fields (phone, e-mail) as UI preferences.
/// <para>
/// Deliberately <b>not</b> an <c>IJournaledCommand</c> and with no <c>CommandTypes</c> entry: ARCHITECTURE §8.5
/// makes UI preferences the one data category eligible for generic last-writer-wins replication, so this
/// carries no replicated business intent. Permission is authentication only — a user edits their own profile.
/// </para>
/// </summary>
public sealed record SaveUserProfileCommand(IReadOnlyDictionary<string, string> Values) : ICommand<Unit>
{
    /// <summary>Replaces <see cref="Values"/> with every key of <paramref name="keys"/>; null means unchanged.</summary>
    public static SaveUserProfileCommand For(string key, string? value) =>
        new(new Dictionary<string, string>(StringComparer.Ordinal) { [key] = value ?? string.Empty });
}

public sealed class SaveUserProfileCommandHandler(
    IProfilePreferenceWriter writer,
    IUnitOfWork unitOfWork)
    : ICommandHandler<SaveUserProfileCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(
        SaveUserProfileCommand command, CommandContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        foreach (var key in command.Values.Keys)
        {
            if (!AppSettingsKeys.IsKnown(key))
                return Result<Unit>.Fail(ErrorCodes.SettingKeyUnknown, $"Unknown preference key '{key}'.");
        }

        foreach (var (key, value) in command.Values)
            await writer.UpsertAsync(context.TenantId, context.UserId, key, value ?? string.Empty, ct)
                .ConfigureAwait(false);

        var saved = await unitOfWork.SaveAsync(ct).ConfigureAwait(false);
        return saved.IsFailure ? Result<Unit>.Fail(saved.Error, saved.Detail) : Result<Unit>.Ok(Unit.Value);
    }
}

/// <summary>
/// C10: persists the whole settings section as preference rows. Not journaled (see
/// <see cref="SaveUserProfileCommand"/>); a key outside <see cref="AppSettingsKeys.SettingsKeys"/> fails with
/// <c>SETTING_KEY_UNKNOWN</c> before anything is written.
/// </summary>
public sealed record SaveAppSettingsCommand(IReadOnlyDictionary<string, string> Values) : ICommand<Unit>;

public sealed class SaveAppSettingsCommandHandler(
    IProfilePreferenceWriter writer,
    IUnitOfWork unitOfWork)
    : ICommandHandler<SaveAppSettingsCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(
        SaveAppSettingsCommand command, CommandContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        foreach (var key in command.Values.Keys)
        {
            if (!AppSettingsKeys.IsSettingKey(key))
                return Result<Unit>.Fail(ErrorCodes.SettingKeyUnknown, $"Unknown settings key '{key}'.");
        }

        foreach (var (key, value) in command.Values)
            await writer.UpsertAsync(context.TenantId, context.UserId, key, value ?? string.Empty, ct)
                .ConfigureAwait(false);

        var saved = await unitOfWork.SaveAsync(ct).ConfigureAwait(false);
        return saved.IsFailure ? Result<Unit>.Fail(saved.Error, saved.Detail) : Result<Unit>.Ok(Unit.Value);
    }
}
