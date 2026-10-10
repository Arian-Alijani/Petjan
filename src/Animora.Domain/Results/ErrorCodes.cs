namespace Animora.Domain.Results;

/// <summary>
/// The <b>central catalog</b> of error codes for the whole product. Later phases only
/// <i>add</i> members here, grouped in a region per bounded context; no phase ever renames
/// or removes an existing code — the Desktop's Persian message map and every stored
/// command result key off these strings.
/// </summary>
public static class ErrorCodes
{
    #region Cross-cutting

    /// <summary>Input failed validation. <c>Detail</c> names the offending field(s).</summary>
    public static readonly ErrorCode ValidationFailed =
        new("VALIDATION_FAILED", ErrorCode.Validation);

    /// <summary>The addressed entity does not exist or is not visible to this tenant.</summary>
    public static readonly ErrorCode NotFound =
        new("NOT_FOUND", ErrorCode.NotFound);

    /// <summary>
    /// An opaque version token did not match. Detection only — the domain policy decides
    /// what to do (A-RULE 004); this is never resolved by blind last-writer-wins.
    /// </summary>
    public static readonly ErrorCode ConcurrencyConflict =
        new("CONCURRENCY_CONFLICT", ErrorCode.Conflict);

    /// <summary>The actor is authenticated but lacks the required permission.</summary>
    public static readonly ErrorCode PermissionDenied =
        new("PERMISSION_DENIED", ErrorCode.Forbidden);

    /// <summary>The requested permission code is not in the catalog (B04a); fail closed.</summary>
    public static readonly ErrorCode PermissionUnknown =
        new("PERMISSION_UNKNOWN", ErrorCode.Forbidden);

    /// <summary>The request addressed data belonging to a different tenant.</summary>
    public static readonly ErrorCode TenantMismatch =
        new("TENANT_MISMATCH", ErrorCode.Forbidden);

    /// <summary>
    /// The same OperationId arrived with a different command fingerprint: a
    /// security/integrity event, audited, never silently accepted (A-RULE 019).
    /// </summary>
    public static readonly ErrorCode IdempotencyFingerprintMismatch =
        new("IDEMPOTENCY_FINGERPRINT_MISMATCH", ErrorCode.Integrity);

    /// <summary>
    /// A command this one depends on has not settled yet. Dependency ordering is resolved
    /// server-side only (A-RULE 021).
    /// </summary>
    public static readonly ErrorCode DependencyUnsettled =
        new("DEPENDENCY_UNSETTLED", ErrorCode.Conflict);

    /// <summary>The tenant's licence/plan does not include the requested capability.</summary>
    public static readonly ErrorCode EntitlementRequired =
        new("ENTITLEMENT_REQUIRED", ErrorCode.Entitlement);

    /// <summary>The cached offline capability window has elapsed; a re-check is required.</summary>
    public static readonly ErrorCode OfflineCapabilityExpired =
        new("OFFLINE_CAPABILITY_EXPIRED", ErrorCode.Entitlement);

    /// <summary>The local or server store could not be reached or read.</summary>
    public static readonly ErrorCode StoreUnavailable =
        new("STORE_UNAVAILABLE", ErrorCode.Infrastructure);

    /// <summary>A mandatory audit row could not be enlisted or persisted; the operation must roll back.</summary>
    public static readonly ErrorCode AuditWriteFailed =
        new("AUDIT_WRITE_FAILED", ErrorCode.Infrastructure);

    /// <summary>A redacted local diagnostics archive could not be assembled.</summary>
    public static readonly ErrorCode DiagnosticsBundleFailed =
        new("DIAGNOSTICS_BUNDLE_FAILED", ErrorCode.Infrastructure);

    /// <summary>A marked client crash requires local-store validation and lease recovery.</summary>
    public static readonly ErrorCode CrashRecoveryRequired =
        new("CRASH_RECOVERY_REQUIRED", ErrorCode.Infrastructure);

    /// <summary>An unclassified defect. Always logged with its exception.</summary>
    public static readonly ErrorCode Unexpected =
        new("UNEXPECTED", ErrorCode.Infrastructure);

    #endregion

    #region Identity (Phase A03)

    /// <summary>The tenant is suspended or closed; writes are refused.</summary>
    public static readonly ErrorCode TenantSuspended =
        new("TENANT_SUSPENDED", ErrorCode.Forbidden);

    /// <summary>A normalized login name is already registered in this tenant.</summary>
    public static readonly ErrorCode UsernameExists =
        new("USERNAME_EXISTS", ErrorCode.Conflict);

    /// <summary>The account exists but is administratively disabled.</summary>
    public static readonly ErrorCode UserDisabled =
        new("USER_DISABLED", ErrorCode.Forbidden);

    /// <summary>Too many failed attempts; the account is locked until its lockout ends.</summary>
    public static readonly ErrorCode UserLockedOut =
        new("USER_LOCKED_OUT", ErrorCode.Forbidden);

    /// <summary>
    /// No active <c>StaffMembership</c> links this user to the tenant — the only source of a
    /// <c>(User, Tenant)</c> link (A-RULE 012).
    /// </summary>
    public static readonly ErrorCode MembershipNotActive =
        new("MEMBERSHIP_NOT_ACTIVE", ErrorCode.Forbidden);

    /// <summary>The device the request came from has been revoked.</summary>
    public static readonly ErrorCode DeviceRevoked =
        new("DEVICE_REVOKED", ErrorCode.Forbidden);

