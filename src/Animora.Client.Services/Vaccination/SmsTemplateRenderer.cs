using System.Text.RegularExpressions;
using Animora.Contracts.Vaccination;
using Animora.Domain.Results;

namespace Animora.Client.Services.Vaccination;

/// <summary>Pure local preview renderer. Rendering or recording a reminder does not transmit SMS.</summary>
public static partial class SmsTemplateRenderer
{
    [GeneratedRegex(@"\{([^{}]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    public static SmsPreview Render(SmsTemplate template, VaccinationRecord record)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(record);
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["pet"] = record.PetName, ["owner"] = record.OwnerName,
            ["vaccine"] = record.VaccineId, ["due"] = record.DueDate.ToString("yyyy/MM/dd",
                System.Globalization.CultureInfo.InvariantCulture),
        };
        var body = TokenPattern().Replace(template.Body, match =>
            replacements.TryGetValue(match.Groups[1].Value, out var replacement) ? replacement :
                throw new InvalidOperationException(ErrorCodes.SmsTemplateTokenUnknown.Code));
        return new SmsPreview(record.Id, template.Id, body, body.Length, (body.Length + 69) / 70);
    }
}
