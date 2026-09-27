using Animora.Domain.Patients;

namespace Animora.Application.Ports;

/// <summary>Append-only weight history writes (C01). Reads come from the pet-file projection.</summary>
public interface IWeightEntryRepository
{
    /// <summary>Stages an appended reading; the ambient unit of work commits it.</summary>
    void Add(WeightEntry entry);
}
