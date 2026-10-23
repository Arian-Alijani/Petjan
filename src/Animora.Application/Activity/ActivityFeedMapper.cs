using System.Text.Json;
using Animora.Contracts.History;
using Animora.Domain.Events;
using Animora.Domain.Finance;

namespace Animora.Application.Activity;

/// <summary>Persistable, language-neutral projection of one domain event.</summary>
public sealed record ActivityEventDraft(
    string EventId, string TenantId, DateTimeOffset OccurredUtc,
    HistoryEventKind Kind, string TitleKey, string DetailArgsJson,
    string EntityType, string EntityId, long AmountMinor, int AmountSign,
    HistoryEventStatus Status, string ActorUserId = "", string ActorName = "", string? CorrelationId = null);

/// <summary>Single mapping authority shared by live dispatch and deterministic rebuild.</summary>
public static class ActivityFeedMapper
{
    /// <summary>Rehydrate supported domain events from the durable outbox for a rebuild.</summary>
    public static DomainEvent? Deserialize(string type, string payload) => type switch
    {
        nameof(PetRegisteredEvent) => JsonSerializer.Deserialize<PetRegisteredEvent>(payload),
        nameof(PetDeletedEvent) => JsonSerializer.Deserialize<PetDeletedEvent>(payload),
        nameof(OwnerDeletedEvent) => JsonSerializer.Deserialize<OwnerDeletedEvent>(payload),
        nameof(VisitCompletedEvent) => JsonSerializer.Deserialize<VisitCompletedEvent>(payload),
        nameof(VaccinationAdministeredEvent) => JsonSerializer.Deserialize<VaccinationAdministeredEvent>(payload),
        nameof(AppointmentBookedEvent) => JsonSerializer.Deserialize<AppointmentBookedEvent>(payload),
        nameof(AppointmentCancelledEvent) => JsonSerializer.Deserialize<AppointmentCancelledEvent>(payload),
        nameof(StayActivatedEvent) => JsonSerializer.Deserialize<StayActivatedEvent>(payload),
        nameof(StayCheckedOutEvent) => JsonSerializer.Deserialize<StayCheckedOutEvent>(payload),
        nameof(GoodsReceiptPostedEvent) => JsonSerializer.Deserialize<GoodsReceiptPostedEvent>(payload),
        nameof(StockMovementRecordedEvent) => JsonSerializer.Deserialize<StockMovementRecordedEvent>(payload),
        nameof(InvoicePostedEvent) => JsonSerializer.Deserialize<InvoicePostedEvent>(payload),
        nameof(PaymentRegisteredEvent) => JsonSerializer.Deserialize<PaymentRegisteredEvent>(payload),
        nameof(InvoiceVoidedEvent) => JsonSerializer.Deserialize<InvoiceVoidedEvent>(payload),
        nameof(InvoiceRefundedEvent) => JsonSerializer.Deserialize<InvoiceRefundedEvent>(payload),
        nameof(ChequeStateChangedEvent) => JsonSerializer.Deserialize<ChequeStateChangedEvent>(payload),
        nameof(ExpensePostedEvent) => JsonSerializer.Deserialize<ExpensePostedEvent>(payload),
        _ => null,
    };