    /// <summary>
    /// The capability is not present in the device's capability snapshot, or its own expiry has
    /// passed. Fail closed (A-RULE 029).
    /// </summary>
    public static readonly ErrorCode CapabilityNotGranted =
        new("CAPABILITY_NOT_GRANTED", ErrorCode.Forbidden);

    /// <summary>The local capability snapshot failed signature verification; it must be discarded (B04b).</summary>
    public static readonly ErrorCode CapabilitySnapshotTampered =
        new("CAPABILITY_SNAPSHOT_TAMPERED", ErrorCode.Forbidden);

    /// <summary>No capability snapshot exists for this tenant/user/device (B04b). Fail closed.</summary>
    public static readonly ErrorCode CapabilitySnapshotMissing =
        new("CAPABILITY_SNAPSHOT_MISSING", ErrorCode.Forbidden);

    /// <summary>Unknown user name or wrong password (deliberately indistinguishable) (B03).</summary>
    public static readonly ErrorCode CredentialsInvalid =
        new("CREDENTIALS_INVALID", ErrorCode.Forbidden);

    /// <summary>The local session passed its absolute lifetime; sign in again (B03).</summary>
    public static readonly ErrorCode SessionExpired =
        new("SESSION_EXPIRED", ErrorCode.Forbidden);

    /// <summary>The password does not meet the fixed policy (≥ 8 chars, a letter and a digit) (B03).</summary>
    public static readonly ErrorCode PasswordPolicyViolation =
        new("PASSWORD_POLICY_VIOLATION", ErrorCode.Validation);

    /// <summary>First-run owner setup was requested but a user already exists (B03).</summary>
    public static readonly ErrorCode FirstRunAlreadyCompleted =
        new("FIRST_RUN_ALREADY_COMPLETED", ErrorCode.Conflict);

    #endregion

    #region Patients & Clinical (Phase A04)

    /// <summary>
    /// A species change was attempted. Species is fixed at registration — the whole clinical history
    /// is interpreted through it, so a correction is a new patient record.
    /// </summary>
    public static readonly ErrorCode PetSpeciesImmutable =
        new("PET_SPECIES_IMMUTABLE", ErrorCode.Integrity);

    /// <summary>The requested visit state change is not in the encounter state machine.</summary>
    public static readonly ErrorCode VisitInvalidTransition =
        new("VISIT_INVALID_TRANSITION", ErrorCode.Conflict);

    /// <summary>A visit cannot be completed without a diagnosis.</summary>
    public static readonly ErrorCode VisitDiagnosisRequired =
        new("VISIT_DIAGNOSIS_REQUIRED", ErrorCode.Validation);

    /// <summary>The visit has been invoiced; its prescription and billable lines are final.</summary>
    public static readonly ErrorCode VisitLocked =
        new("VISIT_LOCKED", ErrorCode.Conflict);

    /// <summary>The weight reading is outside the plausible range and is a data-entry error.</summary>
    public static readonly ErrorCode WeightOutOfRange =
        new("WEIGHT_OUT_OF_RANGE", ErrorCode.Validation);

    /// <summary>A document must reference at least one of owner, pet or visit.</summary>
    public static readonly ErrorCode DocumentTargetRequired =
        new("DOCUMENT_TARGET_REQUIRED", ErrorCode.Validation);

    /// <summary>The dose number is not valid for the vaccine series.</summary>
    public static readonly ErrorCode VaccineDoseInvalid =
        new("VACCINE_DOSE_INVALID", ErrorCode.Validation);

    /// <summary>The addressed owner does not exist or is not visible to this tenant (C01).</summary>
    public static readonly ErrorCode OwnerNotFound = new("OWNER_NOT_FOUND", ErrorCode.NotFound);

    /// <summary>The addressed pet does not exist or is not visible to this tenant (C01).</summary>
    public static readonly ErrorCode PetNotFound = new("PET_NOT_FOUND", ErrorCode.NotFound);

    /// <summary>The owner still has active pets, so the requested change is refused (C01).</summary>
    public static readonly ErrorCode OwnerHasActivePets = new("OWNER_HAS_ACTIVE_PETS", ErrorCode.Conflict);

    /// <summary>The weight measurement is missing, non-positive or otherwise not a valid reading (C01).</summary>
    public static readonly ErrorCode WeightEntryInvalid = new("WEIGHT_ENTRY_INVALID", ErrorCode.Validation);

    public static readonly ErrorCode VisitNotFound = new("VISIT_NOT_FOUND", ErrorCode.NotFound);
    public static readonly ErrorCode VisitNotEditable = new("VISIT_NOT_EDITABLE", ErrorCode.Conflict);
    public static readonly ErrorCode VisitIncomplete = new("VISIT_INCOMPLETE", ErrorCode.Validation);
    public static readonly ErrorCode RxLineNotFound = new("RX_LINE_NOT_FOUND", ErrorCode.NotFound);
    public static readonly ErrorCode VaccinationNotFound = new("VACCINATION_NOT_FOUND", ErrorCode.NotFound);
    public static readonly ErrorCode VaccinationAlreadyDone = new("VACCINATION_ALREADY_DONE", ErrorCode.Conflict);
    public static readonly ErrorCode SmsTemplateTokenUnknown = new("SMS_TEMPLATE_TOKEN_UNKNOWN", ErrorCode.Validation);

