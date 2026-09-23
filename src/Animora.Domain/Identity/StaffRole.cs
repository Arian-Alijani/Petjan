namespace Animora.Domain.Identity;

/// <summary>
/// The five clinic staff roles. The authoritative role set of the domain; the desktop-facing
/// <c>Animora.Contracts.Navigation.UserRole</c> is mapped onto this in Phase C10 — this
/// enum is never reshaped to match Contracts.
/// </summary>
/// <remarks>
/// A role is an <b>authorization</b> concept only. It says nothing about credentials
/// (authentication) or license features (entitlement) — A-RULE 023.
/// </remarks>
public enum StaffRole
{
    /// <summary>Full authority over the tenant, including users, roles and devices.</summary>
    Manager = 0,

    /// <summary>Clinical staff: visits, vaccination, patient records.</summary>
    Doctor = 1,

    /// <summary>Front desk: reception, appointments, boarding.</summary>
    Receptionist = 2,

    /// <summary>Point of sale: invoices, payments, cashier sessions, cheques.</summary>
    Cashier = 3,

    /// <summary>Back office: ledger, period close, purchasing, audit.</summary>
    Accountant = 4,
}
