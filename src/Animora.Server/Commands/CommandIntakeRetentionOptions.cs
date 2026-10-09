namespace Animora.Server.Commands;

internal sealed class CommandIntakeRetentionOptions
{
    public int FullRecordDays { get; set; } = 120;
    public int ReceiptDays { get; set; } = 3650;
}
