# Track S (Server) — Test & Build Issues Report

> Record only. Nothing was fixed (per directive). Desktop app (`Animora.Desktop`) and its dependent test projects were NOT built/tested.
>
> - Run date: 2026-10-10 · Env: Debian, .NET SDK 10.0.401, PostgreSQL 17.11 (local, `127.0.0.1:5433`, `--require-pg` so PG-gated tests really ran)
> - Command: `scripts/sandbox/server-test.sh --all --require-pg` ; build: `dotnet build src/Animora.Server --no-incremental`
> - Full logs: `.sandbox/logs/server-test.log` (gitignored, local)

## 1. Summary

| Target | Result |
|---|---|
| Build `src/Animora.Server` (+ Web.Admin, Server.Licensing, Persistence.PostgreSql, ...) | **0 errors, 26 warnings** (clean build NOT achieved: warnings present) |
| `Animora.Server.Tests` | **FAILED** — Passed 652 / Failed 107 (104 distinct test names listed below; rest are theory cases) / Skipped 0 / Total 759 |
| `Animora.Persistence.Tests` (SQLite client persistence, pulled in by `--all`; not strictly Track S) | FAILED — Passed 114 / Failed 6 / Total 120 |
| `Animora.Sync.Tests` | Passed 1 / 1 |

Verdict: Track S compiles, but is **not** error/warning free and **107 server tests fail** (+6 in Persistence.Tests). Warnings below.

## 2. Build warnings

### 2.1 Product build (`src/Animora.Server`) — 26 × CS9107
`Parameter 'AnimoraServerDbContext db' is captured into the state of the enclosing type and its value is also passed to the base constructor.`
Files (all under `src/Animora.Persistence.PostgreSql/Repositories/`): Appointment, Boarding, BoardingService, CashSession, Cheque, Expense, Insurer, Invoice, Ledger, MedicalHistory (line 7), Owner, Payment, Period, Pet, ProductCategory, Product, Purchasing, ReceptionQueue, RecurringExpense, Room, StockMovementWriter (line 11), Supplier, TreatmentPlan, Vaccination, Visit, WeightEntry — each `*Repository.cs(8,N)`.

### 2.2 Test project build (`tests/Animora.Server.Tests`) — 12
- CS0618 `Outbox/OutboxClaimSqlTests.cs(28,64)`: `IReadOnlyEntityType.GetQueryFilter()` obsolete — use `GetDeclaredQueryFilters()`.
- xUnit2013 `Admin/AdminSourceTests.cs(48)`: use Assert.Single for collection size.
- xUnit2031 (Where before Assert.Single): `Admin/FileMismatchServiceTests.cs(51,60,60)`, `Commands/CommandIntakeStoreTests.cs(30)`, `Provisioning/ProvisioningIntentStoreTests.cs(54,57)`, `Sync/BatchFreezerTests.cs(15)`, `Sync/ContractsTests.cs(19)`.
- xUnit2000 `Sync/PureFeedTests.cs(15,17)`: constant `RetentionPolicy.JournalRetentionDays` should be the `expected` argument.

## 3. Test failures — grouped by symptom

### A. ArgumentNullException (Parameter 'Host') — null Host in test-kit/auth host  (34)

- `Animora.Server.Tests.Authorization.AuthorizationTests.ExtraGrant_AllowsBeyondRole_OverHttp`  
  System.ArgumentNullException : Value cannot be null. (Parameter 'Host')
- `Animora.Server.Tests.Authorization.AuthorizationTests.ManagerOnly_And_OperatorOnly_PoliciesUseStoredRole`  
  System.ArgumentNullException : Value cannot be null. (Parameter 'Host')
- `Animora.Server.Tests.Authorization.AuthorizationTests.RoleClaimInToken_IsNeverTrusted`  
  System.ArgumentNullException : Value cannot be null. (Parameter 'Host')
- `Animora.Server.Tests.Authorization.CapabilitySnapshotTests.Endpoint_IssuesSignedSnapshot_VerifiableWithPublishedKey_NoCriticalCodes`  
  System.ArgumentNullException : Value cannot be null. (Parameter 'Host')
- `Animora.Server.Tests.Authorization.CapabilitySnapshotTests.PendingDevice_CannotRequestCapabilities`  
  System.ArgumentNullException : Value cannot be null. (Parameter 'Host')
