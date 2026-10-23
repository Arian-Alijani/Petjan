using System;
using System.Linq;
using System.Text.Json;
using Animora.Application.Activity;
using Animora.Contracts.History;
using Animora.Domain.Events;
using Animora.Domain.Finance;
using Animora.Domain.Inventory;
using Animora.Domain.Sales;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>
/// C10: <see cref="ActivityFeedMapper"/> is the single mapping authority shared by live dispatch
/// (<c>ActivityFeedHandler</c>) and the deterministic rebuild (<c>RebuildActivityFeedAsync</c>).
/// These tests pin, for each of the 14 mapped domain-event types: the localization key, the money
/// amount and sign, the history kind/status, and that <see cref="ActivityEventDraft.TitleKey"/> is
/// ASCII (Persian rendering lives only in the Desktop <c>HistoryText</c> — CONVENTIONS §4). They
/// also prove the mapper is a pure, stable function (rebuild = live by construction) and that every
/// other domain event is a no-op.
/// </summary>
public sealed class ActivityFeedTests
{
    private const string Tenant = "T-C10";
    private static readonly DateTimeOffset Occurred = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static DomainEvent[] AllFourteen() =>
    [
        new PetRegisteredEvent("E01", Tenant, Occurred, "PET1", "OWN1"),
        new VisitCompletedEvent("E02", Tenant, Occurred, "VIS1", "PET1", "OWN1", "DOC1"),
        new VaccinationAdministeredEvent("E03", Tenant, Occurred, "VAC1", "PET1", "RABIES", null),
        new AppointmentBookedEvent("E04", Tenant, Occurred, "APP1", "PET1", "OWN1", "DOC1", Occurred, 30),
        new AppointmentCancelledEvent("E05", Tenant, Occurred, "APP1", "owner-request"),
        new StayActivatedEvent("E06", Tenant, Occurred, "STAY1", "PET1", "OWN1", "ROOM1"),
        new StayCheckedOutEvent("E07", Tenant, Occurred, "STAY1", "PET1", "ROOM1", Occurred),
        new GoodsReceiptPostedEvent("E08", Tenant, Occurred, "GRN1", "SUP1", 5),
        new StockMovementRecordedEvent("E09", Tenant, Occurred, "MOV1", "PRD1", "VAR1", MovementKind.AdjustmentOut, 2m),
        new InvoicePostedEvent("E10", Tenant, Occurred, "INV1", "OWN1", 1_250_000, "IRR", null, null),
        new PaymentRegisteredEvent("E11", Tenant, Occurred, "PAY1", "INV1", 900_000, "IRR", PaymentMethod.Cash),
        new InvoiceRefundedEvent("E12", Tenant, Occurred, "INV1", "ADJ1", 400_000, "IRR"),
        new ChequeStateChangedEvent("E13", Tenant, Occurred, "CHQ1", ChequeState.Deposited, ChequeState.Cleared),
        new ExpensePostedEvent("E14", Tenant, Occurred, "EXP1", "RENT", 5_000_000, "IRR", null),
    ];

