using System.Collections.Frozen;
using System.Reflection;
using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Security;
using Animora.Application.Validation;
using Animora.Domain.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Animora.Application.Dispatch;

/// <summary>
/// Explicit, one-call-per-handler registration (no assembly scanning). Each <c>Add*</c> records a generic
/// factory; <see cref="Build"/> composes every entry's behavior chain once and freezes the map.
/// </summary>
public sealed class ApplicationRegistrationBuilder
{
    private readonly IServiceCollection _services;
    private readonly List<Registration> _handlers = [];
    private readonly HashSet<Type> _validated = [];
    private readonly List<(Type Request, RequestKind Kind)> _required = [];

    internal ApplicationRegistrationBuilder(IServiceCollection services) => _services = services;

    internal IReadOnlyList<Registration> Handlers => _handlers;

    internal IReadOnlyCollection<Type> ValidatedTypes => _validated;

    internal IReadOnlyList<(Type Request, RequestKind Kind)> Required => _required;

    /// <summary>Registers <typeparamref name="THandler"/> (scoped) as the one handler of <typeparamref name="TCommand"/>.</summary>
    public ApplicationRegistrationBuilder AddCommand<TCommand, TResult, THandler>()
        where TCommand : ICommand<TResult>
        where THandler : class, ICommandHandler<TCommand, TResult>
    {
        _services.TryAddScoped<THandler>();
        _handlers.Add(new Registration(typeof(TCommand), typeof(THandler), RequestKind.Command, Compose));
        return this;

        HandlerEntry Compose(IReadOnlyList<IPipelineBehavior> chain, bool hasValidators)
        {
            PipelineStep<TResult> terminal = static (sp, request, context, ct) =>
                sp.GetRequiredService<THandler>().HandleAsync((TCommand)request, (CommandContext)context, ct);
            return BuildEntry<TCommand, TResult>(terminal, typeof(THandler), RequestKind.Command, chain, hasValidators);
        }
    }

    /// <summary>Registers <typeparamref name="THandler"/> (scoped) as the one handler of <typeparamref name="TQuery"/>.</summary>
    public ApplicationRegistrationBuilder AddQuery<TQuery, TResult, THandler>()
        where TQuery : IQuery<TResult>
        where THandler : class, IQueryHandler<TQuery, TResult>
    {
        _services.TryAddScoped<THandler>();
        _handlers.Add(new Registration(typeof(TQuery), typeof(THandler), RequestKind.Query, Compose));
        return this;

        HandlerEntry Compose(IReadOnlyList<IPipelineBehavior> chain, bool hasValidators)
        {
            PipelineStep<TResult> terminal = static (sp, request, context, ct) =>
                sp.GetRequiredService<THandler>().HandleAsync((TQuery)request, (QueryContext)context, ct);
            return BuildEntry<TQuery, TResult>(terminal, typeof(THandler), RequestKind.Query, chain, hasValidators);
        }
    }

    /// <summary>Registers a validator (scoped) for <typeparamref name="T"/>; several per type are allowed.</summary>
    public ApplicationRegistrationBuilder AddValidator<T, TValidator>()
        where TValidator : class, IValidator<T>
    {
        _services.TryAddEnumerable(ServiceDescriptor.Scoped<IValidator<T>, TValidator>());
        _validated.Add(typeof(T));
        return this;
    }

    /// <summary>Declares that <typeparamref name="TCommand"/> must have a handler; startup fails otherwise.</summary>
    public ApplicationRegistrationBuilder RequireCommand<TCommand, TResult>()
        where TCommand : ICommand<TResult>
    {
        _required.Add((typeof(TCommand), RequestKind.Command));
        return this;
    }

    /// <summary>Declares that <typeparamref name="TQuery"/> must have a handler; startup fails otherwise.</summary>
    public ApplicationRegistrationBuilder RequireQuery<TQuery, TResult>()
        where TQuery : IQuery<TResult>
    {
        _required.Add((typeof(TQuery), RequestKind.Query));
        return this;
    }

    /// <summary>Validates completeness, composes every chain once and freezes the registry.</summary>
    internal HandlerRegistry Build(IReadOnlyList<IPipelineBehavior> chain)
    {
        RegistryValidation.Validate(this);
        var map = new Dictionary<Type, HandlerEntry>(_handlers.Count);
        foreach (var registration in _handlers)
            map.Add(registration.RequestType, registration.Compose(chain, _validated.Contains(registration.RequestType)));
        return new HandlerRegistry(map.ToFrozenDictionary());
    }

    private static HandlerEntry BuildEntry<TRequest, TResult>(PipelineStep<TResult> terminal, Type handlerType,
        RequestKind kind, IReadOnlyList<IPipelineBehavior> chain, bool hasValidators)
        where TRequest : notnull
    {
        var permissionCode = ResolvePermissionCode(typeof(TRequest));
        var audit = ResolveAuditBinding(typeof(TRequest));
        var descriptor = new PipelineDescriptor(typeof(TRequest), typeof(TResult), kind, hasValidators, permissionCode, audit);
        var step = terminal;
        for (var i = chain.Count - 1; i >= 0; i--) step = chain[i].Wrap<TRequest, TResult>(step, descriptor);

        var pipeline = step;
        Func<IServiceProvider, object, object, CancellationToken, Task<object>> invoker = async (sp, request, context, ct) =>
            await pipeline(sp, request, context, ct).ConfigureAwait(false);
        return new HandlerEntry(typeof(TRequest), typeof(TResult), handlerType, kind, pipeline, invoker, permissionCode, audit);
    }

    /// <summary>
    /// Reads <see cref="RequiresPermissionAttribute"/> once, at composition (never on the hot path). A code outside
    /// <see cref="PermissionCodes"/> fails startup; no attribute → <c>null</c> (authenticated-only, not public).
    /// </summary>
    private static string? ResolvePermissionCode(Type requestType)
    {
        var attribute = requestType.GetCustomAttribute<RequiresPermissionAttribute>();
        if (attribute is null) return null;
        if (string.IsNullOrEmpty(attribute.PermissionCode) || !PermissionCodes.Exists(attribute.PermissionCode))
            throw new InvalidOperationException(
                $"{requestType.Name} declares unknown permission code '{attribute.PermissionCode}'.");
        return attribute.PermissionCode;
    }

    /// <summary>Reads and validates audit metadata once while the frozen pipeline is composed.</summary>
    private static AuditBinding? ResolveAuditBinding(Type requestType)
    {
        var attribute = requestType.GetCustomAttribute<AuditableAttribute>();
        if (attribute is null) return null;
        var separator = attribute.Action.IndexOf('.', StringComparison.Ordinal);
        if (separator <= 0 || separator == attribute.Action.Length - 1)
            throw new InvalidOperationException(
                $"{requestType.Name} declares invalid audit action '{attribute.Action}'.");
        return new AuditBinding(attribute.Category, attribute.Action);
    }

    internal sealed record Registration(
        Type RequestType,
        Type HandlerType,
        RequestKind Kind,
        Func<IReadOnlyList<IPipelineBehavior>, bool, HandlerEntry> Compose);
}
