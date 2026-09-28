namespace Animora.Application.Ports;

/// <summary>Persists the immutable visit-to-billing snapshot in the command's transaction.</summary>
public interface IVisitBillingHandoffWriter
{
    void Add(string tenantId, string visitId, string ownerId, string petId, string doctorId,
        DateTimeOffset completedUtc, string linesJson);
}
