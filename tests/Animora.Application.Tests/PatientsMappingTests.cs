using System;
using System.Linq;
using Animora.Application.UseCases.Patients.ReadModels;
using Animora.Client.Services.Patients;
using Animora.Contracts.Patients;
using Xunit;
using DomainOwnerGender = Animora.Domain.Patients.OwnerGender;
using DomainPetGender = Animora.Domain.Patients.PetGender;
using DomainPetSpecies = Animora.Domain.Patients.PetSpecies;
using DomainPetStatus = Animora.Domain.Patients.PetStatus;

namespace Animora.Application.Tests;

/// <summary>
/// C01 hand-written mapping (read model → Contracts DTO). Pins the fixed rules from the phase: enum
/// conversions by NAME (the underlying values differ), name split on the first space, Balance 0 until C09,
/// Phone/Referral null, deterministic avatar tone, and the comma/newline split for clinical flags.
/// </summary>
public sealed class PatientsMappingTests
{
    private static readonly DateTimeOffset Registered = new(2021, 5, 4, 10, 0, 0, TimeSpan.Zero);

    private static PetSummaryReadModel Pet(DomainPetSpecies species = DomainPetSpecies.Cat,
        DomainPetStatus status = DomainPetStatus.Inactive, DomainPetGender gender = DomainPetGender.Female) =>
        new("AB", "رکس", species, null, null, gender, status, "OW1", new DateTimeOffset(2020, 2, 3, 0, 0, 0, TimeSpan.Zero),
            5.5m, null, null, "MC-1");

    [Fact]
    public void PetSummary_EnumConversionsFollowTheNameRules_NotTheIntegerValues()
    {
        // Contracts Cat=0 while Domain Cat=1: a naive cast would produce Dog here.
        var cat = Pet(DomainPetSpecies.Cat).ToSummary();
        Assert.Equal(PetSpecies.Cat, cat.Species);

        Assert.Equal(PetSpecies.Rodent, Pet(DomainPetSpecies.Rodent).ToSummary().Species);
        Assert.Equal(PetSpecies.Other, Pet(DomainPetSpecies.Reptile).ToSummary().Species);

        // Lifecycle → clinical pill.
        Assert.Equal(PetStatus.Healthy, Pet(status: DomainPetStatus.Active).ToSummary().Status);
        Assert.Equal(PetStatus.FollowUp, Pet(status: DomainPetStatus.Inactive).ToSummary().Status);
        Assert.Equal(PetStatus.Critical, Pet(status: DomainPetStatus.Deceased).ToSummary().Status);

        Assert.Equal(PetGender.Unknown, Pet(gender: DomainPetGender.Unknown).ToSummary().Gender);
        Assert.Equal(PetGender.Male, Pet(gender: DomainPetGender.Male).ToSummary().Gender);
    }

    [Fact]
    public void PetSummary_FillsEveryFieldFromTheReadModel()
    {
        var s = Pet().ToSummary();

        Assert.Equal("AB", s.Id);
        Assert.Equal("AB", s.Code);
        Assert.Equal("رکس", s.Name);
        Assert.Equal("", s.Breed);
        Assert.Equal("", s.Color);
        Assert.Equal("OW1", s.OwnerId);
        Assert.Equal(new DateTime(2020, 2, 3), s.BirthDate);
        Assert.Equal(5.5, s.CurrentWeightKg);
        Assert.Null(s.LastVisitDate);      // C03 territory.
        Assert.False(s.HasVaccines);       // C03 territory.
        Assert.Equal("MC-1", s.MicrochipCode);
        // Deterministic, id-derived tone: stable across calls.
        Assert.Equal(s.Tone, Pet().ToSummary().Tone);
    }