    /// <summary>The addressed treatment plan does not exist or is not visible to this tenant.</summary>
    public static readonly ErrorCode TreatmentPlanNotFound = new("TREATMENT_PLAN_NOT_FOUND", ErrorCode.NotFound);

    /// <summary>The addressed treatment occurrence does not exist or is not visible to this tenant.</summary>
    public static readonly ErrorCode TreatmentOccurrenceNotFound = new("TREATMENT_OCCURRENCE_NOT_FOUND", ErrorCode.NotFound);

    /// <summary>The occurrence was already given (or skipped); it cannot be completed twice.</summary>
    public static readonly ErrorCode TreatmentOccurrenceAlreadyDone = new("TREATMENT_OCCURRENCE_ALREADY_DONE", ErrorCode.Conflict);

    /// <summary>The plan is completed or cancelled, so the requested change is refused.</summary>
    public static readonly ErrorCode TreatmentPlanNotActive = new("TREATMENT_PLAN_NOT_ACTIVE", ErrorCode.Conflict);

    /// <summary>The schedule parameters are inconsistent or yield no dates.</summary>
    public static readonly ErrorCode TreatmentScheduleInvalid = new("TREATMENT_SCHEDULE_INVALID", ErrorCode.Validation);

    /// <summary>The schedule would exceed the occurrence or time-span cap; it is refused, never silently truncated.</summary>
    public static readonly ErrorCode TreatmentScheduleTooLarge = new("TREATMENT_SCHEDULE_TOO_LARGE", ErrorCode.Validation);

    /// <summary>A vaccine with this code already exists in the built-in or the clinic catalog.</summary>
    public static readonly ErrorCode VaccineCatalogCodeTaken = new("VACCINE_CATALOG_CODE_TAKEN", ErrorCode.Conflict);

    #endregion

    #region Scheduling & Boarding (Phase A05)

    /// <summary>
    /// The candidate slot overlaps an existing, non-terminal appointment for the same doctor.
    /// Decided by <c>SlotConflictPolicy</c> (ARCHITECTURE.md §8.5) — reject, never last-write-wins.
    /// <c>Detail</c> carries the conflicting appointment id.
    /// </summary>
    public static readonly ErrorCode AppointmentSlotConflict =
        new("APPOINTMENT_SLOT_CONFLICT", ErrorCode.Conflict);

    /// <summary>The requested appointment state change is not in the appointment state machine.</summary>
    public static readonly ErrorCode AppointmentInvalidTransition =
        new("APPOINTMENT_INVALID_TRANSITION", ErrorCode.Conflict);

    /// <summary>The appointment is in visit or later; its slot is fixed and can no longer be moved.</summary>
    public static readonly ErrorCode AppointmentNotMovable =
        new("APPOINTMENT_NOT_MOVABLE", ErrorCode.Conflict);

    /// <summary>The duration is outside [5, 480] minutes or is not a multiple of five.</summary>
    public static readonly ErrorCode AppointmentDurationInvalid =
        new("APPOINTMENT_DURATION_INVALID", ErrorCode.Validation);

    /// <summary>No appointment exists for the supplied id (C04).</summary>
    public static readonly ErrorCode AppointmentNotFound =
        new("APPOINTMENT_NOT_FOUND", ErrorCode.NotFound);

    /// <summary>
    /// The requested appointment action is illegal for the appointment's current state (C04) —
    /// e.g. cancelling an already checked-in appointment or checking in a terminal one.
    /// </summary>
    public static readonly ErrorCode AppointmentStateInvalid =
        new("APPOINTMENT_STATE_INVALID", ErrorCode.Conflict);

    /// <summary>The requested calendar range exceeds the 62-day clamp (C04, CONVENTIONS §5).</summary>
    public static readonly ErrorCode AppointmentRangeTooWide =
        new("APPOINTMENT_RANGE_TOO_WIDE", ErrorCode.Validation);

    /// <summary>The requested slot falls outside the doctor's configured working window (C04).</summary>
    public static readonly ErrorCode DoctorOutsideWorkingHours =
        new("DOCTOR_OUTSIDE_WORKING_HOURS", ErrorCode.Conflict);

    /// <summary>The requested reception-queue state change is not in the queue state machine.</summary>
    public static readonly ErrorCode QueueInvalidTransition =
        new("QUEUE_INVALID_TRANSITION", ErrorCode.Conflict);

    /// <summary>A reception queue ticket id could not be parsed or resolves to no ticket (C02).</summary>
    public static readonly ErrorCode TicketNotFound =
        new("TICKET_NOT_FOUND", ErrorCode.NotFound);

    /// <summary>The requested ticket transition is not legal from the ticket's current state (C02).</summary>
    public static readonly ErrorCode TicketStateInvalid =
        new("TICKET_STATE_INVALID", ErrorCode.Conflict);

    /// <summary>A walk-in registration was submitted without the required contact phone (C02).</summary>
    public static readonly ErrorCode WalkInPhoneRequired =
        new("WALKIN_PHONE_REQUIRED", ErrorCode.Validation);

    /// <summary>The addressed doctor is not an active staff member available for this action (C02).</summary>
    public static readonly ErrorCode DoctorNotAvailable =
        new("DOCTOR_NOT_AVAILABLE", ErrorCode.Conflict);

