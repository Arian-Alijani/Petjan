namespace Animora.Domain.Clinical;

/// <summary>
/// The vitals snapshot taken at the start of an encounter. Every component is optional: a triage
/// nurse records what was actually measured, and a missing value must never be read as a zero.
/// </summary>
/// <param name="WeightKg">Body weight in kilograms.</param>
/// <param name="TemperatureC">Rectal temperature in degrees Celsius.</param>
/// <param name="HeartRateBpm">Heart rate in beats per minute.</param>
/// <param name="RespiratoryRate">Breaths per minute.</param>
/// <param name="MucousMembrane">Mucous-membrane appearance, free text.</param>
/// <param name="Hydration">Hydration assessment, free text.</param>
public sealed record PetVitals(
    decimal? WeightKg,
    decimal? TemperatureC,
    int? HeartRateBpm,
    int? RespiratoryRate,
    string? MucousMembrane,
    string? Hydration);
