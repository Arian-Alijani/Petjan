using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.ValueObjects;

namespace Animora.Domain.Boarding;

/// <summary>How a boarding service is billed.</summary>
public enum BoardingServiceBilling
{
    /// <summary>Charged once for the whole stay.</summary>
    PerStay = 0,

    /// <summary>Charged for each day of the stay.</summary>
    PerDay = 1,

    /// <summary>Charged each time it is used.</summary>
    PerUse = 2,
}

/// <summary>
/// A boarding-service catalogue entry aggregate root: a named, priced service reception can add to a
/// stay (e.g. bathing, a special diet, extra walks) with its billing basis.
/// </summary>
/// <remarks>
/// This is the tenant-editable catalogue of boarding add-ons. Like every catalogue in the product it
/// only carries the <i>unit</i> price and billing basis — the stay records what was consumed and
/// Sales computes totals (A07). Every mutator returns <see cref="Result"/>.
/// </remarks>
public sealed class BoardingServiceDef : AggregateRoot
{
    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private BoardingServiceDef()
    {
    }

    /// <summary>Short service code, e.g. <c>BATH</c>.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Human-readable service title.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>Price per billing unit.</summary>
    public Money UnitPrice { get; private set; }

    /// <summary>How the service is billed.</summary>
    public BoardingServiceBilling Billing { get; private set; }

    /// <summary><see langword="true"/> while the service may be added to new stays.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Creates an active boarding service.</summary>
    public static BoardingServiceDef Create(
        string tenantId,
        string code,
        string title,
        Money unitPrice,
        BoardingServiceBilling billing,
        string? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        return new BoardingServiceDef
        {
            Id = string.IsNullOrWhiteSpace(id) ? IdGenerator.NewId() : id,
            TenantId = tenantId,
            Code = code.Trim(),
            Title = title.Trim(),
            UnitPrice = unitPrice,
            Billing = billing,
            IsActive = true,
        };
    }

    /// <summary>Updates the title, price and billing basis.</summary>
    public Result Update(string title, Money unitPrice, BoardingServiceBilling billing)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        Title = title.Trim();
        UnitPrice = unitPrice;
        Billing = billing;
        return Result.Ok();
    }

    /// <summary>Retires the service from new stays without deleting its history.</summary>
    public Result Deactivate()
    {
        IsActive = false;
        return Result.Ok();
    }

    /// <summary>Returns the service to the active catalogue.</summary>
    public Result Activate()
    {
        IsActive = true;
        return Result.Ok();
    }
}