    /// <summary>The room is retired or otherwise not available for the requested action.</summary>
    public static readonly ErrorCode RoomUnavailable =
        new("ROOM_UNAVAILABLE", ErrorCode.Conflict);

    /// <summary>The room capacity is outside the plausible range [1, 10].</summary>
    public static readonly ErrorCode RoomCapacityInvalid =
        new("ROOM_CAPACITY_INVALID", ErrorCode.Validation);

    /// <summary>The requested boarding-stay state change is not in the stay state machine.</summary>
    public static readonly ErrorCode StayInvalidTransition =
        new("STAY_INVALID_TRANSITION", ErrorCode.Conflict);

    /// <summary>The stay has been invoiced; its items are final and can no longer change.</summary>
    public static readonly ErrorCode StayLocked =
        new("STAY_LOCKED", ErrorCode.Conflict);

    /// <summary>A stay cannot be checked out without an attached invoice — never checked out unbilled.</summary>
    public static readonly ErrorCode StayInvoiceRequired =
        new("STAY_INVOICE_REQUIRED", ErrorCode.Conflict);

    /// <summary>The planned check-out is not strictly after the check-in.</summary>
    public static readonly ErrorCode StayDatesInvalid =
        new("STAY_DATES_INVALID", ErrorCode.Validation);

    /// <summary>
    /// A prepaid stay's room invoice is still waiting at the cashier (an unposted handoff exists), so the
    /// stay cannot be discharged yet. Keeps at most one open billing handoff per stay.
    /// </summary>
    public static readonly ErrorCode StayPrepaidInvoicePending =
        new("STAY_PREPAID_INVOICE_PENDING", ErrorCode.Conflict);

    /// <summary>The addressed boarding stay does not exist.</summary>
    public static readonly ErrorCode StayNotFound = new("STAY_NOT_FOUND", ErrorCode.NotFound);
    /// <summary>The stay cannot be changed in its current state.</summary>
    public static readonly ErrorCode StayNotEditable = new("STAY_NOT_EDITABLE", ErrorCode.Conflict);
    /// <summary>The item at the supplied index changed since it was read.</summary>
    public static readonly ErrorCode StayItemIndexStale = new("STAY_ITEM_INDEX_STALE", ErrorCode.Conflict);
    /// <summary>The requested room has reached its maximum active occupancy.</summary>
    public static readonly ErrorCode RoomAtCapacity = new("ROOM_AT_CAPACITY", ErrorCode.Conflict);
    /// <summary>No compatible room has available capacity.</summary>
    public static readonly ErrorCode RoomNoneAvailable = new("ROOM_NONE_AVAILABLE", ErrorCode.Conflict);
    /// <summary>Another room of this clinic already uses the supplied code.</summary>
    public static readonly ErrorCode RoomCodeDuplicate = new("ROOM_CODE_DUPLICATE", ErrorCode.Conflict);
    /// <summary>The addressed boarding room does not exist.</summary>
    public static readonly ErrorCode RoomNotFound = new("ROOM_NOT_FOUND", ErrorCode.NotFound);
    /// <summary>The stay has already been handed to billing.</summary>
    public static readonly ErrorCode StayAlreadyCheckedOut = new("STAY_ALREADY_CHECKED_OUT", ErrorCode.Conflict);

    #endregion

    #region Inventory & Purchasing (Phase A06)

    /// <summary>
    /// A service product was marked stock-tracked. Labour has no ledger, so a service never
    /// carries stock.
    /// </summary>
    public static readonly ErrorCode ServiceNoStock =
        new("SERVICE_NO_STOCK", ErrorCode.Integrity);

    /// <summary>
    /// The movement's quantity sign contradicts its kind: inbound kinds require a positive
    /// quantity, outbound kinds a negative one, and zero is never a movement.
    /// </summary>
    public static readonly ErrorCode MovementSignInvalid =
        new("MOVEMENT_SIGN_INVALID", ErrorCode.Integrity);

    /// <summary>
    /// The outbound quantity exceeds the derived on-hand and the tenant does not allow negative
    /// stock.
    /// </summary>
    public static readonly ErrorCode InsufficientStock =
        new("INSUFFICIENT_STOCK", ErrorCode.Conflict);

    /// <summary>Two quantities in different units were combined. Units are never converted silently.</summary>
    public static readonly ErrorCode UnitMismatch =
        new("UNIT_MISMATCH", ErrorCode.Validation);

    /// <summary>The category move would make the category its own ancestor.</summary>
    public static readonly ErrorCode CategoryCycle =
        new("CATEGORY_CYCLE", ErrorCode.Integrity);

    /// <summary>
    /// A stock-tracked product must keep at least one active variant — every movement references a
    /// variant, never a bare product.
    /// </summary>
    public static readonly ErrorCode VariantRequired =
        new("VARIANT_REQUIRED", ErrorCode.Integrity);

    /// <summary>The requested purchase-order state change is not in the order state machine.</summary>
    public static readonly ErrorCode PurchaseOrderInvalidTransition =
        new("PO_INVALID_TRANSITION", ErrorCode.Conflict);

    /// <summary>The purchase order has been submitted; its lines are final.</summary>
    public static readonly ErrorCode PurchaseOrderLocked =
        new("PO_LOCKED", ErrorCode.Conflict);

    /// <summary>The stored order totals disagree with the sum of its lines.</summary>
    public static readonly ErrorCode PurchaseOrderTotalMismatch =
        new("PO_TOTAL_MISMATCH", ErrorCode.Integrity);

