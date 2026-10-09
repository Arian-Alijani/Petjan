using Animora.Application.Abstractions;
using Animora.Application.Commands;
using Animora.Server.Commands;
using Xunit;

namespace Animora.Server.Tests.Commands;

public sealed class CommandPayloadCatalogTests
{
    [Fact]
    public void EveryEntry_IsJournaledCommand_WithMatchingTypeName()
    {
        var f = new IntakeTestFixture();
        foreach (var pair in f.Catalog.Entries)
        foreach (var candidate in pair.Value)
        {
            Assert.True(typeof(IJournaledCommand).IsAssignableFrom(candidate.CommandRecord));
            string? id = candidate.CommandRecord == typeof(TestUpdateCommand) ? f.UserId : null;
            string payload = id is null ? "{\"name\":\"alpha\"}" : $"{{\"id\":\"{id}\",\"name\":\"alpha\"}}";
            var result = f.Catalog.Resolve(f.Envelope(pair.Key, payload, aggregateId: id));
            Assert.True(result.IsSuccess);
            var journaled = Assert.IsAssignableFrom<IJournaledCommand>(result.Value!.Command);
            Assert.Equal(pair.Key, journaled.CommandTypeName);
            Assert.Equal(candidate.AggregateType, journaled.AggregateTypeName);
        }
    }

    [Fact]
    public void ResolvesSharedCommandTypes_ByShape()
    {
        var f = new IntakeTestFixture();
        var catalog = new CommandPayloadCatalogBuilder().Add<SharedShort>("test.shared", "Test")
            .Add<SharedLong>("test.shared", "Test").Build();
        var shortShape = catalog.Resolve(f.Envelope("test.shared", "{\"NAME\":\"short\"}"));
        var longShape = catalog.Resolve(f.Envelope("test.shared", "{\"name\":\"long\",\"description\":\"more\"}"));
        Assert.IsType<SharedShort>(shortShape.Value!.Command);
        Assert.IsType<SharedLong>(longShape.Value!.Command);
        Assert.True(catalog.Resolve(f.Envelope("test.shared", "{\"name\":\"bad\",\"extra\":1}")).IsFailure);
        Assert.True(catalog.Resolve(f.Envelope("test.shared", "{\"name\":\"bad\",\"Name\":\"duplicate\"}")).IsFailure);
        Assert.True(catalog.Resolve(f.Envelope("test.shared") with { AggregateType = "Other" }).IsFailure);
    }

    [Fact]
    public void NoTwoCandidates_ShareCommandTypeAggregateAndPropertySet()
    {
        var builder = new CommandPayloadCatalogBuilder().Add<SharedShort>("test.shared", "Test");
        Assert.Throws<InvalidOperationException>(() => builder.Add<SharedDuplicate>("test.shared", "Test"));
        var catalog = builder.Add<SharedLong>("test.shared", "Test").Build();
        Assert.Equal(2, catalog.Entries["test.shared"].Count);
    }

    public sealed record SharedShort(string Name) : ICommand<string>, IJournaledCommand
    {
        public string CommandTypeName => "test.shared";
        public string AggregateTypeName => "Test";
        public string? AggregateIdOrNull => null;
    }
    public sealed record SharedLong(string Name, string Description) : ICommand<string>, IJournaledCommand
    {
        public string CommandTypeName => "test.shared";
        public string AggregateTypeName => "Test";
        public string? AggregateIdOrNull => null;
    }
    public sealed record SharedDuplicate(string NAME) : ICommand<string>, IJournaledCommand
    {
        public string CommandTypeName => "test.shared";
        public string AggregateTypeName => "Test";
        public string? AggregateIdOrNull => null;
    }
}
