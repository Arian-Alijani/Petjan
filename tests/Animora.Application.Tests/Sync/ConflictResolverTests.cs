using System.Text.Json;
using Animora.Application.Sync;
using Animora.Contracts.Wire.Commands;
using Animora.Contracts.Wire.Sync;
using Animora.Domain.Results;
using Xunit;

namespace Animora.Application.Tests.Sync;

/// <summary>G04: ARCHITECTURE.md §8.5 policy per data class — pure resolver, no store involved.</summary>
public sealed class ConflictResolverTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 10, 0, 0, TimeSpan.Zero);
    private readonly ConflictResolver _resolver = new();

    private static SyncRecord Record(string entity, string json = "{\"name\":\"x\",\"phone\":\"1\"}", char op = 'U', DateTimeOffset? at = null) =>
        new(entity, "id1", op, "v2", at ?? T0, op == 'D', op == 'D' ? null : JsonDocument.Parse(json).RootElement.Clone());

    private static PendingOverlay Pending(DateTimeOffset? local = null, bool collides = false, params string[] touched) =>
        new(true, local, new HashSet<string>(touched, StringComparer.Ordinal), collides);

    [Fact]
    public void UiPreference_LastWriteWins()
    {
        var newerLocal = _resolver.Decide(Record("UserPreference", at: T0), Pending(T0.AddMinutes(5)));
        Assert.Equal(ConflictAction.KeepLocal, newerLocal.Action);
        Assert.False(newerLocal.ApplyInbound);

        var newerRemote = _resolver.Decide(Record("UserPreference", at: T0.AddMinutes(9)), Pending(T0));
        Assert.Equal(ConflictAction.ApplyInbound, newerRemote.Action);
        Assert.True(newerRemote.ApplyInbound);
    }

    [Fact]
    public void PostedFinancial_IsReversalOnly_NeverInPlace()
    {
        foreach (var entity in new[] { "Invoice", "Payment" })
        {
            var decision = _resolver.Decide(Record(entity), Pending(T0.AddHours(1)));

            Assert.Equal(ConflictAction.ReversalOnly, decision.Action);
            Assert.True(decision.FlagNeedsAttention);
            Assert.Equal(FinancialEffect.Unknown, decision.FinancialEffect);
            Assert.True(decision.RequiresReversingCommand);
            Assert.True(decision.OverlayPreserved);
            Assert.Null(decision.Error);
        }

        // A server tombstone for a posted fact is still never an in-place delete of the local post.
        var tombstone = _resolver.Decide(Record("Invoice", op: 'D'), Pending());
        Assert.Equal(ConflictAction.ReversalOnly, tombstone.Action);
        Assert.True(tombstone.RequiresReversingCommand);
    }

    [Fact]
    public void InventoryMovements_BothApply_Additive()
    {
        var decision = _resolver.Decide(Record("StockMovement"), Pending());

        Assert.Equal(ConflictAction.ApplyInbound, decision.Action);
        Assert.True(decision.ApplyInbound);
        Assert.True(decision.OverlayPreserved);
        Assert.True(decision.RederiveOnHand);
    }

    [Fact]
    public void AppointmentSlotCollision_Rejected()
    {
        var collision = _resolver.Decide(Record("Appointment"), Pending(collides: true));
        Assert.Equal(ConflictAction.RejectLocalIntent, collision.Action);
        Assert.Equal(ErrorCodes.AppointmentSlotConflict, collision.Error);

        var free = _resolver.Decide(Record("Appointment"), Pending(collides: false));
        Assert.Equal(ConflictAction.ApplyInbound, free.Action);
        Assert.Null(free.Error);
    }

    [Fact]
    public void Demographics_FieldLevelMerge_NotWholeRowLww()
    {
        // The local command changed "phone" only; the local row is NEWER, yet the untouched inbound field still merges.
        var decision = _resolver.Decide(Record("Owner", at: T0), Pending(T0.AddDays(1), touched: "phone"));

        Assert.Equal(ConflictAction.MergeFields, decision.Action);
        Assert.Equal(["name"], decision.MergedFields);
        Assert.Equal(["phone"], decision.KeptLocalFields);
        Assert.True(decision.OverlayPreserved);
    }

    [Fact]
    public void LedgerAppendOnly_CannotConflict()
    {
        var decision = _resolver.Decide(Record("LedgerEntry"), Pending());

        Assert.Equal(ConflictAction.ApplyInbound, decision.Action);
        Assert.True(decision.OverlayPreserved);
        Assert.False(decision.FlagNeedsAttention);
        Assert.Null(decision.Error);
    }

    [Fact]
    public void NoPendingIntent_AppliesInbound_AndUnknownEntityUsesSafeDefault()
    {
        Assert.Equal(ConflictAction.ApplyInbound, _resolver.Decide(Record("Invoice"), null).Action);
        Assert.Equal(ConflictClass.Demographics, ConflictPolicyCatalog.ClassOf("SomethingNew"));
        Assert.False(ConflictPolicyCatalog.IsKnown("SomethingNew"));
    }
}
