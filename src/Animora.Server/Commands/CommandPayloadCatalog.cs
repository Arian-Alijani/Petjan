using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;
using Animora.Application.Abstractions;
using Animora.Application.Commands;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Results;

namespace Animora.Server.Commands;

internal sealed record CommandPayloadCandidate(Type CommandRecord, string AggregateType,
    IReadOnlySet<string> PropertyNames,
    Func<object, IDispatcher, CommandContext, CancellationToken, Task<CommandDispatchResult>> Dispatch);
internal sealed record ResolvedCommandPayload(object Command, CommandPayloadCandidate Candidate);

/// <summary>Immutable, explicit catalog. No handler assembly scanning or domain handlers in S06.</summary>
internal sealed class CommandPayloadCatalog(IReadOnlyDictionary<string, IReadOnlyList<CommandPayloadCandidate>> entries)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public IReadOnlyDictionary<string, IReadOnlyList<CommandPayloadCandidate>> Entries { get; } = entries;

    public Result<ResolvedCommandPayload> Resolve(CommandEnvelope envelope)
    {
        if (ReservedCommandTypes.All.Contains(envelope.CommandType) || !Entries.TryGetValue(envelope.CommandType, out var candidates))
            return Result<ResolvedCommandPayload>.Fail(ErrorCodes.HandlerNotRegistered);
        try
        {
            using JsonDocument document = JsonDocument.Parse(envelope.PayloadJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return Result<ResolvedCommandPayload>.Fail(ErrorCodes.ValidationFailed, "payload shape");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
                if (!names.Add(property.Name)) return Result<ResolvedCommandPayload>.Fail(ErrorCodes.ValidationFailed, "payload shape");
            var matches = candidates.Where(candidate => candidate.AggregateType == envelope.AggregateType
                && names.SetEquals(candidate.PropertyNames)).ToArray();
            if (matches.Length != 1)
                return Result<ResolvedCommandPayload>.Fail(ErrorCodes.ValidationFailed, "payload shape");
            object? command = JsonSerializer.Deserialize(envelope.PayloadJson, matches[0].CommandRecord, JsonOptions);
            if (command is not IJournaledCommand journaled || journaled.CommandTypeName != envelope.CommandType
                || journaled.AggregateTypeName != envelope.AggregateType || journaled.AggregateIdOrNull != envelope.AggregateId)
                return Result<ResolvedCommandPayload>.Fail(ErrorCodes.ValidationFailed, "payload identity");
            return Result<ResolvedCommandPayload>.Ok(new ResolvedCommandPayload(command, matches[0]));
        }
        catch (JsonException)
        {
            return Result<ResolvedCommandPayload>.Fail(ErrorCodes.ValidationFailed, "payload shape");
        }
    }
}

internal sealed class CommandPayloadCatalogBuilder
{
    private readonly Dictionary<string, List<CommandPayloadCandidate>> _entries = new(StringComparer.Ordinal);

    public CommandPayloadCatalogBuilder Add<TCommand>(string commandType, string aggregateType)
        where TCommand : IJournaledCommand
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandType);
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateType);
        Type type = typeof(TCommand);
        Type[] contracts = type.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>)).ToArray();
        if (contracts.Length != 1 || type.IsAbstract || ReservedCommandTypes.All.Contains(commandType))
            throw new InvalidOperationException($"Invalid catalog command {type.Name}.");
        ConstructorInfo constructor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var names = constructor.GetParameters().Select(p => p.Name!).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        if (!_entries.TryGetValue(commandType, out var candidates)) _entries.Add(commandType, candidates = []);
        if (candidates.Any(c => c.AggregateType == aggregateType && names.SetEquals(c.PropertyNames)))
            throw new InvalidOperationException($"Ambiguous payload shape for {commandType}/{aggregateType}.");
        var dispatch = (Func<object, IDispatcher, CommandContext, CancellationToken, Task<CommandDispatchResult>>)
            typeof(CommandPayloadCatalogBuilder).GetMethod(nameof(CreateDispatcher), BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(type, contracts[0].GetGenericArguments()[0]).Invoke(null, null)!;
        candidates.Add(new CommandPayloadCandidate(type, aggregateType, names, dispatch));
        return this;
    }

    public CommandPayloadCatalog Build() => new(_entries.ToFrozenDictionary(e => e.Key,
        e => (IReadOnlyList<CommandPayloadCandidate>)Array.AsReadOnly(e.Value.ToArray()), StringComparer.Ordinal));

    private static Func<object, IDispatcher, CommandContext, CancellationToken, Task<CommandDispatchResult>> CreateDispatcher<TCommand, TResult>()
        where TCommand : ICommand<TResult> => async (command, dispatcher, context, ct) =>
        {
            Result<TResult> result = await dispatcher.SendAsync((TCommand)command, context, ct).ConfigureAwait(false);
            FinancialEffect effect = result.Value is IFinancialEffectReporter reporter ? reporter.FinancialEffect
                : command is IFinancialEffectReporter commandReporter ? commandReporter.FinancialEffect : FinancialEffect.None;
            return new CommandDispatchResult(result.ToResult(), result.Value, effect);
        };
}
