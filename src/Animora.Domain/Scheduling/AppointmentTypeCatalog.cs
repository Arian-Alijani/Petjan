namespace Animora.Domain.Scheduling;

/// <summary>
/// One appointment type: its code, the default slot length reception offers, and whether a doctor
/// must be assigned. Grooming, for example, needs no doctor.
/// </summary>
/// <param name="Code">Uppercase type code, e.g. <c>CHECKUP</c>.</param>
/// <param name="DefaultDurationMinutes">Slot length reception pre-fills for this type.</param>
/// <param name="RequiresDoctor"><see langword="true"/> when a doctor must be assigned.</param>
public readonly record struct AppointmentTypeDef(
    string Code,
    int DefaultDurationMinutes,
    bool RequiresDoctor);

/// <summary>
/// The static seed of appointment types the product ships with. This is reference data, not a
/// tenant-editable catalogue — it lets reception offer sensible defaults before any Application
/// layer exists. Lookups are ordinal and case-sensitive on the uppercase code.
/// </summary>
public static class AppointmentTypeCatalog
{
    /// <summary>The seeded appointment types.</summary>
    public static IReadOnlyList<AppointmentTypeDef> All { get; } =
    [
        new("CHECKUP", 30, true),
        new("VACCINE", 15, true),
        new("SURGERY", 90, true),
        new("DENTAL", 60, true),
        new("GROOMING", 45, false),
        new("FOLLOWUP", 20, true),
        new("CONSULT", 20, true),
    ];

    /// <summary>
    /// Finds a type by its code. Returns <see langword="false"/> when the code is unknown; the
    /// caller decides whether an unknown code is a validation error or a free-form type.
    /// </summary>
    public static bool TryGet(string code, out AppointmentTypeDef def)
    {
        for (var i = 0; i < All.Count; i++)
        {
            if (string.Equals(All[i].Code, code, StringComparison.Ordinal))
            {
                def = All[i];
                return true;
            }
        }

        def = default;
        return false;
    }
}
