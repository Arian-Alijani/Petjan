namespace Animora.Desktop.Pages.Patients;

/// <summary>
/// Which directory entity the toolbar's primary segmented switch shows,
/// mirroring the reference's <c>S.view</c> (owners | pets).
/// </summary>
public enum PatientViewMode
{
    Owners,
    Pets,
}

/// <summary>
/// Whether the content renders as rows (table) or boxes (cards), mirroring the
/// reference's <c>S.layout</c> (list | grid).
/// </summary>
public enum PatientLayout
{
    List,
    Grid,
}

/// <summary>
/// Vaccination filter, mirroring the reference's <c>S.vax</c> select
/// (all | has | none).
/// </summary>
public enum VaccineFilter
{
    All,
    Has,
    None,
}

/// <summary>
/// Sort order for the pets view, mirroring the reference's <c>S.sort</c> select
/// (recent | name | age).
/// </summary>
public enum PatientSort
{
    Recent,
    Name,
    Age,
}
