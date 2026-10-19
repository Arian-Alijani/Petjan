using System.Text.Json;
using Animora.Application.UseCases.Inventory;
using Animora.Application.UseCases.Patients;
using Animora.Application.UseCases.Purchasing;
using Animora.Domain.Inventory;
using Animora.Domain.Patients;
using Animora.LiveTest.Client;

namespace Animora.LiveTest.Correctness;

/// <summary>An owner and the pet created with it (both namespaced <c>lt-</c>).</summary>
public sealed record OwnerPet(string OwnerId, string PetId, string OwnerName, string Phone, string PetName);

/// <summary>A stock-tracked product with one variant and opening stock.</summary>
public sealed record ProductFixture(string ProductId, string VariantId, string Sku, string Name, string CategoryId, string UnitCode);

/// <summary>
/// Reference data many scenarios depend on, created lazily ONCE per run on client A and verified to reach client B
/// before being handed out. Every row is <c>lt-</c> namespaced so it can never be confused with real clinic data.
/// </summary>
public sealed class Fixtures(ScenarioContext c)
{
    private OwnerPet? _ownerPet;
    private string? _categoryId;
    private ProductFixture? _product;
    private string? _supplierId;

    /// <summary>Opening stock given to the fixture product: 100 units at the store's ×1000 quantity scale.</summary>
    public const long OpeningStockScaled = 100_000;

    /// <summary>
    /// An ACTIVE doctor of the tenant (StaffMembership role Doctor=1, status Active=0) read from the replicated staff list,
    /// or null when the tenant has none — scheduling / visit scenarios then report SKIP with that reason.
    /// </summary>
    public async Task<string?> DoctorIdAsync()
    {
        await c.PullAsync(c.A).ConfigureAwait(false);
        foreach (var (_, row) in c.A.Replica.Rows("StaffMembership"))
            if (row.Num("role") == 1 && row.Num("status") == 0 && row.Str("userId") is { Length: > 0 } user)
                return user;
        return null;
    }

    public async Task<OwnerPet> OwnerPetAsync()
    {
        if (_ownerPet is not null) return _ownerPet;
        var name = c.Name("owner");
        var phone = ScenarioContext.NextPhone();
        var petName = c.Name("pet");
        var created = await c.AcceptAsync(c.A, new CreateOwnerCommand(name, phone, OwnerGender.Male, null, null, null,
            petName, PetSpecies.Dog, "mixed", "black", PetGender.Male, null, null, null, false)).ConfigureAwait(false);
        var ownerId = ScenarioContext.IdOf(created);
        var pet = await c.AwaitFindAsync(c.A, "Pet", (_, r) => r.Str("ownerId") == ownerId, "fixture pet").ConfigureAwait(false);
        _ownerPet = new OwnerPet(ownerId, pet.Id, name, phone, petName);
        return _ownerPet;
    }

    public async Task<string> CategoryIdAsync()
    {
        if (_categoryId is not null) return _categoryId;
        var created = await c.AcceptAsync(c.A, new CreateProductCategoryCommand(c.Name("cat"))).ConfigureAwait(false);
        _categoryId = ScenarioContext.IdOf(created);
        return _categoryId;
    }

    public async Task<ProductFixture> ProductAsync()
    {
        if (_product is not null) return _product;
        var categoryId = await CategoryIdAsync().ConfigureAwait(false);
        var unit = await UnitCodeAsync().ConfigureAwait(false);
        var sku = c.Name("sku");
        var name = c.Name("product");
        var created = await c.AcceptAsync(c.A, new CreateProductCommand(sku, name, categoryId, unit, ProductKind.Medicine, 10_000, 20_000,
            [new ProductVariantInput(null, sku + "-1", ProductVariant.DefaultAttributeName, "default", 10_000, 20_000, 0, null, OpeningStockScaled)]))
            .ConfigureAwait(false);
        var productId = ScenarioContext.IdOf(created);
        var row = await c.AwaitRowAsync(c.A, "Product", productId, what: "fixture product").ConfigureAwait(false);
        _product = new ProductFixture(productId, FirstVariantId(row) ?? throw ScenarioContext.Fail("fixture product replicated without a variant id."),
            sku, name, categoryId, unit);
        return _product;
    }

    public async Task<string> SupplierIdAsync()
    {
        if (_supplierId is not null) return _supplierId;
        var created = await c.AcceptAsync(c.A, new CreateSupplierCommand(c.Name("supplier"), null, 0, ScenarioContext.NextPhone())).ConfigureAwait(false);
        _supplierId = ScenarioContext.IdOf(created);
        return _supplierId;
    }

    /// <summary>Unit code the tenant already knows (first replicated UnitOfMeasure), falling back to <c>PCS</c>.</summary>
    public async Task<string> UnitCodeAsync()
    {
        await c.PullAsync(c.A).ConfigureAwait(false);
        foreach (var (_, row) in c.A.Replica.Rows("UnitOfMeasure"))
            if (row.Str("code") is { Length: > 0 } code) return code;
        return "PCS";
    }

    /// <summary>First variant id of a replicated product row.</summary>
    public static string? FirstVariantId(ReplicaRow row)
    {
        if (row.Prop("variants") is not { ValueKind: JsonValueKind.Array } variants) return null;
        foreach (var variant in variants.EnumerateArray())
            if (variant.ValueKind == JsonValueKind.Object)
                foreach (var property in variant.EnumerateObject())
                    if (property.Name.Equals("variantId", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                        return property.Value.GetString();
        return null;
    }

    /// <summary>True when the row's payload mentions the id anywhere (used for ledger rows whose property names we do not rely on).</summary>
    public static bool Mentions(ReplicaRow row, string id) => row.Payload?.GetRawText().Contains(id, StringComparison.Ordinal) == true;
}
