using Animora.Domain.Primitives;
using Animora.Domain.Results;

namespace Animora.Domain.Finance;

/// <summary>The five classical account types of a double-entry chart of accounts.</summary>
public enum AccountType
{
    /// <summary>What the clinic owns or is owed. Normal balance: debit.</summary>
    Asset = 0,

    /// <summary>What the clinic owes. Normal balance: credit.</summary>
    Liability = 1,

    /// <summary>The owners' residual interest. Normal balance: credit.</summary>
    Equity = 2,

    /// <summary>Income earned. Normal balance: credit.</summary>
    Revenue = 3,

    /// <summary>Cost incurred. Normal balance: debit.</summary>
    Expense = 4,
}

/// <summary>
/// One account of the tenant's chart of accounts: the destination every
/// <see cref="JournalLine"/> points at through its <see cref="Code"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Code"/> is a numeric string (<c>"1000"</c>, <c>"4010"</c>, …) and is unique per
/// tenant. Codes are never written as literals anywhere else in the product: every reference goes
/// through an <see cref="AccountCodes"/> constant, so a renumbering is a single-file change.
/// </para>
/// <para>
/// <see cref="NormalBalance"/> is <b>derived</b>, never stored: <see cref="AccountType.Asset"/> and
/// <see cref="AccountType.Expense"/> increase on the debit side, the other three on the credit
/// side. A stored copy could drift from <see cref="Type"/>; a derivation cannot.
/// </para>
/// <para>
/// A non-postable account (<see cref="IsPostable"/> = <see langword="false"/>) is a roll-up header:
/// it may parent other accounts but no journal line may name it
/// (<c>ACCOUNT_NOT_POSTABLE</c>, checked by the write path in A10).
/// </para>
/// </remarks>
public sealed class LedgerAccount : AggregateRoot
{
    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private LedgerAccount()
    {
    }

    /// <summary>Numeric account code, unique per tenant. See <see cref="AccountCodes"/>.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Human-readable account name (English; the Desktop localizes for display).</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Which of the five classical types this account belongs to.</summary>
    public AccountType Type { get; private set; }

    /// <summary>The roll-up parent account; <see langword="null"/> for a top-level account.</summary>
    public string? ParentAccountId { get; private set; }

    /// <summary><see langword="true"/> when a journal line may name this account directly.</summary>
    public bool IsPostable { get; private set; } = true;

    /// <summary><see langword="false"/> retires the account without deleting its history.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Optional free-form description shown in the chart-of-accounts screen.</summary>
    public string? Description { get; private set; }

    /// <summary>
    /// The side on which this account increases, derived from <see cref="Type"/> — assets and
    /// expenses on the debit side, liabilities, equity and revenue on the credit side.
    /// </summary>
    public EntrySide NormalBalance => NormalBalanceOf(Type);

    /// <summary>The normal balance of an account type; the single definition in the product.</summary>
    public static EntrySide NormalBalanceOf(AccountType type) => type switch
    {
        AccountType.Asset or AccountType.Expense => EntrySide.Debit,
        _ => EntrySide.Credit,
    };

    /// <summary>Creates an account. The code must be a non-empty numeric string.</summary>
    /// <param name="tenantId">Owning tenant.</param>
    /// <param name="code">Numeric account code, unique per tenant.</param>
    /// <param name="name">Account name.</param>
    /// <param name="type">Account type; fixes the normal balance.</param>
    /// <param name="createdUtc">Creation instant, UTC (from the injected clock at the call site).</param>
    /// <param name="parentAccountId">The roll-up parent, when this account is a child.</param>
    /// <param name="isPostable"><see langword="false"/> for a header account.</param>
    /// <param name="description">Optional description.</param>
    public static Result<LedgerAccount> Create(
        string tenantId,
        string code,
        string name,
        AccountType type,
        DateTimeOffset createdUtc,
        string? parentAccountId = null,
        bool isPostable = true,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var trimmed = code.Trim();
        if (!IsNumericCode(trimmed))
        {
            return Result<LedgerAccount>.Fail(
                ErrorCodes.ValidationFailed,
                $"An account code must be a numeric string; got '{trimmed}'.");
        }

        return new LedgerAccount
        {
            Id = IdGenerator.NewId(createdUtc),
            TenantId = tenantId,
            Code = trimmed,
            Name = name.Trim(),
            Type = type,
            ParentAccountId = Clean(parentAccountId),
            IsPostable = isPostable,
            IsActive = true,
            Description = Clean(description),
        };
    }

    /// <summary>Renames the account and/or updates its description. The code never changes.</summary>
    public Result Rename(string name, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
        Description = Clean(description);
        return Result.Ok();
    }

    /// <summary>Retires the account. Its posted history stays readable forever.</summary>
    public Result Deactivate()
    {
        IsActive = false;
        return Result.Ok();
    }

    /// <summary>Brings a retired account back into use.</summary>
    public Result Activate()
    {
        IsActive = true;
        return Result.Ok();
    }

    private static bool IsNumericCode(string code)
    {
        for (var i = 0; i < code.Length; i++)
        {
            if (code[i] is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
