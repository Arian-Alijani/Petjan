using Animora.Domain.Primitives;
using Animora.Domain.Results;

namespace Animora.Domain.Inventory;

/// <summary>
/// A node of the product-category tree: a code, a display name and an optional parent.
/// </summary>
/// <remarks>
/// The tree invariant is that a category may never be its own ancestor (<c>CATEGORY_CYCLE</c>).
/// The domain has no repository, so the ancestor chain of the candidate parent is <b>supplied</b>
/// by the Application layer (nearest ancestor first) and walked here with a plain <c>for</c> loop
/// (PERFORMANCE.md §2).
/// </remarks>
public sealed class ProductCategory : AggregateRoot
{
    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private ProductCategory()
    {
    }

    /// <summary>Short category code, unique per tenant.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Human-readable category name.</summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>Parent category id; <see langword="null"/> for a root category.</summary>
    public string? ParentCategoryId { get; private set; }

    /// <summary>Ordering hint inside its parent, ascending.</summary>
    public int SortOrder { get; private set; }

    /// <summary><see langword="false"/> hides the category from pickers without deleting it.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary><see langword="true"/> when this category sits at the root of the tree.</summary>
    public bool IsRoot => ParentCategoryId is null;

    /// <summary>
    /// Creates a category. <paramref name="parentAncestorIds"/> is the ancestor chain of
    /// <paramref name="parentCategoryId"/> — supplied so the cycle guard can run without a
    /// repository; pass an empty list for a root category.
    /// </summary>
    public static Result<ProductCategory> Create(
        string tenantId,
        string code,
        string displayName,
        string? parentCategoryId = null,
        IReadOnlyList<string>? parentAncestorIds = null,
        int sortOrder = 0,
        string? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var categoryId = string.IsNullOrWhiteSpace(id) ? IdGenerator.NewId() : id;
        var guard = EnsureNoCycle(categoryId, parentCategoryId, parentAncestorIds);
        if (guard.IsFailure)
        {
            return Result<ProductCategory>.Fail(guard.Error, guard.Detail);
        }

        return new ProductCategory
        {
            Id = categoryId,
            TenantId = tenantId,
            Code = code.Trim(),
            DisplayName = displayName.Trim(),
            ParentCategoryId = Clean(parentCategoryId),
            SortOrder = sortOrder,
            IsActive = true,
        };
    }

    /// <summary>
    /// Re-parents the category. <paramref name="parentAncestorIds"/> is the new parent's ancestor
    /// chain; the move is refused when it would make this category its own ancestor
    /// (<c>CATEGORY_CYCLE</c>).
    /// </summary>
    public Result MoveTo(string? parentCategoryId, IReadOnlyList<string>? parentAncestorIds = null)
    {
        var guard = EnsureNoCycle(Id, parentCategoryId, parentAncestorIds);
        if (guard.IsFailure)
        {
            return guard;
        }

        ParentCategoryId = Clean(parentCategoryId);
        return Result.Ok();
    }

    /// <summary>Updates the display name and the sibling ordering hint.</summary>
    public Result Rename(string displayName, int sortOrder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        DisplayName = displayName.Trim();
        SortOrder = sortOrder;
        return Result.Ok();
    }

    /// <summary>Hides the category from pickers; existing products keep referencing it.</summary>
    public Result Deactivate()
    {
        IsActive = false;
        return Result.Ok();
    }

    /// <summary>Returns the category to the pickers.</summary>
    public Result Activate()
    {
        IsActive = true;
        return Result.Ok();
    }

    /// <summary>
    /// The cycle rule: the category may be neither its own parent nor any ancestor of its parent.
    /// </summary>
    private static Result EnsureNoCycle(
        string categoryId,
        string? parentCategoryId,
        IReadOnlyList<string>? parentAncestorIds)
    {
        if (string.IsNullOrWhiteSpace(parentCategoryId))
        {
            return Result.Ok();
        }

        if (string.Equals(categoryId, parentCategoryId, StringComparison.Ordinal))
        {
            return Result.Fail(
                ErrorCodes.CategoryCycle,
                $"Category '{categoryId}' cannot be its own parent.");
        }

        if (parentAncestorIds is null)
        {
            return Result.Ok();
        }

        for (var i = 0; i < parentAncestorIds.Count; i++)
        {
            if (string.Equals(parentAncestorIds[i], categoryId, StringComparison.Ordinal))
            {
                return Result.Fail(
                    ErrorCodes.CategoryCycle,
                    $"Category '{categoryId}' is an ancestor of '{parentCategoryId}'.");
            }
        }

        return Result.Ok();
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
