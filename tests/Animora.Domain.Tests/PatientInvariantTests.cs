using System;
using System.Linq;
using System.Reflection;
using Animora.Domain.Clinical;
using Animora.Domain.Events;
using Animora.Domain.Patients;
using Animora.Domain.Results;
using Xunit;
using TestClock = Animora.Domain.Tests.VisitStateMachineTests.TestClock;

namespace Animora.Domain.Tests;

/// <summary>
/// Patient-side invariants: species is fixed at registration, weights stay in a plausible range,
/// a document always has a target, phone numbers normalize, and the medical history is append-only.
/// </summary>
public sealed class PatientInvariantTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Species_HasNoMutator_AndRegistrationRaisesTheEvent()
    {
        var pet = Pet.Register("T1", "OWN1", "Rex", PetSpecies.Dog, new TestClock(Now));

        var species = typeof(Pet).GetProperty(nameof(Pet.Species))!;
        Assert.Null(species.SetMethod?.IsPublic == true ? species.SetMethod : null);
        Assert.DoesNotContain(
            typeof(Pet).GetMethods(BindingFlags.Public | BindingFlags.Instance),
            m => m.GetParameters().Any(p => p.ParameterType == typeof(PetSpecies)));

        var e = Assert.IsType<PetRegisteredEvent>(Assert.Single(pet.DomainEvents));
        Assert.Equal(pet.Id, e.PetId);
        Assert.Equal("OWN1", e.OwnerId);
        Assert.Equal(PetSpecies.Dog, pet.Species);
    }

    [Theory]
    [InlineData(2026, 4, 1, 12)] // exactly one year
    [InlineData(2026, 4, 2, 11)] // one day short of the 12-month anniversary
    [InlineData(2027, 4, 1, 0)] // born in the future → never negative
    public void AgeMonthsAt_IsComputedNotStored(int year, int month, int day, int expected)
    {
        var birth = new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero);
        var pet = Pet.Register("T1", "OWN1", "Rex", PetSpecies.Dog, new TestClock(Now), birthDateUtc: birth);

        Assert.Equal(expected, pet.AgeMonthsAt(new DateTimeOffset(2027, 4, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void AgeMonthsAt_IsNullWithoutABirthDate() =>
        Assert.Null(Pet.Register("T1", "OWN1", "Rex", PetSpecies.Cat, new TestClock(Now)).AgeMonthsAt(Now));

    [Theory]
    [InlineData(0)]
    [InlineData(-1.5)]
    [InlineData(300.1)]
    public void WeightOutsideRange_IsRefused(double weight)
    {
        var result = Record((decimal)weight);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.WeightOutOfRange, result.Error);
    }

    [Fact]
    public void WeightInRange_IsRecordedAndAppendOnly()
    {
        var result = Record(12.4m);
        Assert.True(result.IsSuccess);
        Assert.Equal(12.4m, result.Value!.WeightKg);
        Assert.Null(result.Value.CorrectsEntryId);

        Assert.True(Record(WeightEntry.MaxWeightKg).IsSuccess);
        Assert.Empty(PublicMutators(typeof(WeightEntry)));
    }

    [Fact]
    public void DocumentWithoutAnyTarget_IsRefused()
    {
        var orphan = Attach(null, null, null);
        Assert.True(orphan.IsFailure);
        Assert.Equal(ErrorCodes.DocumentTargetRequired, orphan.Error);

        Assert.True(Attach("OWN1", null, null).IsSuccess);
        Assert.True(Attach(null, "PET1", null).IsSuccess);
        Assert.True(Attach(null, null, "VIS1").IsSuccess);

        // A-RULE 020: the reference carries the file-store id, never a filesystem path.
        Assert.Equal("FILE1", Attach(null, "PET1", null).Value!.FileId);
    }

    [Theory]
    [InlineData("0912 345 6789", "09123456789")]
    [InlineData("+98-912-345-6789", "989123456789")]
    [InlineData("۰۹۱۲۳۴۵۶۷۸۹", "09123456789")]
    [InlineData("(021) 8877-6655", "02188776655")]
    public void NormalizePhone_KeepsDigitsAndTheLeadingZero(string raw, string expected) =>
        Assert.Equal(expected, Owner.NormalizePhone(raw));

    [Fact]
    public void Owner_NormalizesBothNumbersAndBlocksIdempotently()
    {
        var owner = Owner.Register("T1", " سارا ", "0912 111 2233", new TestClock(Now), OwnerGender.Female);
        Assert.Equal("سارا", owner.FullName);
        Assert.Equal("09121112233", owner.PhonePrimary);

        owner.UpdateContactDetails("سارا م.", "0912-111-2233", phoneSecondary: "021 888 99 00", city: "  ");
        Assert.Equal("02188899 00".Replace(" ", string.Empty, StringComparison.Ordinal), owner.PhoneSecondary);
        Assert.Null(owner.City);

        owner.Block("unpaid invoices");
        owner.Block("second reason");
        Assert.True(owner.IsBlocked);
        Assert.Equal("unpaid invoices", owner.BlockReason);

        owner.Unblock();
        Assert.False(owner.IsBlocked);
        Assert.Null(owner.BlockReason);
    }

    /// <summary>
    /// D10: a NON-BLANK but digitless phone ("---", "abc") normalizes to the empty string. Storing that created a
    /// server row the replica consumer refuses (PhonePrimary is a REQUIRED sync field), so the whole Owner — and
    /// every pet, visit and invoice under it — was dropped on every peer. The domain refuses such a value.
    /// </summary>
    [Theory]
    [InlineData("---")]
    [InlineData("abc")]
    [InlineData("+")]
    [InlineData("() -")]
    public void Owner_RefusesADigitlessPhone(string raw)
    {
        Assert.Throws<ArgumentException>(() => Owner.Register("T1", "سارا", raw, new TestClock(Now)));
        var owner = Owner.Register("T1", "سارا", "09121112233", new TestClock(Now));
        Assert.Throws<ArgumentException>(() => owner.UpdateContactDetails("سارا", raw));
    }

    /// <summary>
    /// D10: an ABSENT phone is a different thing — "not recorded", a real state for an imported record whose source
    /// row has no contact number. It is stored as the empty string and is never mistaken for a digitless typo.
    /// </summary>
    [Fact]
    public void Owner_AcceptsAnAbsentPhoneAsNotRecorded()
    {
        var owner = Owner.Register("T1", "سارا", null, new TestClock(Now));
        Assert.Equal(string.Empty, owner.PhonePrimary);

        owner.UpdateContactDetails("سارا", null);
        Assert.Equal(string.Empty, owner.PhonePrimary);
    }

    [Fact]
    public void MedicalHistoryEntry_IsAppendOnly()
    {
        // ARCHITECTURE.md §8.5: history is preserved in full — no mutator, no public setter.
        Assert.Empty(PublicMutators(typeof(MedicalHistoryEntry)));
        Assert.DoesNotContain(
            typeof(MedicalHistoryEntry).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            p => p.SetMethod is { IsPublic: true });

        var entry = new MedicalHistoryEntry(
            "T1", "PET1", MedicalHistoryKind.Visit, "Consultation", Now, "DOC1", new TestClock(Now));

        Assert.Equal("PET1", entry.PetId);
        Assert.Equal(MedicalHistoryKind.Visit, entry.Kind);
        Assert.Equal(26, entry.Id.Length);
    }

    private static Result<WeightEntry> Record(decimal weightKg) =>
        WeightEntry.Record("T1", "PET1", weightKg, Now, "DOC1", new TestClock(Now));

    private static Result<DocumentReference> Attach(string? ownerId, string? petId, string? visitId) =>
        DocumentReference.Attach(
            "T1", "Blood panel", DocumentKind.Lab, "FILE1", 2048, "abc123", "DOC1", new TestClock(Now),
            ownerId, petId, visitId);

    /// <summary>Public instance methods a type declares itself, excluding accessors and object overrides.</summary>
    private static MethodInfo[] PublicMutators(Type type) =>
        [.. type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)];
}