- `Animora.Server.Tests.Authorization.DeviceEndpointsTests.Approve_ActivatesPendingDevice_AndIsAudited`  
  System.ArgumentNullException : Value cannot be null. (Parameter 'Host')
- `Animora.Server.Tests.Authorization.DeviceEndpointsTests.Approve_RevokedDevice_IsRejected`  
  System.ArgumentNullException : Value cannot be null. (Parameter 'Host')
- `Animora.Server.Tests.Authorization.DeviceEndpointsTests.NonManager_CannotApproveOrRevoke`  
  System.ArgumentNullException : Value cannot be null. (Parameter 'Host')
- `Animora.Server.Tests.Authorization.DeviceEndpointsTests.Revoke_ReissuedTokenOnRevokedDevice_IsDeviceRevoked`  
  System.ArgumentNullException : Value cannot be null. (Parameter 'Host')
- `Animora.Server.Tests.Authorization.DeviceEndpointsTests.Revoke_RotatesSecurityStamp_InvalidatesToken`  
  System.ArgumentNullException : Value cannot be null. (Parameter 'Host')
- `Animora.Server.Tests.Authorization.DeviceEndpointsTests.Revoke_WithoutReason_Returns400_AndChangesNothing(reason:`  
  System.ArgumentNullException : Value cannot be null. (Parameter 'Host')
- `Animora.Server.Tests.Authorization.DeviceEndpointsTests.UnknownDevice_Returns404`  
  System.ArgumentNullException : Value cannot be null. (Parameter 'Host')
- `Animora.Server.Tests.Authorization.KeysEndpointTests.ExposesPublicOnly_NeverPrivate`  
  System.ArgumentNullException : Value cannot be null. (Parameter 'Host')
- `Animora.Server.Tests.Authorization.KeysEndpointTests.IsAnonymous_AndNeverTenantBound`  
  System.ArgumentNullException : Value cannot be null. (Parameter 'Host')
- `Animora.Server.Tests.Authorization.PlaneOrderTests.AllowAllGate_NeverBlocks_AndAnonymousRoutesBypassEntitlement`  
  System.ArgumentNullException : Value cannot be null. (Parameter 'Host')
- `Animora.Server.Tests.Authorization.PlaneOrderTests.TenantBindingFailure_PrecedesPermissionAndEntitlement`  
  System.ArgumentNullException : Value cannot be null. (Parameter 'Host')
- `Animora.Server.Tests.Authorization.PlaneOrderTests.Unauthenticated401_Then_Permission403_Then_Entitlement`  
  System.ArgumentNullException : Value cannot be null. (Parameter 'Host')
- `Animora.Server.Tests.Authorization.TenantContextMiddlewareTests.DisabledUser_Returns403`  
  System.ArgumentNullException : Value cannot be null. (Parameter 'Host')
- `Animora.Server.Tests.Authorization.TenantContextMiddlewareTests.ForeignTenantHint_IsAuditedAndIgnored`  
  System.ArgumentNullException : Value cannot be null. (Parameter 'Host')
- `Animora.Server.Tests.Authorization.TenantContextMiddlewareTests.IgnoresClaimedTenant_UsesMembership`  
  System.ArgumentNullException : Value cannot be null. (Parameter 'Host')
- `Animora.Server.Tests.Authorization.TenantContextMiddlewareTests.InactiveMembership_Returns403`  
  System.ArgumentNullException : Value cannot be null. (Parameter 'Host')
- `Animora.Server.Tests.Authorization.TenantContextMiddlewareTests.MatchingTenantHint_IsNotAudited`  
  System.ArgumentNullException : Value cannot be null. (Parameter 'Host')
- `Animora.Server.Tests.Authorization.TenantContextMiddlewareTests.RevokedDevice_Returns403`  
  System.ArgumentNullException : Value cannot be null. (Parameter 'Host')
- `Animora.Server.Tests.Authorization.TenantContextMiddlewareTests.StaleSecurityStamp_Returns401`  
  System.ArgumentNullException : Value cannot be null. (Parameter 'Host')
- `Animora.Server.Tests.Authorization.TenantContextMiddlewareTests.TenantSuspended_Returns403`  
  System.ArgumentNullException : Value cannot be null. (Parameter 'Host')
- `Animora.Server.Tests.HostSmokeTests.Live_Returns200_WithoutDependencies`  
  System.ArgumentNullException : Value cannot be null. (Parameter 'Host')