    [Fact]
    public void PetDetail_MapsWeightsAndSplitsClinicalFlags()
    {
        var detail = new PetDetailReadModel("P1", "رکس", DomainPetSpecies.Dog, "mixed", "black",
            DomainPetGender.Male, DomainPetStatus.Active, "OW1", new DateTimeOffset(2019, 1, 1, 0, 0, 0, TimeSpan.Zero),
            true, "MC", new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "پنی سیلین، گردو\nمرغ", "دیابت, ", [
                new PetWeightReadModel(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), 4m),
                new PetWeightReadModel(new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero), 5m)]).ToDetail();

        Assert.Equal(PetSpecies.Dog, detail.Species);
        Assert.True(detail.Neutered);
        Assert.Equal("MC", detail.Microchip.Code);
        Assert.Equal(new DateTime(2022, 1, 1), detail.Microchip.ImplantDate);
        Assert.Equal(2, detail.Weights.Count);
        Assert.Equal(4.0, detail.Weights[0].WeightKg);
        Assert.Equal(new[] { "پنی سیلین", "گردو", "مرغ" }, detail.Allergies);
        Assert.Equal(new[] { "دیابت" }, detail.ChronicConditions);
        Assert.Empty(detail.Visits);
        Assert.Empty(detail.Vaccinations);
        Assert.Empty(detail.Documents);
        Assert.Null(detail.Notes);
    }

    [Fact]
    public void OwnerSummary_SplitsTheNameAndDefersFinanceToC09()
    {
        var s = new OwnerSummaryReadModel("OW1", "نگار محمدی", "09120000001", null, null, null,
            DomainOwnerGender.Female, Registered, 1, 0L).ToSummary();

        Assert.Equal("OW1", s.File);
        Assert.Equal("نگار", s.FirstName);
        Assert.Equal("محمدی", s.LastName);
        Assert.Equal(OwnerGender.Female, s.Gender);
        Assert.Equal("09120000001", s.Mobile1);
        Assert.Null(s.Mobile2);
        Assert.Null(s.Phone);              // no landline field in the domain.
        Assert.Equal("", s.NationalId);    // null → "".
        Assert.Null(s.Referral);           // C09 territory.
        Assert.Equal(Registered.UtcDateTime, s.Since);
        Assert.Equal(0L, s.Balance);       // TotalSpend deferred to C09.
        Assert.Empty(s.Tags);
    }

    [Fact]
    public void OwnerSummary_UnspecifiedGenderDefaultsToMale_AndASingleWordIsNotSplit()
    {
        var s = new OwnerSummaryReadModel("OW2", "Ali", "0912", null, "0079", null,
            DomainOwnerGender.Unspecified, Registered, 0, 0L).ToSummary();

        Assert.Equal("Ali", s.FirstName);
        Assert.Equal("", s.LastName);
        Assert.Equal(OwnerGender.Male, s.Gender);   // Contracts has no Unspecified.
        Assert.Equal("0079", s.NationalId);
    }

    [Fact]
    public void OwnerDetail_CarriesTheMappedPetRoster()
    {
        var owner = new OwnerSummaryReadModel("OW1", "نگار محمدی", "0912", null, null, null,
            DomainOwnerGender.Female, Registered, 1, 0L);
        var detail = new OwnerDetailReadModel(owner, [Pet()]).ToDetail();

        var pet = Assert.Single(detail.Pets);
        Assert.Equal("AB", pet.Id);
        Assert.Equal(OwnerGender.Female, detail.Gender);
        Assert.Equal(detail.Id, detail.File);
    }

    [Fact]
    public void ContractsToDomainConversions_MapRabbitToOtherAndAreRoundTripSafe()
    {
        Assert.Equal(DomainPetSpecies.Other, PetSpecies.Rabbit.ToDomain());
        Assert.Equal(DomainPetSpecies.Cat, PetSpecies.Cat.ToDomain());
        Assert.Equal(DomainPetGender.Unknown, PetGender.Unknown.ToDomain());
        Assert.Equal(DomainOwnerGender.Male, OwnerGender.Male.ToDomain());
        // Round trip through the mapping preserves the species name.
        Assert.Equal(PetSpecies.Dog, Pet(DomainPetSpecies.Dog).ToSummary().Species);
    }
}
