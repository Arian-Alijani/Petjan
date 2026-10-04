using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.Validation;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Boarding;
using Animora.Domain.Identity;
using Animora.Domain.Inventory;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;

namespace Animora.Application.UseCases.Sales;

/// <summary>The two stores a service rate can live in.</summary>
public static class ServiceRateSources
{
    /// <summary>A <see cref="ProductKind.Service"/> product billed from visits and the cashier.</summary>
    public const string Clinic = "Clinic";

    /// <summary>A <see cref="BoardingServiceDef"/> add-on billed from a boarding stay.</summary>
    public const string Boarding = "Boarding";
}

/// <summary>One row of the «نرخ خدمات» catalogue; <paramref name="Billing"/> is set for boarding services only.</summary>
public sealed record ServiceRateReadModel(string Id, string Source, string Title, long PriceMinor, bool IsActive,
    string? Billing);

/// <summary>Every clinic and boarding service with its current rate (inactive ones included).</summary>
[RequiresPermission(PermissionCodes.SalesRead)]
public sealed record ServiceRatesQuery : IQuery<IReadOnlyList<ServiceRateReadModel>>;

public sealed class ServiceRatesQueryHandler(IServiceRateQueries queries)
    : IQueryHandler<ServiceRatesQuery, IReadOnlyList<ServiceRateReadModel>>
{
    public async Task<Result<IReadOnlyList<ServiceRateReadModel>>> HandleAsync(
        ServiceRatesQuery query, QueryContext context, CancellationToken ct) =>
        Result<IReadOnlyList<ServiceRateReadModel>>.Ok(await queries.GetAsync(ct));
}

internal static class ServiceRateRules
{
    public const int MaxTitleLength = 256;

    public static IReadOnlyList<ValidationFailure> Validate(string title, long priceMinor)
    {
        var failures = new List<ValidationFailure>();
        if (Rules.Required(title, nameof(title)) is { } required) failures.Add(required);
        if (Rules.MaxLength(title, MaxTitleLength, nameof(title)) is { } length) failures.Add(length);
        if (priceMinor < 0) failures.Add(new ValidationFailure(nameof(priceMinor), "PRICE_NEGATIVE"));
        return failures;
    }

    public static string Code(string prefix, int length) =>
        prefix + Guid.NewGuid().ToString("N")[..length].ToUpperInvariant();
}

/// <summary>Creates a clinic service (a stock-free <see cref="ProductKind.Service"/> product). Returns the product id.</summary>
[RequiresPermission(PermissionCodes.SettingsWrite)]
[Auditable(AuditCategory.Configuration, CommandTypes.SalesServiceRateCreate)]
public sealed record CreateServiceRateCommand(string Title, long PriceMinor) : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.SalesServiceRateCreate;
    public string AggregateTypeName => "Product";
    public string? AggregateIdOrNull => null;
}

public sealed class CreateServiceRateCommandValidator : IValidator<CreateServiceRateCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(CreateServiceRateCommand c, CancellationToken ct) =>
        ValueTask.FromResult(ServiceRateRules.Validate(c.Title, c.PriceMinor));
}

public sealed class CreateServiceRateCommandHandler(
    IProductRepository products, IServiceRateQueries queries, IClock clock)
    : ICommandHandler<CreateServiceRateCommand, string>
{
    private const string Unit = "PCS";

    public async Task<Result<string>> HandleAsync(CreateServiceRateCommand c, CommandContext context, CancellationToken ct)
    {
        var categoryId = await queries.FindServiceCategoryIdAsync(ct);
        if (categoryId is null)
            return Result<string>.Fail(ErrorCodes.CategoryNotFound, "No product category exists to hold the service.");

        var sku = ServiceRateRules.Code("SRV-", 8);
        var price = Money.Irt(c.PriceMinor);
        var variant = new ProductVariant(IdGenerator.NewId(clock.UtcNow), sku + "-1",
            ProductVariant.DefaultAttributeName, sku, Money.Irt(0), price, Quantity.Zero(Unit), null, true);
        var created = Product.Create(context.TenantId, sku, c.Title, categoryId, ProductKind.Service, Unit,
            Money.Irt(0), price, [variant], trackStock: false);
        if (created.IsFailure) return Result<string>.Fail(created.Error, created.Detail);

        products.Add(created.Value!);
        return Result<string>.Ok(created.Value!.Id);
    }
}

/// <summary>Creates a boarding add-on service with its billing basis. Returns the service id.</summary>
[RequiresPermission(PermissionCodes.SettingsWrite)]
[Auditable(AuditCategory.Configuration, CommandTypes.SalesServiceRateCreate)]
public sealed record CreateBoardingServiceRateCommand(string Title, long PriceMinor, BoardingServiceBilling Billing)
    : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.SalesServiceRateCreate;
    public string AggregateTypeName => "BoardingServiceDef";
    public string? AggregateIdOrNull => null;
}

