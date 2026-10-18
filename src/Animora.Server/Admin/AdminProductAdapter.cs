using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Web.Admin.Models;
using Animora.Web.Admin.Ports;
namespace Animora.Server.Admin;

/// <summary>Per-clinic product catalogue (with ledger-derived stock) for the operator console. Read-only.</summary>
internal sealed class AdminProductAdapter(OperatorReadQueries queries,AdminSession session) : IAdminProductQueries
{
    public async Task<Result<ProductCatalog>> ListAsync(string tenantId,ProductListQuery query,CancellationToken ct=default)
    {
        _=session.OperatorId;
        if(string.IsNullOrWhiteSpace(tenantId)||tenantId.Length>26||query.Search?.Length>100||query.CategoryId?.Length>26
            ||(query.Stock is { Length: > 0 }&&!OperatorReadQueries.StockStates.Contains(query.Stock))
            ||(query.Sort is { Length: > 0 }&&!OperatorReadQueries.ProductSorts.Contains(query.Sort))
            ||query.MinPrice<0||query.MaxPrice<0||(query.MinPrice>query.MaxPrice))
            return Result<ProductCatalog>.Fail(ErrorCodes.ValidationFailed);
        var result=await queries.ProductsAsync(tenantId,new(query.Search,query.CategoryId,query.Stock,query.MinPrice,query.MaxPrice,query.Sort),query.Page,query.PageSize,ct).ConfigureAwait(false);
        if(result is not { } found)return Result<ProductCatalog>.Fail(ErrorCodes.NotFound);
        var page=found.Page;
        return Result<ProductCatalog>.Ok(new(new(page.Items.Select(p=>new ProductSummary(p.Id,p.Sku,p.Name,p.CategoryName,p.Kind.ToString(),p.Active,p.PriceMinor,
            p.Currency,p.OnHand,p.ReorderLevel,p.StockState,p.SalesQty)).ToArray(),page.Page,page.Size,page.Total),
            found.Categories.Select(c=>new CategoryOption(c.Id,c.Name)).ToArray()));
    }
}