- `Animora.Server.Tests.HostSmokeTests.Ready_Returns200_WhenDataRootWritable`  
  System.ArgumentNullException : Value cannot be null. (Parameter 'Host')
- `Animora.Server.Tests.HostSmokeTests.Ready_Returns503_NamesFailedProbeOnly_WhenDataRootMissing`  
  System.ArgumentNullException : Value cannot be null. (Parameter 'Host')
- `Animora.Server.Tests.HostSmokeTests.Ready_Returns503_NamingDatabaseProbes_WhenPostgreSqlIsAbsent`  
  System.ArgumentNullException : Value cannot be null. (Parameter 'Host')
- `Animora.Server.Tests.Identity.AuthEndpointsTests.AuthenticatedEndpoints_WithoutBearer_Return401_SessionExpired(path:`  
  System.ArgumentNullException : Value cannot be null. (Parameter 'Host')
- `Animora.Server.Tests.Identity.AuthEndpointsTests.Failure_Returns_WireApiError_WithCorrelationId`  
  System.ArgumentNullException : Value cannot be null. (Parameter 'Host')
- `Animora.Server.Tests.Identity.AuthEndpointsTests.Login_RateLimited_Returns429`  
  System.ArgumentNullException : Value cannot be null. (Parameter 'Host')
- `Animora.Server.Tests.Identity.AuthEndpointsTests.Login_Success_Returns200_NoStore_AndTokens`  
  System.ArgumentNullException : Value cannot be null. (Parameter 'Host')
- `Animora.Server.Tests.Identity.AuthEndpointsTests.Refresh_WithGarbageToken_Returns401`  
  System.ArgumentNullException : Value cannot be null. (Parameter 'Host')

### B. NullReferenceException in S09/S10 handler-intake tests  (28)

- `Animora.Server.Tests.HandlersS09.SalesCashierIntakeTests.Settles_AddCustomInvoiceLineCommand`  
  System.NullReferenceException : Object reference not set to an instance of an object.
- `Animora.Server.Tests.HandlersS09.SalesCashierIntakeTests.Settles_AddInvoiceLineCommand`  
  System.NullReferenceException : Object reference not set to an instance of an object.
- `Animora.Server.Tests.HandlersS09.SalesCashierIntakeTests.Settles_AssignInvoiceOwnerCommand`  
  System.NullReferenceException : Object reference not set to an instance of an object.
- `Animora.Server.Tests.HandlersS09.SalesCashierIntakeTests.Settles_DeleteDraftInvoiceCommand`  
  System.NullReferenceException : Object reference not set to an instance of an object.
- `Animora.Server.Tests.HandlersS09.SalesCashierIntakeTests.Settles_DeleteInsurerCommand`  
  System.NullReferenceException : Object reference not set to an instance of an object.
- `Animora.Server.Tests.HandlersS09.SalesCashierIntakeTests.Settles_EditInvoiceLineCommand`  
  System.NullReferenceException : Object reference not set to an instance of an object.
- `Animora.Server.Tests.HandlersS09.SalesCashierIntakeTests.Settles_PostInvoiceCommand`  
  System.NullReferenceException : Object reference not set to an instance of an object.
- `Animora.Server.Tests.HandlersS09.SalesCashierIntakeTests.Settles_RefundInvoiceCommand`  
  System.NullReferenceException : Object reference not set to an instance of an object.
- `Animora.Server.Tests.HandlersS09.SalesCashierIntakeTests.Settles_RegisterPaymentCommand`  
  System.NullReferenceException : Object reference not set to an instance of an object.
- `Animora.Server.Tests.HandlersS09.SalesCashierIntakeTests.Settles_RemoveInvoiceLineCommand`  
  System.NullReferenceException : Object reference not set to an instance of an object.
- `Animora.Server.Tests.HandlersS09.SalesCashierIntakeTests.Settles_ReturnInvoiceLineCommand`  
  System.NullReferenceException : Object reference not set to an instance of an object.
- `Animora.Server.Tests.HandlersS09.SalesCashierIntakeTests.Settles_SetInvoiceDiscountCommand`  
  System.NullReferenceException : Object reference not set to an instance of an object.
- `Animora.Server.Tests.HandlersS09.SalesCashierIntakeTests.Settles_SetInvoiceInsuranceCommand`  
  System.NullReferenceException : Object reference not set to an instance of an object.
- `Animora.Server.Tests.HandlersS09.SalesCashierIntakeTests.Settles_UpdateInsurerCommand`  
  System.NullReferenceException : Object reference not set to an instance of an object.
