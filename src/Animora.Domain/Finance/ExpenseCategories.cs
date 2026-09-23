namespace Animora.Domain.Finance;

/// <summary>
/// The seeded expense categories and the ledger account each one posts to. An expense never names
/// an account directly: it names a category code, and this mapping resolves it, so re-pointing a
/// category is a one-line change here rather than a data migration.
/// </summary>
/// <remarks>
/// <c>SALARY</c> posts to <see cref="AccountCodes.SalaryExpense"/> (6100); every other category
/// posts to <see cref="AccountCodes.OperatingExpense"/> (6000). An unknown category code also
/// resolves to 6000 — an uncategorized cost is still a cost and must never silently fail to post.
/// </remarks>
public static class ExpenseCategories
{
    /// <summary>Premises rent.</summary>
    public const string Rent = "RENT";

    /// <summary>Electricity, water, gas, internet.</summary>
    public const string Utilities = "UTILITIES";

    /// <summary>Staff salaries and wages; the only category that posts to 6100.</summary>
    public const string Salary = "SALARY";

    /// <summary>Consumables and office supplies.</summary>
    public const string Supplies = "SUPPLIES";

    /// <summary>Repairs and equipment maintenance.</summary>
    public const string Maintenance = "MAINTENANCE";

    /// <summary>Advertising and promotion.</summary>
    public const string Marketing = "MARKETING";

    /// <summary>Taxes and statutory fees paid as a cost.</summary>
    public const string Tax = "TAX";

    /// <summary>Transport, fuel and delivery.</summary>
    public const string Transport = "TRANSPORT";

    /// <summary>Anything that fits nowhere else.</summary>
    public const string Other = "OTHER";

    private static readonly string[] AllCodes =
    [
        Rent, Utilities, Salary, Supplies, Maintenance, Marketing, Tax, Transport, Other,
    ];

    /// <summary>The nine seeded category codes.</summary>
    public static IReadOnlyList<string> All => AllCodes;

    /// <summary><see langword="true"/> when the code is one of the seeded categories.</summary>
    public static bool Contains(string categoryCode) =>
        Array.IndexOf(AllCodes, categoryCode) >= 0;

    /// <summary>
    /// The ledger account a category posts to: <see cref="AccountCodes.SalaryExpense"/> for
    /// <see cref="Salary"/>, <see cref="AccountCodes.OperatingExpense"/> for everything else
    /// (including an unrecognised code).
    /// </summary>
    public static string AccountCodeFor(string categoryCode) =>
        string.Equals(categoryCode, Salary, StringComparison.Ordinal)
            ? AccountCodes.SalaryExpense
            : AccountCodes.OperatingExpense;
}
