using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Inventory;
using Animora.Contracts.Inventory;
using Animora.Domain.Inventory;
using Animora.Domain.Time;
using Microsoft.Extensions.DependencyInjection;
using ContractProduct = Animora.Contracts.Inventory.Product;
using ContractVariant = Animora.Contracts.Inventory.ProductVariant;
using DomainProductVariant = Animora.Domain.Inventory.ProductVariant;

namespace Animora.Client.Services.Inventory;

/// <summary>
/// The real <see cref="IInventoryQueryService"/> over the C06 use cases (mirrors the C01
/// <c>PatientsService</c> pattern). Singleton; the scoped dispatcher and identity provider are resolved
/// from a fresh async scope per call and never captured. No EF or SQL here.
/// </summary>
public sealed class InventoryQueryService(IServiceScopeFactory scopes) : IInventoryQueryService
{
    private const int MaxPageSize = 200; // QueryPaging.HardMaxPageSize

    public async Task<ProductPage> GetProductsAsync(ProductListQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider, _) = Resolve(scope);
        var context = provider.BuildQueryContext();
        if (context.IsFailure) return new ProductPage([], 0);

        var search = string.IsNullOrWhiteSpace(query.Search) ? null : Animora.Application.Text.PersianText.NormalizeDigits(query.Search.Trim());
        var category = string.IsNullOrWhiteSpace(query.CategoryId) ? null : query.CategoryId;
        var size = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var result = await dispatcher.QueryAsync(
            new ProductCatalogQuery(search, category, query.Stock == ProductStockFilter.Low,
                Math.Max(1, query.Page), size, query.Stock == ProductStockFilter.Ok), context.Value!);
        if (result.IsFailure) throw new ServiceException(result.Error);

        var rows = result.Value!.Items;
        var products = new List<ContractProduct>(rows.Count);
        for (var i = 0; i < rows.Count; i++) products.Add(rows[i].ToContract());