- `Animora.Server.Tests.HandlersS09.SalesCashierIntakeTests.Settles_UpdateServiceRateCommand`  
  System.NullReferenceException : Object reference not set to an instance of an object.
- `Animora.Server.Tests.HandlersS09.SalesCashierIntakeTests.Settles_VoidInvoiceCommand`  
  System.NullReferenceException : Object reference not set to an instance of an object.
- `Animora.Server.Tests.HandlersS10.MoneyStockIntakeTests.Settles_AddProductVariantCommand`  
  System.NullReferenceException : Object reference not set to an instance of an object.
- `Animora.Server.Tests.HandlersS10.MoneyStockIntakeTests.Settles_AdjustStockCommand`  
  System.NullReferenceException : Object reference not set to an instance of an object.
- `Animora.Server.Tests.HandlersS10.MoneyStockIntakeTests.Settles_CancelPurchaseOrderCommand`  
  System.NullReferenceException : Object reference not set to an instance of an object.
- `Animora.Server.Tests.HandlersS10.MoneyStockIntakeTests.Settles_DeleteProductCategoryCommand`  
  System.NullReferenceException : Object reference not set to an instance of an object.
- `Animora.Server.Tests.HandlersS10.MoneyStockIntakeTests.Settles_DeleteProductCommand`  
  System.NullReferenceException : Object reference not set to an instance of an object.
- `Animora.Server.Tests.HandlersS10.MoneyStockIntakeTests.Settles_EditRecurringExpenseCommand`  
  System.NullReferenceException : Object reference not set to an instance of an object.
- `Animora.Server.Tests.HandlersS10.MoneyStockIntakeTests.Settles_PayRecurringExpenseForPeriodCommand`  
  System.NullReferenceException : Object reference not set to an instance of an object.
- `Animora.Server.Tests.HandlersS10.MoneyStockIntakeTests.Settles_RenameProductCategoryCommand`  
  System.NullReferenceException : Object reference not set to an instance of an object.
- `Animora.Server.Tests.HandlersS10.MoneyStockIntakeTests.Settles_ReverseExpenseCommand`  
  System.NullReferenceException : Object reference not set to an instance of an object.
- `Animora.Server.Tests.HandlersS10.MoneyStockIntakeTests.Settles_SetStockCommand`  
  System.NullReferenceException : Object reference not set to an instance of an object.
- `Animora.Server.Tests.HandlersS10.MoneyStockIntakeTests.Settles_ToggleRecurringExpenseCommand`  
  System.NullReferenceException : Object reference not set to an instance of an object.
- `Animora.Server.Tests.HandlersS10.MoneyStockIntakeTests.Settles_UpdateProductInfoCommand`  
  System.NullReferenceException : Object reference not set to an instance of an object.

### C. EF ManyServiceProvidersCreatedWarning promoted to error (>20 IServiceProvider)  (14)

- `Animora.Server.Tests.Admin.AdminEndToEndTests.CreateTenant_DoubleSubmit_SameIdempotencyKey_OneTenant`  
  System.InvalidOperationException : An error was generated for warning 'Microsoft.EntityFrameworkCore.Infrastructure.ManyServiceProvidersCreatedWarning': More than twenty 'IServiceProvider' instances h
- `Animora.Server.Tests.Admin.AdminEndToEndTests.RevokeLicence_DeviceBlockedAtServerGate`  
  System.InvalidOperationException : An error was generated for warning 'Microsoft.EntityFrameworkCore.Infrastructure.ManyServiceProvidersCreatedWarning': More than twenty 'IServiceProvider' instances h
- `Animora.Server.Tests.Persistence.TenantIsolationTests.A_CannotRead_B_ById`  
  System.InvalidOperationException : An error was generated for warning 'Microsoft.EntityFrameworkCore.Infrastructure.ManyServiceProvidersCreatedWarning': More than twenty 'IServiceProvider' instances h
- `Animora.Server.Tests.Persistence.TenantIsolationTests.A_CannotRead_B_List`  
  System.InvalidOperationException : An error was generated for warning 'Microsoft.EntityFrameworkCore.Infrastructure.ManyServiceProvidersCreatedWarning': More than twenty 'IServiceProvider' instances h