    [Fact]
    public void EveryMappedEvent_ProducesExactlyOneDraft_WithDocumentedKeyAmountAndSign()
    {
        var expected = new (DomainEvent Event, string Key, long Amount, int Sign, HistoryEventKind Kind)[]
        {
            (new PetRegisteredEvent("E01", Tenant, Occurred, "PET1", "OWN1"),
                ActivityEventKeys.PetRegistered, 0, 0, HistoryEventKind.Patient),
            (new VisitCompletedEvent("E02", Tenant, Occurred, "VIS1", "PET1", "OWN1", "DOC1"),
                ActivityEventKeys.VisitCompleted, 0, 0, HistoryEventKind.Visit),
            (new VaccinationAdministeredEvent("E03", Tenant, Occurred, "VAC1", "PET1", "RABIES", null),
                ActivityEventKeys.VaccinationAdministered, 0, 0, HistoryEventKind.Vaccine),
            (new AppointmentBookedEvent("E04", Tenant, Occurred, "APP1", "PET1", "OWN1", "DOC1", Occurred, 30),
                ActivityEventKeys.AppointmentBooked, 0, 0, HistoryEventKind.Appointment),
            (new AppointmentCancelledEvent("E05", Tenant, Occurred, "APP1", "owner-request"),
                ActivityEventKeys.AppointmentCancelled, 0, 0, HistoryEventKind.Appointment),
            (new StayActivatedEvent("E06", Tenant, Occurred, "STAY1", "PET1", "OWN1", "ROOM1"),
                ActivityEventKeys.StayActivated, 0, 0, HistoryEventKind.Boarding),
            (new StayCheckedOutEvent("E07", Tenant, Occurred, "STAY1", "PET1", "ROOM1", Occurred),
                ActivityEventKeys.StayCheckedOut, 0, 0, HistoryEventKind.Boarding),
            (new GoodsReceiptPostedEvent("E08", Tenant, Occurred, "GRN1", "SUP1", 5),
                ActivityEventKeys.GoodsReceiptPosted, 0, 0, HistoryEventKind.Purchase),
            (new StockMovementRecordedEvent("E09", Tenant, Occurred, "MOV1", "PRD1", "VAR1", MovementKind.AdjustmentOut, 2m),
                ActivityEventKeys.StockMovementRecorded, 0, 0, HistoryEventKind.Inventory),
            (new InvoicePostedEvent("E10", Tenant, Occurred, "INV1", "OWN1", 1_250_000, "IRR", null, null),
                ActivityEventKeys.InvoicePosted, 1_250_000, 1, HistoryEventKind.Sale),
            (new PaymentRegisteredEvent("E11", Tenant, Occurred, "PAY1", "INV1", 900_000, "IRR", PaymentMethod.Cash),
                ActivityEventKeys.PaymentRegistered, 900_000, 1, HistoryEventKind.Sale),
            (new InvoiceRefundedEvent("E12", Tenant, Occurred, "INV1", "ADJ1", 400_000, "IRR"),
                ActivityEventKeys.InvoiceRefunded, 400_000, -1, HistoryEventKind.Sale),
            (new ChequeStateChangedEvent("E13", Tenant, Occurred, "CHQ1", ChequeState.Deposited, ChequeState.Cleared),
                ActivityEventKeys.ChequeStateChanged, 0, 0, HistoryEventKind.Cheque),
            (new ExpensePostedEvent("E14", Tenant, Occurred, "EXP1", "RENT", 5_000_000, "IRR", null),
                ActivityEventKeys.ExpensePosted, 5_000_000, -1, HistoryEventKind.Expense),
        };

        Assert.Equal(14, expected.Length);
        foreach (var (evt, key, amount, sign, kind) in expected)
        {
            var draft = ActivityFeedMapper.Map(evt);
            Assert.NotNull(draft);
            Assert.Equal(key, draft!.TitleKey);
            Assert.Equal(amount, draft.AmountMinor);
            Assert.Equal(sign, draft.AmountSign);
            Assert.Equal(kind, draft.Kind);
            Assert.Equal(evt.EventId, draft.EventId);
            Assert.Equal(Tenant, draft.TenantId);
            Assert.Equal(Occurred, draft.OccurredUtc);
            // Amount is an absolute magnitude; the direction is carried only by the sign.
            Assert.True(draft.AmountMinor >= 0);
        }
    }

    [Fact]
    public void PatientDeletions_MapToRemovedRows_WithActor()
    {
        var pet = ActivityFeedMapper.Map(new PetDeletedEvent("E20", Tenant, Occurred, "PET1", "OWN1", "USR1"));
        Assert.NotNull(pet);
        Assert.Equal(ActivityEventKeys.PetDeleted, pet!.TitleKey);
        Assert.Equal(("Pet", "PET1", HistoryEventStatus.Removed, "USR1"), (pet.EntityType, pet.EntityId, pet.Status, pet.ActorUserId));

        var owner = ActivityFeedMapper.Map(new OwnerDeletedEvent("E21", Tenant, Occurred, "OWN1", "USR1"));
        Assert.NotNull(owner);
        Assert.Equal(ActivityEventKeys.OwnerDeleted, owner!.TitleKey);
        Assert.Equal(("Owner", "OWN1", HistoryEventStatus.Removed, "USR1"), (owner.EntityType, owner.EntityId, owner.Status, owner.ActorUserId));
        Assert.IsType<OwnerDeletedEvent>(ActivityFeedMapper.Deserialize(nameof(OwnerDeletedEvent), JsonSerializer.Serialize(new OwnerDeletedEvent("E21", Tenant, Occurred, "OWN1", "USR1"))));
    }