public sealed class CreateBoardingServiceRateCommandValidator : IValidator<CreateBoardingServiceRateCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(CreateBoardingServiceRateCommand c, CancellationToken ct)
    {
        var failures = new List<ValidationFailure>(ServiceRateRules.Validate(c.Title, c.PriceMinor));
        if (Rules.ValidEnum(c.Billing, nameof(c.Billing)) is { } billing) failures.Add(billing);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

public sealed class CreateBoardingServiceRateCommandHandler(IBoardingServiceRepository services)
    : ICommandHandler<CreateBoardingServiceRateCommand, string>
{
    public Task<Result<string>> HandleAsync(CreateBoardingServiceRateCommand c, CommandContext context, CancellationToken ct)
    {
        var created = BoardingServiceDef.Create(context.TenantId, ServiceRateRules.Code("SVC-", 6), c.Title,
            Money.Irt(c.PriceMinor), c.Billing);
        services.Add(created);
        return Task.FromResult(Result<string>.Ok(created.Id));
    }
}

/// <summary>
/// Changes a service's title, rate and active flag. <paramref name="Source"/> selects the store
/// (<see cref="ServiceRateSources"/>); a boarding service keeps its billing basis.
/// </summary>
[RequiresPermission(PermissionCodes.SettingsWrite)]
[Auditable(AuditCategory.Configuration, CommandTypes.SalesServiceRateUpdate)]
public sealed record UpdateServiceRateCommand(string Id, string Source, string Title, long PriceMinor, bool IsActive)
    : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.SalesServiceRateUpdate;
    public string AggregateTypeName => Source == ServiceRateSources.Boarding ? "BoardingServiceDef" : "Product";
    public string? AggregateIdOrNull => Id;
}

public sealed class UpdateServiceRateCommandValidator : IValidator<UpdateServiceRateCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(UpdateServiceRateCommand c, CancellationToken ct)
    {
        var failures = new List<ValidationFailure>(ServiceRateRules.Validate(c.Title, c.PriceMinor));
        if (Rules.Required(c.Id, nameof(c.Id)) is { } id) failures.Add(id);
        if (c.Source is not (ServiceRateSources.Clinic or ServiceRateSources.Boarding))
            failures.Add(new ValidationFailure(nameof(c.Source), "SOURCE_INVALID"));
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

public sealed class UpdateServiceRateCommandHandler(IProductRepository products, IBoardingServiceRepository services)
    : ICommandHandler<UpdateServiceRateCommand, Unit>
{
    public Task<Result<Unit>> HandleAsync(UpdateServiceRateCommand c, CommandContext context, CancellationToken ct) =>
        c.Source == ServiceRateSources.Boarding ? UpdateBoardingAsync(c, ct) : UpdateClinicAsync(c, ct);

    private async Task<Result<Unit>> UpdateClinicAsync(UpdateServiceRateCommand c, CancellationToken ct)
    {
        var product = await products.FindAsync(c.Id, ct);
        if (product is null || product.Kind != ProductKind.Service) return Result<Unit>.Fail(ErrorCodes.ProductNotFound);

        var price = Money.Irt(c.PriceMinor);
        var described = product.Describe(c.Title, product.Brand, product.Notes);
        if (described.IsFailure) return Result<Unit>.Fail(described.Error, described.Detail);
        var priced = product.SetPrices(product.DefaultCostPrice, price);
        if (priced.IsFailure) return Result<Unit>.Fail(priced.Error, priced.Detail);

        foreach (var variant in product.Variants.ToArray())
        {
            var updated = product.UpdateVariant(variant with { SalePrice = price });
            if (updated.IsFailure) return Result<Unit>.Fail(updated.Error, updated.Detail);
        }

        var toggled = c.IsActive ? product.Activate() : product.Deactivate();
        return toggled.IsFailure ? Result<Unit>.Fail(toggled.Error, toggled.Detail) : Result<Unit>.Ok(Unit.Value);
    }

    private async Task<Result<Unit>> UpdateBoardingAsync(UpdateServiceRateCommand c, CancellationToken ct)
    {
        var service = await services.FindAsync(c.Id, ct);
        if (service is null) return Result<Unit>.Fail(ErrorCodes.NotFound, $"Boarding service '{c.Id}' not found.");

        var updated = service.Update(c.Title, Money.Irt(c.PriceMinor), service.Billing);
        if (updated.IsFailure) return Result<Unit>.Fail(updated.Error, updated.Detail);

        var toggled = c.IsActive ? service.Activate() : service.Deactivate();
        return toggled.IsFailure ? Result<Unit>.Fail(toggled.Error, toggled.Detail) : Result<Unit>.Ok(Unit.Value);
    }
}
