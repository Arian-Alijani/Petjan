using Animora.Client.Services.Appointments;
using Animora.Contracts.Visits;
using Animora.Domain.Scheduling;

namespace Animora.Client.Services.Visits;

public static class VisitTypeCatalog
{
    // Static reference data from the domain; synchronous Contracts member never does I/O.
    public static IReadOnlyList<VisitTypeInfo> All { get; } = AppointmentTypeCatalog.All
        .Select(type => new VisitTypeInfo(type.Code, AppointmentsMapping.TypeLabel(type.Code), "IconBriefcase", "#0d9488",
            type.DefaultDurationMinutes)).ToArray();
}
