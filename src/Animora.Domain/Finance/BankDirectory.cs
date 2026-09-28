namespace Animora.Domain.Finance;

/// <summary>Static reference catalog; no database lookup is needed to render bank names.</summary>
public static class BankDirectory
{
    public sealed record Bank(string Key, string Name, string Short, string ColorFrom, string ColorTo);

    public static IReadOnlyList<Bank> All { get; } = Array.AsReadOnly(new[]
    {
        new Bank("melli", "بانک ملی", "ملی", "#2363A3", "#163967"),
        new Bank("mellat", "بانک ملت", "ملت", "#C23042", "#761D2A"),
        new Bank("saderat", "بانک صادرات", "صادرات", "#2476AF", "#15517D"),
        new Bank("tejarat", "بانک تجارت", "تجارت", "#3F68A5", "#253F78"),
        new Bank("sepah", "بانک سپه", "سپه", "#A86547", "#77412E"),
        new Bank("keshavarzi", "بانک کشاورزی", "کشاورزی", "#48954B", "#28602F"),
        new Bank("maskan", "بانک مسکن", "مسکن", "#D18B42", "#A45B29"),
        new Bank("refah", "بانک رفاه", "رفاه", "#33868A", "#1C5F65"),
        new Bank("parsian", "بانک پارسیان", "پارسیان", "#9E567B", "#71385A"),
        new Bank("pasargad", "بانک پاسارگاد", "پاسارگاد", "#B18847", "#785823"),
        new Bank("saman", "بانک سامان", "سامان", "#3973AB", "#224875"),
        new Bank("eghtesadnovin", "بانک اقتصادنوین", "اقتصادنوین", "#6D598F", "#463561"),
        new Bank("sina", "بانک سینا", "سینا", "#5B9C78", "#306549"),
        new Bank("dey", "بانک دی", "دی", "#5598A4", "#2E6377"),
        new Bank("shahr", "بانک شهر", "شهر", "#B44D60", "#79273C"),
        new Bank("ayandeh", "بانک آینده", "آینده", "#8060AA", "#533782"),
        new Bank("gardeshgari", "بانک گردشگری", "گردشگری", "#B78A3B", "#755921"),
        new Bank("sarmayeh", "بانک سرمایه", "سرمایه", "#738393", "#465767"),
    });
}