    [Fact]
    public void ChequeCleared_MapsToClearedStatus_AndSerializesFromToArgs()
    {
        var draft = ActivityFeedMapper.Map(
            new ChequeStateChangedEvent("E13", Tenant, Occurred, "CHQ1", ChequeState.Deposited, ChequeState.Cleared));

        Assert.NotNull(draft);
        Assert.Equal(HistoryEventStatus.Cleared, draft!.Status);
        Assert.Equal("Cheque", draft.EntityType);
        Assert.Equal("CHQ1", draft.EntityId);

        using var doc = JsonDocument.Parse(draft.DetailArgsJson);
        Assert.Equal("Deposited", doc.RootElement.GetProperty("from").GetString());
        Assert.Equal("Cleared", doc.RootElement.GetProperty("to").GetString());
    }

    [Fact]
    public void ExpensePosted_IsNegativeSigned_WithCategoryInDetailArgs()
    {
        var draft = ActivityFeedMapper.Map(
            new ExpensePostedEvent("E14", Tenant, Occurred, "EXP1", "RENT", 5_000_000, "IRR", null));

        Assert.NotNull(draft);
        Assert.Equal(-1, draft!.AmountSign);
        Assert.Equal(5_000_000, draft.AmountMinor);
        Assert.Equal("Expense", draft.EntityType);

        using var doc = JsonDocument.Parse(draft.DetailArgsJson);
        Assert.Equal("RENT", doc.RootElement.GetProperty("category").GetString());
    }

    [Fact]
    public void AllTitleKeys_AreAscii_AndDetailArgsAreValidJson()
    {
        foreach (var evt in AllFourteen())
        {
            var draft = ActivityFeedMapper.Map(evt);
            Assert.NotNull(draft);
            Assert.All(draft!.TitleKey, ch => Assert.True(ch < 128,
                $"Title key '{draft.TitleKey}' must be ASCII — Persian rendering belongs to HistoryText."));
            // Detail args must always be a parseable JSON object (never null / free text).
            using var doc = JsonDocument.Parse(draft.DetailArgsJson);
            Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        }
    }

    [Fact]
    public void CatalogKeys_AreAllAscii_AndUnique()
    {
        var keys = typeof(ActivityEventKeys)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(string))
            .Select(f => (string)f.GetValue(null)!)
            .ToArray();

        Assert.Equal(14, keys.Length);
        Assert.Equal(keys.Length, keys.Distinct().Count());
        Assert.All(keys, k => Assert.All(k, ch => Assert.True(ch < 128)));
    }

    [Fact]
    public void UnmappedEvents_AreNoOps()
    {
        // Events outside the 14 mapped types must not appear in the feed.
        Assert.Null(ActivityFeedMapper.Map(
            new VisitReadyForInvoiceEvent("X1", Tenant, Occurred, "VIS1", "PET1", "OWN1")));
        Assert.Null(ActivityFeedMapper.Map(
            new AppointmentMovedEvent("X2", Tenant, Occurred, "APP1", Occurred, Occurred.AddHours(1), 30)));
        Assert.Null(ActivityFeedMapper.Map(
            new QueueEntryCreatedEvent("X3", Tenant, Occurred, "Q1", "PET1", "OWN1", null)));
    }

    [Fact]
    public void Mapper_IsPure_RebuildReproducesTheLiveDraftExactly()
    {
        // The rebuild path replays the SAME events through the SAME mapper, so a second call must be
        // byte-for-byte identical to the first (this is what makes "rebuild == maintained feed" true).
        foreach (var evt in AllFourteen())
        {
            var live = ActivityFeedMapper.Map(evt);
            var rebuilt = ActivityFeedMapper.Map(evt);
            Assert.Equal(live, rebuilt); // records compare by value across every field.
        }
    }
}