- `Animora.Server.Tests.Persistence.TenantIsolationTests.NullContext_Throws_TenantContextMissing`  
  System.InvalidOperationException : An error was generated for warning 'Microsoft.EntityFrameworkCore.Infrastructure.ManyServiceProvidersCreatedWarning': More than twenty 'IServiceProvider' instances h
- `Animora.Server.Tests.Persistence.TenantIsolationTests.PooledConnection_A_B_A_NoLeak`  
  System.InvalidOperationException : An error was generated for warning 'Microsoft.EntityFrameworkCore.Infrastructure.ManyServiceProvidersCreatedWarning': More than twenty 'IServiceProvider' instances h
- `Animora.Server.Tests.Persistence.TenantIsolationTests.SoftDeleteLift_StillHidesOtherTenant`  
  System.InvalidOperationException : An error was generated for warning 'Microsoft.EntityFrameworkCore.Infrastructure.ManyServiceProvidersCreatedWarning': More than twenty 'IServiceProvider' instances h
- `Animora.Server.Tests.Persistence.TenantIsolationTests.Write_WithForeignTenant_Throws_TenantMismatch`  
  System.InvalidOperationException : An error was generated for warning 'Microsoft.EntityFrameworkCore.Infrastructure.ManyServiceProvidersCreatedWarning': More than twenty 'IServiceProvider' instances h
- `Animora.Server.Tests.Subscriptions.SubscriptionPersistenceTests.ConcurrentProvisionSameKey_OneTenant`  
  System.InvalidOperationException : An error was generated for warning 'Microsoft.EntityFrameworkCore.Infrastructure.ManyServiceProvidersCreatedWarning': More than twenty 'IServiceProvider' instances h
- `Animora.Server.Tests.Subscriptions.SubscriptionPersistenceTests.EventsAndOperatorAudit_ImmutableTriggers`  
  System.InvalidOperationException : An error was generated for warning 'Microsoft.EntityFrameworkCore.Infrastructure.ManyServiceProvidersCreatedWarning': More than twenty 'IServiceProvider' instances h
- `Animora.Server.Tests.Subscriptions.SubscriptionPersistenceTests.SameKeyDifferentFingerprint_Conflicts`  
  System.InvalidOperationException : An error was generated for warning 'Microsoft.EntityFrameworkCore.Infrastructure.ManyServiceProvidersCreatedWarning': More than twenty 'IServiceProvider' instances h
- `Animora.Server.Tests.Subscriptions.SubscriptionPersistenceTests.SameKeySameFingerprint_ReplaysWithoutSecondTenant`  
  System.InvalidOperationException : An error was generated for warning 'Microsoft.EntityFrameworkCore.Infrastructure.ManyServiceProvidersCreatedWarning': More than twenty 'IServiceProvider' instances h
- `Animora.Server.Tests.Subscriptions.SubscriptionPersistenceTests.SuspendedSubscription_UpdatesTenantStatus_AndIfMatchConflictDoesNotAppend`  
  System.InvalidOperationException : An error was generated for warning 'Microsoft.EntityFrameworkCore.Infrastructure.ManyServiceProvidersCreatedWarning': More than twenty 'IServiceProvider' instances h
- `Animora.Server.Tests.Subscriptions.SubscriptionPersistenceTests.SweepAndConsumer_Idempotent_OnRedelivery`  
  System.InvalidOperationException : An error was generated for warning 'Microsoft.EntityFrameworkCore.Infrastructure.ManyServiceProvidersCreatedWarning': More than twenty 'IServiceProvider' instances h

### D. PostgresException: Missing tenant feed state (journal trigger)  (6)

- `Animora.Server.Tests.HandlersS07.S07EndToEndTests.CommandsRoundTrip_ThroughIntake_WithTenantIsolation`  
  Microsoft.EntityFrameworkCore.DbUpdateException : An error occurred while saving the entity changes. See the inner exception for details. ---- Npgsql.PostgresException : P0001: Missing tenant feed sta
- `Animora.Server.Tests.HandlersS08.S08ConcurrencyTests.TwoDevices_LastRoomPlace_OneWins`  
  Microsoft.EntityFrameworkCore.DbUpdateException : An error occurred while saving the entity changes. See the inner exception for details. ---- Npgsql.PostgresException : P0001: Missing tenant feed sta
- `Animora.Server.Tests.HandlersS08.S08ConcurrencyTests.TwoDevices_SameSlot_OneWins`  
  Microsoft.EntityFrameworkCore.DbUpdateException : An error occurred while saving the entity changes. See the inner exception for details. ---- Npgsql.PostgresException : P0001: Missing tenant feed sta