        return new ProductPage(products, result.Value.TotalCount);
    }

    public async Task<IReadOnlyList<CategoryInfo>> GetCategoriesAsync()
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider, _) = Resolve(scope);
        var context = provider.BuildQueryContext();
        if (context.IsFailure) return [];

        var result = await dispatcher.QueryAsync(new ProductCategoriesQuery(), context.Value!);
        if (result.IsFailure) return [];

        var rows = result.Value!;
        var categories = new List<CategoryInfo>(rows.Count);
        for (var i = 0; i < rows.Count; i++) categories.Add(rows[i].ToContract());
        return categories;
    }

    public async Task<ProductDetail?> GetDetailAsync(string productId)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider, zones) = Resolve(scope);
        var context = provider.BuildQueryContext();
        if (context.IsFailure) return null;

        var result = await dispatcher.QueryAsync(new ProductDetailQuery(productId), context.Value!);
        return result.IsSuccess && result.Value is { } row ? row.ToContract(zones.GetClinicTimeZone()) : null;
    }

    public async Task SaveProductInfoAsync(string productId, ProductInfoUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        // A multi-variant product must keep its dimension even when the caller did not re-send the name:
        // fall back to the dimension already stored on the product (P2-13).
        var existingName = string.IsNullOrWhiteSpace(update.AttributeName) && update.Variants.Count > 1
            ? (await GetDetailAsync(productId))?.Product.Attribute?.Name
            : null;
        var attributeName = AttributeNameOf(update.AttributeName, update.Variants, existingName);
        var variants = ToInputs(update.Variants, attributeName);
        _ = await SendAsync(new UpdateProductInfoCommand(productId, update.Name, Lowest(update.Variants, cost: true),
            Lowest(update.Variants, cost: false), variants, update.Brand, update.Description,
            CategoryId: update.CategoryId, Sku: update.Code));
    }

    public async Task<MovementPage> GetHistoryAsync(string productId, int page, int pageSize)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider, zones) = Resolve(scope);
        var context = provider.BuildQueryContext();
        if (context.IsFailure) return new MovementPage([], 0);

        var result = await dispatcher.QueryAsync(
            new StockMovementHistoryQuery(productId, Math.Max(1, page), Math.Clamp(pageSize, 1, MaxPageSize)),
            context.Value!);
        if (result.IsFailure) throw new ServiceException(result.Error);

        var zone = zones.GetClinicTimeZone();
        var rows = result.Value!.Items;
        var movements = new List<Contracts.Inventory.StockMovement>(rows.Count);
        for (var i = 0; i < rows.Count; i++) movements.Add(rows[i].ToContract(zone));
        return new MovementPage(movements, result.Value.TotalCount);
    }

    public async Task<ContractProduct> CreateProductAsync(NewProductRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var attributeName = AttributeNameOf(request.AttributeName, request.Variants, null);
        var variants = ToInputs(request.Variants, attributeName, withOpeningStock: true);
        if (variants.Length == 0 && request.AttributeOptions.Count > 0)
        {
            // A multi-variant product: one variant per declared option, sharing the dimension. Every option
            // gets its own unique SKU and starts independently stockable at zero.
            attributeName = string.IsNullOrWhiteSpace(request.AttributeName) ? "تنوع" : request.AttributeName.Trim();
            variants = FromOptions(request.Code, attributeName, request.AttributeOptions);
        }
        else if (variants.Length == 0)
        {
            // A stock-tracked product always keeps at least one variant (VARIANT_REQUIRED): a single-unit
            // product is created with one implicit DEFAULT variant, exactly as the domain models it.
            variants = [new ProductVariantInput(null, SkuFor(request.Code, "1"), DomainProductVariant.DefaultAttributeName,
                request.Code, 0L, 0L, 0L)];
            attributeName = DomainProductVariant.DefaultAttributeName;
        }

        var productId = await SendAsync(new CreateProductCommand(
            request.Code, request.Name, request.CategoryId, "PCS", ProductKind.PetShop,
            Lowest(request.Variants, cost: true), Lowest(request.Variants, cost: false), variants,
            request.Brand, Notes: request.Description));

        var detail = await GetDetailAsync(productId);
        return detail?.Product ?? new ContractProduct(productId, request.Code, request.Name, request.Brand,
            request.CategoryId, request.Description, null, []);
    }

    public async Task DeleteProductAsync(string productId) =>
        _ = await SendAsync(new DeleteProductCommand(productId));

    public async Task AdjustStockAsync(string productId, StockAdjustRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Lines.Count == 0) return;

        // Delta-native and atomic: absolute targets go to the handler, which reads the fresh on-hand inside
        // its own unit of work (no client read-modify-write gap) and records one reasoned movement per change.
        var lines = new StockTargetLine[request.Lines.Count];
        for (var i = 0; i < lines.Length; i++)
        {
            lines[i] = new StockTargetLine(request.Lines[i].VariantId,
                InventoryMapping.IntToScaled(Math.Max(0, request.Lines[i].Stock)));
        }

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? AdjustmentReasons.Correction : request.Reason;
        _ = await SendAsync(new SetStockCommand(productId, lines, reason, request.Note));
    }

    /// <summary>Expands a multi-variant product's declared options into one variant input each.</summary>
    private static ProductVariantInput[] FromOptions(string code, string attributeName, IReadOnlyList<string> options)
    {
        var inputs = new List<ProductVariantInput>(options.Count);
        var used = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < options.Count; i++)
        {
            var option = options[i].Trim();
            if (option.Length == 0) continue;

            var sku = SkuFor(code, option);
            if (!used.Add(sku))
            {
                sku = $"{sku}-{i + 1}";
                used.Add(sku);
            }
            inputs.Add(new ProductVariantInput(null, sku, attributeName, option, 0L, 0L, 0L));
        }
        return inputs.ToArray();
    }

    private static ProductVariantInput[] ToInputs(
        IReadOnlyList<ContractVariant> variants, string attributeName, bool withOpeningStock = false)
    {
        var inputs = new ProductVariantInput[variants.Count];
        for (var i = 0; i < variants.Count; i++)
        {
            var variant = variants[i];
            inputs[i] = new ProductVariantInput(
                string.IsNullOrEmpty(variant.Id) ? null : variant.Id,
                string.IsNullOrWhiteSpace(variant.Sku) ? SkuFor(variant.Label, (i + 1).ToString()) : variant.Sku,
                attributeName, variant.Label, variant.Cost, variant.Price,
                InventoryMapping.IntToScaled(variant.ReorderPoint),
                OpeningStockScaled: withOpeningStock ? InventoryMapping.IntToScaled(Math.Max(0, variant.Stock)) : 0L);
        }
        return inputs;
    }

    /// <summary>
    /// The dimension the product's variants share: the request's own attribute name when it has one, else the
    /// dimension already stored on the product (<paramref name="existing"/>), else — for a genuinely
    /// multi-variant set — a generic «تنوع» dimension so it never collapses to the implicit single-variant
    /// <c>DEFAULT</c> dimension (which the read side treats as "no variants").
    /// </summary>
    private static string AttributeNameOf(string? requested, IReadOnlyList<ContractVariant>? variants, string? existing)
    {
        if (!string.IsNullOrWhiteSpace(requested)) return requested.Trim();
        if (!string.IsNullOrWhiteSpace(existing)) return existing.Trim();
        return variants is { Count: > 1 } ? "تنوع" : DomainProductVariant.DefaultAttributeName;
    }

    /// <summary>Lowest variant cost (or sale) price, used as the product-level default.</summary>
    private static long Lowest(IReadOnlyList<ContractVariant> variants, bool cost)
    {
        var lowest = 0L;
        for (var i = 0; i < variants.Count; i++)
        {
            var value = cost ? variants[i].Cost : variants[i].Price;
            if (lowest == 0L || value < lowest) lowest = value;
        }
        return lowest;
    }

    private static string SkuFor(string seed, string label)
    {
        var suffix = seed.Length > 8 ? seed[^8..] : seed;
        var builder = new System.Text.StringBuilder(suffix.Length + label.Length + 1);
        builder.Append(suffix).Append('-');
        for (var i = 0; i < label.Length; i++)
        {
            var c = label[i];
            if (char.IsLetterOrDigit(c)) builder.Append(c);
        }
        return builder.Length == suffix.Length + 1 ? suffix + "-1" : builder.ToString();
    }

    private async Task<T> SendAsync<T>(ICommand<T> command)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider, _) = Resolve(scope);
        var context = await provider.BuildCommandContextAsync(null, null, null, default);
        if (context.IsFailure) throw new ServiceException(context.Error);
        var result = await dispatcher.SendAsync(command, context.Value!);
        if (result.IsFailure) throw new ServiceException(result.Error);
        return result.Value!;
    }

    private static (IDispatcher Dispatcher, ICurrentUserProvider Provider, IClinicTimeZoneProvider Zones) Resolve(
        AsyncServiceScope scope) =>
        (scope.ServiceProvider.GetRequiredService<IDispatcher>(),
         scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>(),
         scope.ServiceProvider.GetRequiredService<IClinicTimeZoneProvider>());
}
