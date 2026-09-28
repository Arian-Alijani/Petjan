namespace Animora.Application.Ports;

/// <summary>Writes the immutable stay-to-billing payload within the command transaction.</summary>
public interface IBoardingBillingHandoffWriter
{
    void Add(string tenantId, string stayId, string ownerId, string petId,
        DateTimeOffset completedUtc, string linesJson);
}