- `Animora.Server.Tests.HandlersS08.S08EndToEndTests.CommandsRoundTrip_ThroughIntake_WithTenantIsolation`  
  Microsoft.EntityFrameworkCore.DbUpdateException : An error occurred while saving the entity changes. See the inner exception for details. ---- Npgsql.PostgresException : P0001: Missing tenant feed sta
- `Animora.Server.Tests.HandlersS09.S09EndToEndTests.CommandsRoundTrip_ThroughIntake_WithTenantIsolation`  
  Microsoft.EntityFrameworkCore.DbUpdateException : An error occurred while saving the entity changes. See the inner exception for details. ---- Npgsql.PostgresException : P0001: Missing tenant feed sta
- `Animora.Server.Tests.HandlersS10.S10EndToEndTests.CommandsRoundTrip_ThroughIntake_WithTenantIsolation`  
  Microsoft.EntityFrameworkCore.DbUpdateException : An error occurred while saving the entity changes. See the inner exception for details. ---- Npgsql.PostgresException : P0001: Missing tenant feed sta

### E. CryptographicException: DataProtection payload invalid (Licensing)  (4)

- `Animora.Server.Tests.Licensing.LicensePersistenceTests.ConcurrentRenew_NoDuplicateLive`  
  System.Security.Cryptography.CryptographicException : The payload was invalid. For more information go to https://aka.ms/aspnet/dataprotectionwarning
- `Animora.Server.Tests.Licensing.LicensePersistenceTests.CrossTenant_CannotReadOrRevoke`  
  System.Security.Cryptography.CryptographicException : The payload was invalid. For more information go to https://aka.ms/aspnet/dataprotectionwarning
- `Animora.Server.Tests.Licensing.LicensePersistenceTests.DeviceLimit_Mismatch_AndRevokedGrant_AreRejected`  
  System.Security.Cryptography.CryptographicException : The payload was invalid. For more information go to https://aka.ms/aspnet/dataprotectionwarning
- `Animora.Server.Tests.Licensing.LicensePersistenceTests.RevokedLicence_BlocksCommandsAt_ServerGate`  
  System.Security.Cryptography.CryptographicException : The payload was invalid. For more information go to https://aka.ms/aspnet/dataprotectionwarning

### F. Debug.Fail: InvoiceNumberAllocator outside ambient UnitOfWork (SQLite client persistence tests)  (2)

- `Animora.Persistence.Tests.InvoiceNumberAllocatorTests.RolledBackAllocation_IsNotCommitted`  
  Microsoft.VisualStudio.TestPlatform.TestHost.DebugAssertException : Method Debug.Fail failed with 'InvoiceNumberAllocator must run inside an ambient UnitOfWork transaction to avoid invoice-number gaps
- `Animora.Persistence.Tests.InvoiceNumberAllocatorTests.TwoHundredConcurrentAllocations_AreDistinct`  
  Microsoft.VisualStudio.TestPlatform.TestHost.DebugAssertException : Method Debug.Fail failed with 'InvoiceNumberAllocator must run inside an ambient UnitOfWork transaction to avoid invoice-number gaps

### G. Assertion / expectation mismatches (misc)  (22)

- `Animora.Persistence.Tests.FinanceProjectionIntegrityTests.SeededProjections_MatchIndependentRebuild_AndRepairCorruption`  
  Assert.NotEmpty() Failure: Collection was empty
- `Animora.Persistence.Tests.SeedingTests.SampleData_SeedsTheC01OwnersPetsWeightsAndDocuments`  
  Assert.Equal() Failure: Values differ Expected: 12 Actual: 0
- `Animora.Persistence.Tests.SeedingTests.Seeding_IsIdempotentAndComplete`  
  Assert.Equal() Failure: Values differ Expected: 18 Actual: 12
- `Animora.Persistence.Tests.UnitOfWorkTests.StoreFailure_IsMappedToAResult`  
  Assert.Equal() Failure: Values differ Expected: Infrastructure/STORE_UNAVAILABLE Actual: Conflict/CONCURRENCY_CONFLICT
- `Animora.Server.Tests.Admin.AdminCookiePgTests.CookieRevokedByOperatorPasswordChange`  
  Assert.Equal() Failure: Strings differ Expected: "/admin/login" Actual: null
