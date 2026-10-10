using System.Text.RegularExpressions;
using Animora.Domain.Primitives;
using Animora.Domain.Results;

namespace Animora.Domain.Subscriptions;

public sealed class Plan
{
    private Plan() { }
    public string Id { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public int DefaultSeatLimit { get; private set; }
    public long MonthlyAmountMinor { get; private set; }
    public string Currency { get; private set; } = "IRT";
    public int TrialDays { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedUtc { get; private set; }

    public static Result<Plan> Create(string code, string name, int seats, long amountMinor,
        int trialDays, DateTimeOffset now, string currency = "IRT")
    {
        if (string.IsNullOrWhiteSpace(code) ||
            !Regex.IsMatch(code, "^[a-z0-9-]{2,32}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) ||
            string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100 || seats < 1 ||
            amountMinor < 0 || trialDays is < 0 or > 365 || currency != "IRT")
            return Result<Plan>.Fail(ErrorCodes.ValidationFailed, "invalid_plan");
        return Result<Plan>.Ok(new Plan
        {
            Id = IdGenerator.NewId(now), Code = code, Name = name.Trim(), DefaultSeatLimit = seats,
            MonthlyAmountMinor = amountMinor, TrialDays = trialDays, Currency = currency,
            CreatedUtc = now.ToUniversalTime(),
        });
    }

    public void Deactivate() => IsActive = false;
}
