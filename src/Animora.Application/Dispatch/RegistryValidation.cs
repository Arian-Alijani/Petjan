namespace Animora.Application.Dispatch;

/// <summary>
/// Startup completeness check: a gap is a startup failure, never a runtime surprise. Fails when a request
/// type has more than one handler, a handler type is abstract, a required command/query has no handler (or
/// one of the wrong kind), or a validator is registered for a type nothing handles. <c>TResult</c> agreement
/// between request and handler is enforced at compile time by the <c>Add*</c> generic constraints.
/// </summary>
public static class RegistryValidation
{
    public static void Validate(ApplicationRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var errors = new List<string>();
        var kinds = new Dictionary<Type, RequestKind>();

        foreach (var r in builder.Handlers)
        {
            if (!kinds.TryAdd(r.RequestType, r.Kind))
                errors.Add($"{r.RequestType.FullName}: more than one handler registered.");
            if (r.HandlerType.IsAbstract || r.HandlerType.IsInterface)
                errors.Add($"{r.RequestType.FullName}: handler {r.HandlerType.FullName} is not a concrete type.");
        }

        foreach (var (request, kind) in builder.Required)
        {
            if (!kinds.TryGetValue(request, out var actual))
                errors.Add($"{request.FullName}: required {kind} has no handler.");
            else if (actual != kind)
                errors.Add($"{request.FullName}: required as {kind} but registered as {actual}.");
        }

        foreach (var validated in builder.ValidatedTypes)
            if (!kinds.ContainsKey(validated))
                errors.Add($"{validated.FullName}: validator registered but no handler.");

        if (errors.Count > 0)
            throw new InvalidOperationException(
                "Application handler registry is incomplete (HANDLER_NOT_REGISTERED): " + string.Join(" | ", errors));
    }
}