- `Animora.Server.Tests.Admin.AdminCookiePgTests.Cookie_CannotCallOperatorApi_AndPostsWithoutTokenRejected`  
  Assert.Equal() Failure: Values differ Expected: BadRequest Actual: InternalServerError
- `Animora.Server.Tests.Admin.AdminEndToEndTests.OperatorCreatesTenant_SuspendsIt_TenantRequestsGet403_ReinstateRestores`  
  Assert.Equal() Failure: Values differ Expected: OK Actual: InternalServerError
- `Animora.Server.Tests.Admin.AdminWebsiteClientPagesTests.CreateAndRevoke_RequireConfirmation_AndAntiforgery`  
  Assert.Equal() Failure: Values differ Expected: BadRequest Actual: InternalServerError
- `Animora.Server.Tests.Authorization.AuthorizationPgTests.SigningKeyMigration_Applies`  
  Assert.Equal() Failure: Strings differ ↓ (pos 1) Expected: "104_CommandIntake" Actual: "115_Files" ↑ (pos 1)
- `Animora.Server.Tests.Authorization.AuthorizationTests.CommandAuthorizer_UnmappedCommand_IsDenied_FailClosed`  
  Assert.True() Failure Expected: True Actual: False
- `Animora.Server.Tests.Authorization.AuthorizationTests.CommandPermissionTable_StartsEmpty_AndUnmappedLookupIsNull`  
  Assert.Empty() Failure: Collection was not empty Collection: [["sales.invoice.insurance.set"] = "sales.draft", ["inventory.category.rename"] = "inventory.adjust", ["vaccination.record.reminder"] = "va
- `Animora.Server.Tests.HandlersS07.ClinicalIntakeTests.Settles_CreateWalkInCommand`  
  Assert.Equal() Failure: Strings differ ↓ (pos 0) Expected: "WALKIN_PHONE_REQUIRED" Actual: "VALIDATION_FAILED" ↑ (pos 0)
- `Animora.Server.Tests.HandlersS07.ServerS07RederiveTests.Occurrence_IsIdempotent_AndCatalogCodeIsUnique`  
  Assert.Equal() Failure: Values differ Expected: Conflict/TREATMENT_OCCURRENCE_ALREADY_DONE Actual: Conflict/TREATMENT_PLAN_NOT_ACTIVE
- `Animora.Server.Tests.HandlersS10.HandlerCoverageTests.EveryS10CommandType_HasHandler_CatalogEntry_AndPermissionRow`  
  Assert.True() Failure Expected: True Actual: False
- `Animora.Server.Tests.HandlersS10.MoneyStockIntakeTests.Settles_DeleteChequeDraftCommand`  
  Assert.Equal() Failure: Values differ Expected: Accepted Actual: Rejected
- `Animora.Server.Tests.HandlersS10.MoneyStockIntakeTests.Settles_RegisterChequeCommand`  
  Assert.Equal() Failure: Values differ Expected: Accepted Actual: Rejected
- `Animora.Server.Tests.HandlersS10.MoneyStockIntakeTests.Settles_TransitionChequeCommand`  
  Assert.Equal() Failure: Values differ Expected: Accepted Actual: Rejected
- `Animora.Server.Tests.HandlersS10.MoneyStockIntakeTests.Settles_UpdateChequeMetadataCommand`  
  Assert.Equal() Failure: Values differ Expected: Accepted Actual: Rejected
- `Animora.Server.Tests.HandlersS10.MoneyStockModelTests.MoneyStockTable_HasTenantIndexesFiltersAndUuid(clrType:`  
  Assert.Equal() Failure: Strings differ ↓ (pos 11) Expected: "JournalEntries" Actual: "JournalEntrys" ↑ (pos 11)
- `Animora.Server.Tests.Identity.IdentityMigrationTests.Migration_Applies_To_Head_And_Installs_Triggers_AndTenantCode`  
  Assert.Equal() Failure: Strings differ ↓ (pos 1) Expected: "104_CommandIntake" Actual: "115_Files" ↑ (pos 1)
- `Animora.Server.Tests.Outbox.OutboxClaimTests.Redelivery_ConsumerEffectRunsOnce`  
  Assert.Single() Failure: The collection was empty
- `Animora.Server.Tests.Subscriptions.SubscriptionPersistenceTests.CreateTenantWithSubscription_OneTransaction_RollsBackTogether`  
  Assert.Equal() Failure: Values differ Expected: 1 Actual: 0
