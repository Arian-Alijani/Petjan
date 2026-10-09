namespace Animora.Persistence.PostgreSql.Concurrency;

/// <summary>A reservation fence lost; only a complete transaction retry may re-derive slots/capacity.</summary>
public sealed class ReservationConcurrencyException(Exception inner) : Exception("Reservation fence changed.", inner);
