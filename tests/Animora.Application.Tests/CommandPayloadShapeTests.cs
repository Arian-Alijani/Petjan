using System.Reflection;
using System.Text.Json;
using Animora.Application.Abstractions;
using Animora.Application.Commands;
using Animora.Application.UseCases.Patients;
using Animora.Domain.Patients;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>
/// BUG 1 regression: the canonical command payload must contain <b>only</b> the command's constructor
/// arguments — never the derived <see cref="IJournaledCommand"/> journaling members
/// (<c>CommandTypeName</c>/<c>AggregateTypeName</c>/<c>AggregateIdOrNull</c>). The server's payload-shape gate
/// (<c>CommandPayloadCatalog</c>) requires the payload JSON keys to equal the longest constructor's parameter
/// names by set equality; a leaked journaling key makes every push fail with
/// <c>VALIDATION_FAILED "payload shape; ruleset=1"</c>.
/// </summary>
public sealed class CommandPayloadShapeTests
{
    private static readonly string[] JournalingKeys = ["commandTypeName", "aggregateTypeName", "aggregateIdOrNull"];

    private sealed record LocalJournaledCommand(string AggregateId, int Amount) : ICommand<string>, IJournaledCommand
    {
        public string CommandTypeName => "sales.invoice.post";
        public string AggregateTypeName => "Invoice";
        public string? AggregateIdOrNull => AggregateId;
    }

    private static IReadOnlySet<string> PayloadKeys<T>(T command)
    {
        using var doc = JsonDocument.Parse(CommandFingerprint.CanonicalJson(command!));
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        return doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
    }

    private static IReadOnlySet<string> ConstructorParamCamelKeys(Type type)
    {
        var constructor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        return constructor.GetParameters()
            .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name!))
            .ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void LocalJournaledCommand_Payload_ExcludesJournalingMembers()
    {
        var keys = PayloadKeys(new LocalJournaledCommand("INV-1", 500));
        foreach (var journaling in JournalingKeys)
            Assert.DoesNotContain(journaling, keys);
        Assert.Equal(ConstructorParamCamelKeys(typeof(LocalJournaledCommand)), keys);
    }

    [Fact]
    public void CreateOwnerCommand_Payload_EqualsConstructorArguments_Only()
    {
        var command = new CreateOwnerCommand(
            FullName: "مشتری مهمان", PhonePrimary: "09120000000", Gender: OwnerGender.Unspecified,
            PhoneSecondary: null, NationalId: null, Address: null,
            PetName: null, PetSpecies: null, PetBreed: null, PetColor: null, PetGender: null,
            PetBirthUtc: null, PetMicrochip: null, PetMicrochipUtc: null, PetNeutered: false);

        var keys = PayloadKeys(command);

        // No journaling leakage.
        foreach (var journaling in JournalingKeys)
            Assert.DoesNotContain(journaling, keys);

        // Exactly the constructor parameters (what the server's SetEquals shape gate expects).
        Assert.Equal(ConstructorParamCamelKeys(typeof(CreateOwnerCommand)), keys);
    }

    [Fact]
    public void JournalingMembers_AreDeclaredOn_TheInterface()
    {
        // Guards the modifier's member-name list against silent interface drift.
        var names = typeof(IJournaledCommand).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal)
            {
                nameof(IJournaledCommand.CommandTypeName),
                nameof(IJournaledCommand.AggregateTypeName),
                nameof(IJournaledCommand.AggregateIdOrNull),
            },
            names);
    }
}