    /// <summary>The goods receipt is posted; it is immutable and a correction is a reversing receipt.</summary>
    public static readonly ErrorCode ReceiptLocked =
        new("RECEIPT_LOCKED", ErrorCode.Conflict);

    /// <summary>The SKU is already used by another product or variant of this tenant.</summary>
    public static readonly ErrorCode ProductSkuDuplicate =
        new("PRODUCT_SKU_DUPLICATE", ErrorCode.Conflict);

    #endregion

    #region Inventory & Purchasing (Phase C06)

    public static readonly ErrorCode ProductNotFound = new("PRODUCT_NOT_FOUND", ErrorCode.NotFound);
    public static readonly ErrorCode ProductHasStock = new("PRODUCT_HAS_STOCK", ErrorCode.Conflict);
    public static readonly ErrorCode CategoryNotFound = new("CATEGORY_NOT_FOUND", ErrorCode.NotFound);
    public static readonly ErrorCode CategoryNameDuplicate = new("CATEGORY_NAME_DUPLICATE", ErrorCode.Conflict);
    public static readonly ErrorCode CategoryInUse = new("CATEGORY_IN_USE", ErrorCode.Conflict);
    public static readonly ErrorCode VariantNotFound = new("VARIANT_NOT_FOUND", ErrorCode.NotFound);
    public static readonly ErrorCode AdjustmentReasonInvalid = new("ADJUSTMENT_REASON_INVALID", ErrorCode.Validation);
    public static readonly ErrorCode StockWouldGoNegative = new("STOCK_WOULD_GO_NEGATIVE", ErrorCode.Conflict);
    public static readonly ErrorCode PurchaseOrderNotFound = new("PURCHASE_ORDER_NOT_FOUND", ErrorCode.NotFound);
    public static readonly ErrorCode PurchaseOrderStateInvalid = new("PURCHASE_ORDER_STATE_INVALID", ErrorCode.Conflict);
    public static readonly ErrorCode ReceiptExceedsOrder = new("RECEIPT_EXCEEDS_ORDER", ErrorCode.Conflict);

    #endregion

    #region Sales & Cashier (Phase A07)

    /// <summary>
    /// The invoice is posted; its lines, discount and insurance are final. A change after posting
    /// is an <c>InvoiceAdjustment</c>, never an edit (A-RULE 002).
    /// </summary>
    public static readonly ErrorCode InvoiceImmutable =
        new("INVOICE_IMMUTABLE", ErrorCode.Conflict);

    /// <summary>The requested invoice state change is not in the invoice state machine.</summary>
    public static readonly ErrorCode InvoiceInvalidTransition =
        new("INVOICE_INVALID_TRANSITION", ErrorCode.Conflict);

    /// <summary>The stored invoice totals disagree with a recomputation from the lines.</summary>
    public static readonly ErrorCode InvoiceTotalMismatch =
        new("INVOICE_TOTAL_MISMATCH", ErrorCode.Integrity);

    /// <summary>The invoice's grand total came out below zero; a claim is never negative.</summary>
    public static readonly ErrorCode InvoiceNegativeTotal =
        new("INVOICE_NEGATIVE_TOTAL", ErrorCode.Integrity);

    /// <summary>The invoice has no lines, so there is nothing to post.</summary>
    public static readonly ErrorCode InvoiceEmpty =
        new("INVOICE_EMPTY", ErrorCode.Validation);

    /// <summary>Money was already received, so the invoice must be refunded rather than voided.</summary>
    public static readonly ErrorCode InvoiceVoidAfterPayment =
        new("INVOICE_VOID_AFTER_PAYMENT", ErrorCode.Conflict);

    /// <summary>The payment is larger than the amount still due on the invoice.</summary>
    public static readonly ErrorCode PaymentExceedsDue =
        new("PAYMENT_EXCEEDS_DUE", ErrorCode.Validation);

    /// <summary>
    /// A payment carrying an already-applied <c>OperationId</c> was replayed. The replay is a
    /// no-op, not a second financial effect (A-RULE 006).
    /// </summary>
    public static readonly ErrorCode PaymentDuplicateOperation =
        new("PAYMENT_DUPLICATE_OPERATION", ErrorCode.Conflict);

    /// <summary>
    /// The payment method needs a reference the caller did not supply: cash needs its cash
    /// session, a cheque needs its cheque record.
    /// </summary>
    public static readonly ErrorCode PaymentMethodReferenceRequired =
        new("PAYMENT_METHOD_REFERENCE_REQUIRED", ErrorCode.Validation);

    /// <summary>This cashier already has an open session on this terminal.</summary>
    public static readonly ErrorCode CashSessionAlreadyOpen =
        new("CASH_SESSION_ALREADY_OPEN", ErrorCode.Conflict);

    /// <summary>Cash payments require an active cash drawer.</summary>
    public static readonly ErrorCode CashSessionRequired = new("CASH_SESSION_REQUIRED", ErrorCode.Validation);

    /// <summary>The cash session is closed; a correction belongs to the next session.</summary>
    public static readonly ErrorCode CashSessionClosed =
        new("CASH_SESSION_CLOSED", ErrorCode.Conflict);

    /// <summary>The declared count differs from the expected one and no explanation was given.</summary>
    public static readonly ErrorCode CashVarianceNoteRequired =
        new("CASH_VARIANCE_NOTE_REQUIRED", ErrorCode.Validation);

