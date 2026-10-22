namespace Animora.Contracts.CustomerClub;

/// <summary>
/// A patient whose birthday is today (clinic-local) with its owner's contact for the greeting list.
/// <see cref="AgeYears"/> is the whole-year age reached today; <see cref="Species"/> is the domain species name.
/// </summary>
public sealed record BirthdayPet(string PetId, string PetName, string Species, string OwnerId, string OwnerName,
    string Phone, int AgeYears);