    public static ActivityEventDraft? Map(DomainEvent e) => e switch
    {
        PetRegisteredEvent x => Draft(x, HistoryEventKind.Patient, ActivityEventKeys.PetRegistered,
            "Pet", x.PetId, args: new { x.OwnerId }, status: HistoryEventStatus.New),
        PetDeletedEvent x => Draft(x, HistoryEventKind.Patient, ActivityEventKeys.PetDeleted,
            "Pet", x.PetId, args: new { x.PetId, x.OwnerId }, status: HistoryEventStatus.Removed, actor: x.ActorUserId),
        OwnerDeletedEvent x => Draft(x, HistoryEventKind.Patient, ActivityEventKeys.OwnerDeleted,
            "Owner", x.OwnerId, args: new { x.OwnerId }, status: HistoryEventStatus.Removed, actor: x.ActorUserId),
        VisitCompletedEvent x => Draft(x, HistoryEventKind.Visit, ActivityEventKeys.VisitCompleted,
            "Visit", x.VisitId, args: new { x.PetId, x.OwnerId }, status: HistoryEventStatus.Done,
            actor: x.DoctorUserId),
        VaccinationAdministeredEvent x => Draft(x, HistoryEventKind.Vaccine, ActivityEventKeys.VaccinationAdministered,
            "Vaccination", x.RecordId, args: new { x.PetId, x.VaccineCode }, status: HistoryEventStatus.Done),
        AppointmentBookedEvent x => Draft(x, HistoryEventKind.Appointment, ActivityEventKeys.AppointmentBooked,
            "Appointment", x.AppointmentId, args: new { x.PetId, x.StartUtc }, status: HistoryEventStatus.Confirmed),
        AppointmentCancelledEvent x => Draft(x, HistoryEventKind.Appointment, ActivityEventKeys.AppointmentCancelled,
            "Appointment", x.AppointmentId, args: new { x.Reason }, status: HistoryEventStatus.Cancelled),
        StayActivatedEvent x => Draft(x, HistoryEventKind.Boarding, ActivityEventKeys.StayActivated,
            "Stay", x.StayId, args: new { x.PetId, x.RoomId }, status: HistoryEventStatus.CheckIn),
        StayCheckedOutEvent x => Draft(x, HistoryEventKind.Boarding, ActivityEventKeys.StayCheckedOut,
            "Stay", x.StayId, args: new { x.PetId, x.RoomId }, status: HistoryEventStatus.Discharge),
        GoodsReceiptPostedEvent x => Draft(x, HistoryEventKind.Purchase, ActivityEventKeys.GoodsReceiptPosted,
            "GoodsReceipt", x.ReceiptId, args: new { x.SupplierId, x.LineCount }, status: HistoryEventStatus.Done),
        StockMovementRecordedEvent x => Draft(x, HistoryEventKind.Inventory, ActivityEventKeys.StockMovementRecorded,
            "StockMovement", x.MovementId, args: new { x.ProductId, x.VariantId, x.QuantityValue }, status: HistoryEventStatus.Normal),
        InvoicePostedEvent x => Draft(x, HistoryEventKind.Sale, ActivityEventKeys.InvoicePosted,
            "Invoice", x.InvoiceId, x.GrandTotalMinor, 1, new { x.OwnerId, x.Currency }, HistoryEventStatus.Unpaid),
        PaymentRegisteredEvent x => Draft(x, HistoryEventKind.Sale, ActivityEventKeys.PaymentRegistered,
            "Payment", x.PaymentId, x.AmountMinor, Math.Sign(x.AmountMinor), new { x.InvoiceId, x.Currency, x.Method },
            x.AmountMinor < 0 ? HistoryEventStatus.Done : HistoryEventStatus.Paid),
        InvoiceVoidedEvent x => Draft(x, HistoryEventKind.Sale, ActivityEventKeys.InvoiceVoided,
            "Invoice", x.InvoiceId, args: new { x.Reason }, status: HistoryEventStatus.Cancelled),
        InvoiceRefundedEvent x => Draft(x, HistoryEventKind.Sale, ActivityEventKeys.InvoiceRefunded,
            "Invoice", x.InvoiceId, x.AmountMinor, -1, new { x.AdjustmentId, x.Currency }, HistoryEventStatus.Done),
        ChequeStateChangedEvent x => Draft(x, HistoryEventKind.Cheque, ActivityEventKeys.ChequeStateChanged,
            "Cheque", x.ChequeId, args: new { from = x.FromState.ToString(), to = x.ToState.ToString() },
            status: x.ToState switch
            {
                ChequeState.Cleared => HistoryEventStatus.Cleared,
                ChequeState.Bounced => HistoryEventStatus.Bounced,
                ChequeState.Cancelled => HistoryEventStatus.Cancelled,
                _ => HistoryEventStatus.Pending,
            }),
        ExpensePostedEvent x => Draft(x, HistoryEventKind.Expense, ActivityEventKeys.ExpensePosted,
            "Expense", x.ExpenseId, x.AmountMinor, x.IsReversal ? 1 : -1, new { category = x.CategoryCode, x.Currency }, HistoryEventStatus.Done),
        _ => null,
    };

    private static ActivityEventDraft Draft(DomainEvent e, HistoryEventKind kind, string key,
        string entityType, string entityId, object? args = null, HistoryEventStatus status = HistoryEventStatus.Normal,
        string actor = "") => Draft(e, kind, key, entityType, entityId, 0, 0, args, status, actor);

    private static ActivityEventDraft Draft(DomainEvent e, HistoryEventKind kind, string key,
        string entityType, string entityId, long amount, int sign, object? args, HistoryEventStatus status,
        string actor = "") => new(e.EventId, e.TenantId, e.OccurredAtUtc, kind, key,
            args is null ? "{}" : JsonSerializer.Serialize(args), entityType, entityId,
            Math.Abs(amount), sign, status, actor);
}