    /// <summary>The drawer still has commands awaiting settlement; it cannot be closed yet.</summary>
    public static readonly ErrorCode CashSessionUnsettledCommands =
        new("CASH_SESSION_UNSETTLED_COMMANDS", ErrorCode.Conflict);

    /// <summary>The adjustment gives back more than was ever received on the invoice.</summary>
    public static readonly ErrorCode AdjustmentExceedsPaid =
        new("ADJUSTMENT_EXCEEDS_PAID", ErrorCode.Validation);

    /// <summary>The addressed invoice does not exist or is not visible to this tenant.</summary>
    public static readonly ErrorCode InvoiceNotFound =
        new("INVOICE_NOT_FOUND", ErrorCode.NotFound);

    /// <summary>The invoice line at the supplied index changed since the caller last read it.</summary>
    public static readonly ErrorCode InvoiceLineIndexStale =
        new("INVOICE_LINE_INDEX_STALE", ErrorCode.Conflict);

    /// <summary>A posted invoice must identify the owner being billed.</summary>
    public static readonly ErrorCode InvoiceOwnerRequired =
        new("INVOICE_OWNER_REQUIRED", ErrorCode.Validation);

    /// <summary>The visit or boarding handoff has already been attached to an invoice.</summary>
    public static readonly ErrorCode HandoffAlreadyInvoiced =
        new("HANDOFF_ALREADY_INVOICED", ErrorCode.Conflict);

    /// <summary>The addressed insurer does not exist or is not visible to this tenant.</summary>
    public static readonly ErrorCode InsurerNotFound =
        new("INSURER_NOT_FOUND", ErrorCode.NotFound);

    /// <summary>Another insurer of this tenant already uses the name.</summary>
    public static readonly ErrorCode InsurerNameDuplicate =
        new("INSURER_NAME_DUPLICATE", ErrorCode.Conflict);

    #endregion

    #region Finance (Phase A08)

    /// <summary>The entry's debits and credits do not sum to the same amount.</summary>
    public static readonly ErrorCode JournalUnbalanced =
        new("JOURNAL_UNBALANCED", ErrorCode.Integrity);

    /// <summary>A double entry needs at least two lines; one line is not an entry.</summary>
    public static readonly ErrorCode JournalTooFewLines =
        new("JOURNAL_TOO_FEW_LINES", ErrorCode.Validation);

    /// <summary>
    /// A journal line amount was zero or negative. Direction lives in the line's side, never in
    /// the sign of its amount.
    /// </summary>
    public static readonly ErrorCode JournalLineAmountInvalid =
        new("JOURNAL_LINE_AMOUNT_INVALID", ErrorCode.Validation);

    /// <summary>
    /// The entry mixes currencies. Multi-currency ledgers are out of scope for the product: one
    /// entry is always one currency.
    /// </summary>
    public static readonly ErrorCode JournalCurrencyMixed =
        new("JOURNAL_CURRENCY_MIXED", ErrorCode.Validation);

    /// <summary>The line names a roll-up header account, which takes no direct posting.</summary>
    public static readonly ErrorCode AccountNotPostable =
        new("ACCOUNT_NOT_POSTABLE", ErrorCode.Integrity);

    /// <summary>The line names an account that exists in neither the default chart nor the tenant's.</summary>
    public static readonly ErrorCode AccountUnknown =
        new("ACCOUNT_UNKNOWN", ErrorCode.NotFound);

    /// <summary>
    /// The accounting period is closed or locked. A post-close correction is an entry in the next
    /// open period, never a retroactive edit of a closed book (A-RULE 003).
    /// </summary>
    public static readonly ErrorCode PeriodClosed =
        new("PERIOD_CLOSED", ErrorCode.Conflict);

    public static readonly ErrorCode PeriodNotFound =
        new("PERIOD_NOT_FOUND", ErrorCode.NotFound);

    public static readonly ErrorCode FinanceRangeInvalid =
        new("FINANCE_RANGE_INVALID", ErrorCode.Validation);

    public static readonly ErrorCode ExpenseCategoryInvalid =
        new("EXPENSE_CATEGORY_INVALID", ErrorCode.Validation);

    /// <summary>
    /// The period cannot close yet: unsettled commands, an open cash session or a draft invoice is
    /// still in flight. <c>Detail</c> names the blocker.
    /// </summary>
    public static readonly ErrorCode PeriodCloseBlocked =
        new("PERIOD_CLOSE_BLOCKED", ErrorCode.Conflict);

    /// <summary>The requested period state change is not in the period state machine (no reopen).</summary>
    public static readonly ErrorCode PeriodInvalidTransition =
        new("PERIOD_INVALID_TRANSITION", ErrorCode.Conflict);

    /// <summary>The requested cheque state change is not in the cheque state machine.</summary>
    public static readonly ErrorCode ChequeInvalidTransition =
        new("CHEQUE_INVALID_TRANSITION", ErrorCode.Conflict);

    /// <summary>
    /// The cheque names no counterparty: an inbound cheque needs its owner, an outbound one its
    /// supplier.
    /// </summary>
    public static readonly ErrorCode ChequeCounterpartyRequired =
        new("CHEQUE_COUNTERPARTY_REQUIRED", ErrorCode.Validation);

