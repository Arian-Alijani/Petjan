using Animora.Domain.Primitives;
using Animora.Domain.Results;

namespace Animora.Domain.Purchasing;

/// <summary>
/// A goods supplier: who the clinic buys from, how to reach them and on what payment term.
/// </summary>
/// <remarks>
/// Payables, cheques and the supplier balance live in the ledger (A08) — this aggregate only owns
/// the supplier's own facts. Every mutator returns <see cref="Result"/>.
/// </remarks>
public sealed class Supplier : AggregateRoot
{
    /// <summary>Maximum payment term the domain accepts, in days.</summary>
    public const int MaxPaymentTermDays = 365;

    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private Supplier()
    {
    }

    /// <summary>Short supplier code, unique per tenant.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Company or person name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Day-to-day contact person.</summary>
    public string? ContactPerson { get; private set; }

    /// <summary>Contact phone number.</summary>
    public string? Phone { get; private set; }

    /// <summary>Contact e-mail address.</summary>
    public string? Email { get; private set; }

    /// <summary>Postal address.</summary>
    public string? Address { get; private set; }

    /// <summary>National / company registration id, used on purchase invoices.</summary>
    public string? NationalId { get; private set; }

    /// <summary>Agreed payment term in days; <c>0</c> means cash on delivery.</summary>
    public int PaymentTermDays { get; private set; }

    /// <summary><see langword="false"/> retires the supplier from pickers without deleting history.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Free-form notes.</summary>
    public string? Notes { get; private set; }

    /// <summary>Creates an active supplier.</summary>
    public static Result<Supplier> Create(
        string tenantId,
        string code,
        string name,
        int paymentTermDays = 0,
        string? contactPerson = null,
        string? phone = null,
        string? email = null,
        string? address = null,
        string? nationalId = null,
        string? notes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (paymentTermDays is < 0 or > MaxPaymentTermDays)
        {
            return Result<Supplier>.Fail(
                ErrorCodes.ValidationFailed,
                $"PaymentTermDays {paymentTermDays} must be in [0, {MaxPaymentTermDays}].");
        }

        return new Supplier
        {
            Id = IdGenerator.NewId(),
            TenantId = tenantId,
            Code = code.Trim(),
            Name = name.Trim(),
            ContactPerson = Clean(contactPerson),
            Phone = Clean(phone),
            Email = Clean(email),
            Address = Clean(address),
            NationalId = Clean(nationalId),
            PaymentTermDays = paymentTermDays,
            IsActive = true,
            Notes = Clean(notes),
        };
    }

    /// <summary>Updates the contact details.</summary>
    public Result SetContact(string? contactPerson, string? phone, string? email, string? address)
    {
        ContactPerson = Clean(contactPerson);
        Phone = Clean(phone);
        Email = Clean(email);
        Address = Clean(address);
        return Result.Ok();
    }

    /// <summary>Changes the agreed payment term.</summary>
    public Result SetPaymentTerm(int paymentTermDays)
    {
        if (paymentTermDays is < 0 or > MaxPaymentTermDays)
        {
            return Result.Fail(
                ErrorCodes.ValidationFailed,
                $"PaymentTermDays {paymentTermDays} must be in [0, {MaxPaymentTermDays}].");
        }

        PaymentTermDays = paymentTermDays;
        return Result.Ok();
    }

    /// <summary>Renames the supplier and replaces its notes.</summary>
    public Result Rename(string name, string? notes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
        Notes = Clean(notes);
        return Result.Ok();
    }

    /// <summary>Retires the supplier from pickers; its orders and receipts stay intact.</summary>
    public Result Deactivate()
    {
        IsActive = false;
        return Result.Ok();
    }

    /// <summary>Returns the supplier to the pickers.</summary>
    public Result Activate()
    {
        IsActive = true;
        return Result.Ok();
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
