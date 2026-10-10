namespace Animora.Server.Licensing.Issuance;

public sealed class LicensingOptions
{
    public const string SectionName = "Licensing";
    public int DevicesPerSeat { get; set; } = 2;
    public int MaxIssueTtlDays { get; set; } = 40;
    public int OfflineGraceDays { get; set; } = 14;
    public int ReadOnlyWindowDays { get; set; } = 60;
    public bool IsValid() => DevicesPerSeat is > 0 and <= 20 && MaxIssueTtlDays is > 0 and <= 40
        && OfflineGraceDays is >= 0 and <= 14 && ReadOnlyWindowDays == 60;
}