    /// <summary>The addressed cheque does not exist for this tenant.</summary>
    public static readonly ErrorCode ChequeNotFound = new("CHEQUE_NOT_FOUND", ErrorCode.NotFound);

    /// <summary>Only a newly registered cheque may have its metadata edited or be removed.</summary>
    public static readonly ErrorCode ChequeNotEditable = new("CHEQUE_NOT_EDITABLE", ErrorCode.Conflict);

    /// <summary>The expense is posted; a correction is a reversing expense, never an edit.</summary>
    public static readonly ErrorCode ExpenseImmutable =
        new("EXPENSE_IMMUTABLE", ErrorCode.Conflict);

    /// <summary>The addressed expense does not exist for this tenant.</summary>
    public static readonly ErrorCode ExpenseNotFound = new("EXPENSE_NOT_FOUND", ErrorCode.NotFound);

    /// <summary>The expense is already reversed, is itself a reversal, or is not posted.</summary>
    public static readonly ErrorCode ExpenseNotReversible = new("EXPENSE_NOT_REVERSIBLE", ErrorCode.Conflict);

    /// <summary>The addressed fixed (recurring) expense does not exist for this tenant.</summary>
    public static readonly ErrorCode RecurringExpenseNotFound = new("RECURRING_EXPENSE_NOT_FOUND", ErrorCode.NotFound);

    /// <summary>The fixed expense is inactive and cannot be paid.</summary>
    public static readonly ErrorCode RecurringExpenseInactive = new("RECURRING_EXPENSE_INACTIVE", ErrorCode.Conflict);

    /// <summary>The fixed expense was already paid for the current period.</summary>
    public static readonly ErrorCode RecurringExpenseAlreadyPaid = new("RECURRING_EXPENSE_ALREADY_PAID", ErrorCode.Conflict);

    #endregion

    #region Local store / keystore (A11)

    /// <summary>The local store key could not be read from the OS keystore; the store must not open (fail closed).</summary>
    public static readonly ErrorCode StoreKeyUnavailable =
        new("STORE_KEY_UNAVAILABLE", ErrorCode.Infrastructure);

    /// <summary>The native SQLite library is older than the minimum supported version.</summary>
    public static readonly ErrorCode NativeSqliteTooOld =
        new("NATIVE_SQLITE_TOO_OLD", ErrorCode.Infrastructure);

    /// <summary>The local store failed its integrity check.</summary>
    public static readonly ErrorCode StoreCorrupt =
        new("STORE_CORRUPT", ErrorCode.Infrastructure);

    /// <summary>A local store backup could not be created or verified.</summary>
    public static readonly ErrorCode StoreBackupFailed =
        new("STORE_BACKUP_FAILED", ErrorCode.Infrastructure);

    /// <summary>A local store restore could not be completed; the previous store is kept.</summary>
    public static readonly ErrorCode StoreRestoreFailed =
        new("STORE_RESTORE_FAILED", ErrorCode.Infrastructure);

    /// <summary>Store encryption was requested but the encryption provider (K2) is not approved.</summary>
    public static readonly ErrorCode StoreEncryptionNotApproved =
        new("STORE_ENCRYPTION_NOT_APPROVED", ErrorCode.Infrastructure);

    #endregion

    #region Application dispatch (B01)

    /// <summary>No handler is registered for the dispatched command/query type. <c>Detail</c> names the type.</summary>
    public static readonly ErrorCode HandlerNotRegistered =
        new("HANDLER_NOT_REGISTERED", ErrorCode.Infrastructure);

    /// <summary>
    /// The dispatch pipeline could not run the request (kind/result-type mismatch or a missing pipeline
    /// dependency such as the unit of work). A wiring defect, never a business outcome.
    /// </summary>
    public static readonly ErrorCode DispatchFailed =
        new("DISPATCH_FAILED", ErrorCode.Infrastructure);

    #endregion

    #region Command pipeline (B02)

    /// <summary>
    /// A settlement-state change is not in <c>SettlementTransitions</c>. <c>Detail</c> names the pair.
    /// </summary>
    public static readonly ErrorCode SettlementInvalidTransition =
        new("SETTLEMENT_INVALID_TRANSITION", ErrorCode.Integrity);

    /// <summary>A settlement was applied to a command that is already in a terminal settled state.</summary>
    public static readonly ErrorCode CommandAlreadySettled =
        new("COMMAND_ALREADY_SETTLED", ErrorCode.Conflict);

    /// <summary>
    /// A processor tried to settle a claim whose lease has expired and been taken by another processor —
    /// the first processor must abandon its (now stale) work (A-RULE 026).
    /// </summary>
    public static readonly ErrorCode CommandClaimLost =
        new("COMMAND_CLAIM_LOST", ErrorCode.Conflict);

    /// <summary>An outbox message exhausted its retries and was dead-lettered with its last error preserved.</summary>
    public static readonly ErrorCode OutboxDeadLettered =
        new("OUTBOX_DEAD_LETTERED", ErrorCode.Conflict);

    #endregion

    #region History / Dashboard / Search / Profile (C10)

    /// <summary>A history CSV export exceeded the 50 000-row streaming cap (PERFORMANCE.md §6).</summary>
    public static readonly ErrorCode ExportTooLarge =
        new("EXPORT_TOO_LARGE", ErrorCode.Validation);

