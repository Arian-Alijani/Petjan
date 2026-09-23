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

    /// <summary>An unclassified defect. Always logged with its exception.</summary>
    public static readonly ErrorCode Unexpected =
        new("UNEXPECTED", ErrorCode.Infrastructure);

    #endregion

    #region Identity (Phase A03)

    /// <summary>The tenant is suspended or closed; writes are refused.</summary>
    public static readonly ErrorCode TenantSuspended =
        new("TENANT_SUSPENDED", ErrorCode.Forbidden);

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

    #endregion
}
