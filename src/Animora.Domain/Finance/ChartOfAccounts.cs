namespace Animora.Domain.Finance;

/// <summary>
/// The account codes of the default chart. <b>Every</b> reference to an account — posting rules,
/// expense categories, seeds, read models, reports — goes through a constant declared here; a
/// numeric literal like <c>"1100"</c> never appears anywhere else in the product.
/// </summary>
public static class AccountCodes
{
    /// <summary>1000 — Cash on Hand (asset).</summary>
    public const string CashOnHand = "1000";

    /// <summary>1010 — Bank Account (asset).</summary>
    public const string Bank = "1010";

    /// <summary>1020 — Cheques Receivable (asset): inbound cheques not yet cleared.</summary>
    public const string ChequesReceivable = "1020";

    /// <summary>1100 — Accounts Receivable (asset): what customers owe.</summary>
    public const string AccountsReceivable = "1100";

    /// <summary>1110 — Insurance Receivable (asset): the insurer's share of invoices.</summary>
    public const string InsuranceReceivable = "1110";

    /// <summary>1200 — Inventory (asset): stock at weighted-average cost.</summary>
    public const string Inventory = "1200";

    /// <summary>1900 — Cash Variance (expense): drawer over/short.</summary>
    public const string CashVariance = "1900";

    /// <summary>2000 — Accounts Payable (liability): what the clinic owes suppliers.</summary>
    public const string AccountsPayable = "2000";

    /// <summary>2010 — Cheques Payable (liability): outbound cheques not yet cleared.</summary>
    public const string ChequesPayable = "2010";

    /// <summary>2100 — Customer Prepayments (liability): wallet / deposit balances.</summary>
    public const string CustomerPrepayments = "2100";

    /// <summary>2200 — Tax Payable (liability).</summary>
    public const string TaxPayable = "2200";

    /// <summary>3000 — Owner Equity (equity).</summary>
    public const string OwnerEquity = "3000";

    /// <summary>4000 — Service Revenue (revenue): clinical and grooming services.</summary>
    public const string ServiceRevenue = "4000";

    /// <summary>4010 — Product Revenue (revenue): goods sold from inventory.</summary>
    public const string ProductRevenue = "4010";

    /// <summary>4020 — Boarding Revenue (revenue): stays and boarding items.</summary>
    public const string BoardingRevenue = "4020";

    /// <summary>4900 — Sales Discounts (expense, contra-revenue by convention).</summary>
    public const string SalesDiscounts = "4900";

    /// <summary>5000 — Cost of Goods Sold (expense), at weighted-average cost.</summary>
    public const string Cogs = "5000";

    /// <summary>6000 — Operating Expense (expense): the default expense bucket.</summary>
    public const string OperatingExpense = "6000";

    /// <summary>6100 — Salary Expense (expense).</summary>
    public const string SalaryExpense = "6100";

    /// <summary>6200 — Inventory Loss (expense): shrinkage, damage, write-down.</summary>
    public const string InventoryLoss = "6200";
}

/// <summary>
/// One row of the default chart of accounts seed.
/// </summary>
/// <param name="Code">The account code; always one of the <see cref="AccountCodes"/> constants.</param>
/// <param name="Name">The account name.</param>
/// <param name="Type">The account type; fixes the normal balance.</param>
/// <param name="IsPostable"><see langword="true"/> when a journal line may name it directly.</param>
public sealed record ChartOfAccountsEntry(
    string Code,
    string Name,
    AccountType Type,
    bool IsPostable)
{
    /// <summary>The side on which this account increases, derived from <see cref="Type"/>.</summary>
    public EntrySide NormalBalance => LedgerAccount.NormalBalanceOf(Type);
}

/// <summary>
/// The <b>authoritative</b> default chart of accounts: the 20 accounts every new tenant is seeded
/// with (the seed itself runs in the persistence layer — this type only declares the truth).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PostingRules"/> posts only to codes listed here, and every code listed here is
/// postable, so a posting rule can never address a roll-up header. A tenant may add its own
/// accounts on top; it may not remove one of these — later phases assume they exist.
/// </para>
/// </remarks>
public static class ChartOfAccounts
{
    private static readonly ChartOfAccountsEntry[] DefaultEntries =
    [
        new(AccountCodes.CashOnHand, "Cash on Hand", AccountType.Asset, true),
        new(AccountCodes.Bank, "Bank Account", AccountType.Asset, true),
        new(AccountCodes.ChequesReceivable, "Cheques Receivable", AccountType.Asset, true),
        new(AccountCodes.AccountsReceivable, "Accounts Receivable", AccountType.Asset, true),
        new(AccountCodes.InsuranceReceivable, "Insurance Receivable", AccountType.Asset, true),
        new(AccountCodes.Inventory, "Inventory", AccountType.Asset, true),
        new(AccountCodes.CashVariance, "Cash Variance", AccountType.Expense, true),
        new(AccountCodes.AccountsPayable, "Accounts Payable", AccountType.Liability, true),
        new(AccountCodes.ChequesPayable, "Cheques Payable", AccountType.Liability, true),
        new(AccountCodes.CustomerPrepayments, "Customer Prepayments", AccountType.Liability, true),
        new(AccountCodes.TaxPayable, "Tax Payable", AccountType.Liability, true),
        new(AccountCodes.OwnerEquity, "Owner Equity", AccountType.Equity, true),
        new(AccountCodes.ServiceRevenue, "Service Revenue", AccountType.Revenue, true),
        new(AccountCodes.ProductRevenue, "Product Revenue", AccountType.Revenue, true),
        new(AccountCodes.BoardingRevenue, "Boarding Revenue", AccountType.Revenue, true),
        new(AccountCodes.SalesDiscounts, "Sales Discounts", AccountType.Expense, true),
        new(AccountCodes.Cogs, "Cost of Goods Sold", AccountType.Expense, true),
        new(AccountCodes.OperatingExpense, "Operating Expense", AccountType.Expense, true),
        new(AccountCodes.SalaryExpense, "Salary Expense", AccountType.Expense, true),
        new(AccountCodes.InventoryLoss, "Inventory Loss", AccountType.Expense, true),
    ];

    private static readonly Dictionary<string, ChartOfAccountsEntry> ByCode =
        DefaultEntries.ToDictionary(static e => e.Code, StringComparer.Ordinal);

    /// <summary>The 20 default accounts, in code order.</summary>
    public static IReadOnlyList<ChartOfAccountsEntry> Default => DefaultEntries;

    /// <summary>Looks a default account up by code; <see langword="null"/> when unknown.</summary>
    public static ChartOfAccountsEntry? Find(string code) =>
        code is not null && ByCode.TryGetValue(code, out var entry) ? entry : null;

    /// <summary><see langword="true"/> when the code belongs to the default chart.</summary>
    public static bool Contains(string code) => Find(code) is not null;

    /// <summary>
    /// <see langword="true"/> when the code is a known default account <i>and</i> may be named by a
    /// journal line. An unknown code is not postable — the caller decides whether that is
    /// <c>ACCOUNT_UNKNOWN</c> or a tenant-defined account it must load from the store.
    /// </summary>
    public static bool IsPostable(string code) => Find(code)?.IsPostable == true;
}