    /// <summary>The signed-in user has no profile record to read or edit.</summary>
    public static readonly ErrorCode ProfileNotFound =
        new("PROFILE_NOT_FOUND", ErrorCode.NotFound);

    /// <summary>A settings save referenced a preference key the catalog does not define.</summary>
    public static readonly ErrorCode SettingKeyUnknown =
        new("SETTING_KEY_UNKNOWN", ErrorCode.Validation);

    #endregion

    #region Sync feed (S12)

    /// <summary>The feed cursor has been fenced or lies beyond retained history.</summary>
    public static readonly ErrorCode ResyncRequired = new("RESYNC_REQUIRED", ErrorCode.Conflict);

    /// <summary>The requested tenant-owned immutable batch or page does not exist.</summary>
    public static readonly ErrorCode BatchNotFound = new("BATCH_NOT_FOUND", ErrorCode.NotFound);

    #endregion

    #region Server command intake (Phase S06)

    /// <summary>The same operation is currently being processed under an unexpired lease; retry later.</summary>
    public static readonly ErrorCode CommandInProgress =
        new("COMMAND_IN_PROGRESS", ErrorCode.Conflict);

    /// <summary>The command this one depends on was settled as rejected, so this one can never apply.</summary>
    public static readonly ErrorCode DependencyRejected =
        new("DEPENDENCY_REJECTED", ErrorCode.Conflict);

    /// <summary>A command batch exceeded the 50-command cap.</summary>
    public static readonly ErrorCode BatchTooLarge =
        new("BATCH_TOO_LARGE", ErrorCode.Validation);

    /// <summary>The envelope's wire-contract version is not supported by this server.</summary>
    public static readonly ErrorCode ContractVersionUnsupported =
        new("CONTRACT_VERSION_UNSUPPORTED", ErrorCode.Validation);

    /// <summary>The envelope's rule-set version is older than the oldest version still supported.</summary>
    public static readonly ErrorCode RuleSetUnsupported =
        new("RULESET_UNSUPPORTED", ErrorCode.Validation);

    #endregion

    #region Subscription & Operator (Phase S14)

    public static readonly ErrorCode SubscriptionNotFound = new("SUBSCRIPTION_NOT_FOUND", ErrorCode.NotFound);
    public static readonly ErrorCode SubscriptionInvalidTransition = new("SUBSCRIPTION_INVALID_TRANSITION", ErrorCode.Conflict);
    public static readonly ErrorCode SeatLimitExceeded = new("SEAT_LIMIT_EXCEEDED", ErrorCode.Conflict);
    public static readonly ErrorCode PlanNotFound = new("PLAN_NOT_FOUND", ErrorCode.NotFound);
    public static readonly ErrorCode TenantCodeTaken = new("TENANT_CODE_TAKEN", ErrorCode.Conflict);
    public static readonly ErrorCode ProvisioningKeyConflict = new("PROVISIONING_KEY_CONFLICT", ErrorCode.Integrity);
    public static readonly ErrorCode OperatorCredentialsInvalid = new("OPERATOR_CREDENTIALS_INVALID", ErrorCode.Forbidden);
    public static readonly ErrorCode OperatorLockedOut = new("OPERATOR_LOCKED_OUT", ErrorCode.Forbidden);

    #endregion

    #region Provisioning (Phase S15)

    public static readonly ErrorCode ProvisioningSignatureInvalid = new("PROVISIONING_SIGNATURE_INVALID", ErrorCode.Forbidden);
    public static readonly ErrorCode ProvisioningIntentNotFound = new("PROVISIONING_INTENT_NOT_FOUND", ErrorCode.NotFound);

    #endregion

    #region Licensing (Phase S16)

    public static readonly ErrorCode LicenseInvalid = new("LICENSE_INVALID", ErrorCode.Entitlement);
    public static readonly ErrorCode LicenseKeyUnknown = new("LICENSE_KEY_UNKNOWN", ErrorCode.Entitlement);
    public static readonly ErrorCode LicenseExpired = new("LICENSE_EXPIRED", ErrorCode.Entitlement);
    public static readonly ErrorCode LicenseRevoked = new("LICENSE_REVOKED", ErrorCode.Entitlement);
    public static readonly ErrorCode LicenseDeviceMismatch = new("LICENSE_DEVICE_MISMATCH", ErrorCode.Forbidden);
    public static readonly ErrorCode LicenseDeviceLimitReached = new("LICENSE_DEVICE_LIMIT_REACHED", ErrorCode.Conflict);
    public static readonly ErrorCode LicenseClockRollback = new("LICENSE_CLOCK_ROLLBACK", ErrorCode.Entitlement);
    public static readonly ErrorCode LicenseNotGranted = new("LICENSE_NOT_GRANTED", ErrorCode.Entitlement);

    #endregion

    #region Files (Phase S17)

    public static readonly ErrorCode FileNotFound = new("FILE_NOT_FOUND", ErrorCode.NotFound);
    public static readonly ErrorCode FileHashMismatch = new("FILE_HASH_MISMATCH", ErrorCode.Integrity);
    public static readonly ErrorCode FileTypeNotAllowed = new("FILE_TYPE_NOT_ALLOWED", ErrorCode.Validation);
    public static readonly ErrorCode FileTooLarge = new("FILE_TOO_LARGE", ErrorCode.Validation);
    public static readonly ErrorCode ValetKeyInvalid = new("VALET_KEY_INVALID", ErrorCode.Forbidden);

    #endregion
}
