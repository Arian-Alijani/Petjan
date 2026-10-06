namespace Animora.Desktop.Pages.Patients;

/// <summary>
/// The page-level section switch above the toolbar: the records directory
/// (owners/pets) or the clinic settings tab (species &amp; breeds, SMS toggle).
/// </summary>
public enum PatientsSection
{
    Directory,
    Settings,
}

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
/// Sort order for the pets view, mirroring the reference's <c>S.sort</c> select
/// (recent | name | age).
/// </summary>
public enum PatientSort
{
    Recent,
    Name,
    Age,
}

/// <summary>
/// A selectable "records per page" option for the directory toolbar. <see cref="Value"/>
/// is the page size (<c>int.MaxValue</c> = show all); <see cref="Label"/> is the
/// Persian caption shown in the dropdown.
/// </summary>
public sealed record PatientsPageSizeOption(int Value, string Label)
{
    public override string ToString() => Label;
}
