using Animora.Application.UseCases.Visits.ReadModels;
using Animora.Domain.Results;

namespace Animora.Application.Ports;

/// <summary>
/// Projection reads backing the visits-triage screen (C02). The doctor rail is one projection over
/// today's appointments (never per doctor); the day is a UTC-ticks range, not a SQL date function
/// (CONVENTIONS §5, PERFORMANCE §6).
/// </summary>
public interface ITriageQueries
{
    /// <summary>Per-doctor booked count + in-visit flag + next-free-slot minute for the local day.</summary>
    Task<IReadOnlyList<DoctorRailReadModel>> GetDoctorRailAsync(
        DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken ct);

    /// <summary>One doctor's booked appointments for the local day.</summary>
    Task<IReadOnlyList<BookedAppointmentReadModel>> GetBookedForDoctorAsync(
        string doctorId, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken ct);

    /// <summary>Top-50 recently-seen patients, optionally filtered by the indexed search key.</summary>
    Task<IReadOnlyList<TriagePatientReadModel>> GetRecentPatientsAsync(
        string? search, CancellationToken ct);
}

/// <summary>A server roster identity, not a credential or permission grant.</summary>
public sealed record VisitDoctorDirectoryEntry(string UserId, string Username, string Name, bool IsActive);

/// <summary>Materializes the complete server doctor roster in the local clinical store; joins the command transaction.</summary>
public interface IVisitDoctorDirectoryWriter
{
    Task<Result> RefreshAsync(IReadOnlyList<VisitDoctorDirectoryEntry> doctors, string actorUserId,
        CancellationToken ct);
}
