namespace Animora.Application.Ports;

/// <summary>
/// Why a boarding handoff was raised. A prepaid stay raises a <see cref="Room"/> charge in advance
/// (at admission) and a <see cref="Final"/> settlement at checkout; a postpaid stay raises only a
/// single <see cref="Final"/> handoff. Mirrors the persistence-side discriminator; value 0 is
/// <see cref="Final"/> so existing rows keep their meaning.
/// </summary>
public enum BoardingHandoffKind
{
    /// <summary>Settlement at checkout (room + items, or items-only when the room was prepaid).</summary>
    Final = 0,

    /// <summary>The prepaid room charge raised at admission; does not complete the checkout.</summary>
    Room = 1,
}

/// <summary>Writes the immutable stay-to-billing payload within the command transaction.</summary>
public interface IBoardingBillingHandoffWriter
{
    void Add(string tenantId, string stayId, string ownerId, string petId,
        DateTimeOffset completedUtc, string linesJson,
        BoardingHandoffKind kind = BoardingHandoffKind.Final);
}
