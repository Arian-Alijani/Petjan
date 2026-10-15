namespace Animora.Application.Ports;

/// <summary>
/// Persists the immutable visit-to-billing snapshot in the command's transaction. <paramref name="id"/> is derived by the
/// handler from the completing command's operation id (<c>IdGenerator.ForOperation</c>) so the offline client and the
/// server — which run the same handler for the same operation — store the handoff under the <b>same</b> id (D03).
/// </summary>
public interface IVisitBillingHandoffWriter
{
    void Add(string id, string tenantId, string visitId, string ownerId, string petId, string doctorId,
        DateTimeOffset completedUtc, string linesJson);
}
