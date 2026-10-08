# Animora — Technical Stack Specification

> **Purpose of this file.** The single, authoritative **technology stack**
> definition for Animora — *what to use, which exact version, and why this
> technology over the alternatives*. Source of truth for AI agents and
> engineers on every technology decision: languages, runtimes, frameworks,
> libraries, exact package versions, database engine/edition, and their
> documented capabilities, limitations and known issues.
>
> **Split from `ARCHITECTURE.md` (read once, do not re-derive elsewhere).**
> This file states technology facts only — never business flows, bounded
> contexts, conflict policy, layering, deployment topology, or threat model;
> those live in `ARCHITECTURE.md`. **Test:** a technology's own documented
> capability (e.g. "Datasync's delta cursor is `MAX(UpdatedAt)`", "Blazor
> Static SSR holds no circuit") is a **stack fact**, belongs here. What
> Animora *does* with that capability (e.g. "our conflict policy for a
> posted invoice is reversal-only") is an **architecture decision**, belongs
> in `ARCHITECTURE.md`. If a sentence would stay true for *any* product
> built on this technology → stack fact. If true only because Animora chose
> to build it this way → architecture. Every §-pointer below follows this
> rule; it is not repeated at each occurrence.
>
> **Scope.** Technology stack only — languages, runtimes, frameworks,
> packages, exact versions, database engine/edition, technology-level
> capabilities/limitations/known issues, technology selection rationale. No
> feature design, DB schema, business/behavioral architecture, or code.
>
> **Product.** Animora — a **commercial, vendor-hosted (SaaS) accounting +
> clinic-management platform** for veterinary clinics. The vendor runs one
> central, multi-tenant server; clinics subscribe and connect to it and never
> host a server themselves ("self-hosted" = the vendor self-operates the
> server on its own infrastructure rather than on a cloud PaaS — see
> `ARCHITECTURE.md` header / §17, never per-clinic hosting).
> **Targets.** Windows desktop, macOS desktop, Web browser.
> **Platform base.** .NET 10 (LTS) end-to-end, one C# solution, shared domain.
> **Hard runtime rule.** **Zero external-Internet dependency at runtime** — the
> vendor's server environment must run fully even with the Internet cable
> pulled. (This is a product requirement; how the architecture satisfies it is
> defined in `ARCHITECTURE.md` §17.4 — this file only lists which technologies
> are and are not permitted to require Internet connectivity at runtime.)
>
> **Alignment revision (2026-10-08).** Critical sync, local context, SignalR
> launch support and SQL edition choices now match `ARCHITECTURE.md` v3.2.
> No package pins or runtime code change; future adapters are not claimed built.
> Its §24 records primary sources. Older package/native claims below retain
> their own verification dates.
>
> **Evidence status (verified 2026-09).** Every version, limit and
> "documented limitation" below was re-checked against live sources: the
> NuGet flat-container API (exact published versions), the
> CommunityToolkit/Datasync docs + repo, Microsoft Learn (.NET 10 support
> policy, Blazor render modes, EF Core 10, SQL Server 2025 editions, RLS),
> and the Avalonia 12 release. Claim tags: **[fact]** doc-backed ·
> **[decision]** Animora chose this · **[constraint]** an unavoidable limit ·
> **[PoC]** must be validated by proof-of-concept before relied on. Source
> basis: §14.

---

## 0. TL;DR (read this first)

| Concern | Decision |
|---|---|
| Language / runtime | **C# 14 on .NET 10 (LTS)** everywhere — pin the `net10.0` TFM |
| Desktop (Win + Mac) | **Avalonia Native** (single native process, one UI codebase for both OSes) |
| Desktop UI pattern | **MVVM via `CommunityToolkit.Mvvm`** (source-generated, AOT/offline-safe, Microsoft-maintained) |
| Desktop offline store | **Encrypted SQLite + EF Core 10 project-owned DbContext**; optional Datasync context only for a qualified adapter |
| Web | **Blazor Web App** — **Static SSR by default + selective `InteractiveServer` islands** (never global) |
| Shared code | Domain / Application / Contracts / Validation / Sync-abstraction are shared modules across clients; **which** modules are shared and why is defined in `ARCHITECTURE.md` §7.1 — this file only lists the .NET project types that host them |
| Offline sync engine | **Project-owned critical engine + SQL Server Change Tracking** behind Application-owned `IStateSyncEngine`; optional Datasync `10.1.2` SYNC-SIMPLE adapter |
| Sync API | **ASP.NET Core CT-backed critical `/sync/*` contracts**; optional qualified Datasync `TableController<T>`/OData `/tables/*` only |
| Server DB | **SQL Server 2025 Standard (ver17)** production baseline; Express for bounded development/pilot only |
| Client mode matrix | Desktop = offline-capable (SQLite/EF Core present); Web = online-only (documented WASM/Hybrid offline limit of this technology stack) |
| Identity | **ASP.NET Core Identity's built-in bearer/refresh-token mode** (local) — *not* a proprietary token protocol; cookies for web, bearer/refresh for desktop; passkeys optional/post-launch |
| Notifications / broker / backplane | **In-process self-hosted SignalR hints at launch**; no broker/Redis/backplane until measured scale-out |
| Observability tech | **OpenTelemetry to a *local* collector only** — no external telemetry SDK/endpoint wired in by default |
| Server key material | **ASP.NET Core Data Protection** — persisted via `PersistKeysToFileSystem` or the EF Core key store (technology choice; the *recovery invariant* is defined in `ARCHITECTURE.md` §10.6/§17.3) |
| TLS policy | **`SslProtocols.None`** (let the OS negotiate) — technology-level API choice; the *policy rationale* is in `ARCHITECTURE.md` §10.5 |
| Server topology | **Single ASP.NET Core process** at launch (no clustering technology introduced) |

**No numerical quality score is asserted; "10/10" / "production-ready" is
*earned*, not declared** (see §12 Validation Gates). The label may be
raised only after the technology-proving gates (critical CT snapshot correctness, optional Datasync qualification,
SQLCipher integration, SQL RLS/`SESSION_CONTEXT` mitigation, native-binary
version assertion) are proven — the full behavioral/architectural gate suite
lives in `ARCHITECTURE.md` §19. The **core stack is Microsoft first-party**
(.NET 10, C# 14, ASP.NET Core, EF Core, Blazor, SQL Server, Kestrel, ASP.NET Core
Identity) and Avalonia (a mature, .NET-Foundation-adjacent, .NET 10-ready UI
framework). **The optional simple-sync adapter is a deliberate exception:** the
**Datasync Community Toolkit is a .NET Foundation / Community Toolkit project, NOT
a Microsoft first-party product** in the sense SQL Server / ASP.NET are. It is the
contained dependency risk — its exact version pin and fork-readiness are
called out in §4 and §13.

### 0.1 Why a stack file separate from the architecture file

Conflating "which library" with "how the system behaves" is exactly the
failure mode this project's own review history flagged repeatedly — a
technology's own quirk (e.g. Datasync's delta cursor semantics) kept getting
tangled with Animora's own policy (e.g. how *we* resolve a financial
conflict). This file now states only the former. See `ARCHITECTURE.md` §0
for the behavioral counterpart.

---

## 1. Requirement → Technology Mapping

Each product requirement maps to the exact **technology** that satisfies it —
the *pattern/behavior* satisfying each requirement is in `ARCHITECTURE.md` §1.

| # | Requirement | Technology answer |
|---|---|---|
| R1 | Windows + macOS + Web, one .NET codebase | **Avalonia Native** desktop (Win + Mac from one UI codebase) + **Blazor Web App** (web); both target `net10.0` and reference the same shared C# projects. |
| R2 | High performance on **weak servers**, many users/server | Kestrel on ASP.NET Core 10; Blazor Static SSR (no per-session circuit by default); Change Tracking delta extraction with bounded immutable payloads. |
| R3 | Secure, reliable, correct | ASP.NET Core Identity (local); EF Core 10 named query filters; SQL Server `rowversion`; ASP.NET Core Data Protection. |
| R4 | Offline-capable desktop, low server pressure | Avalonia native process + encrypted SQLite + EF Core 10 project-owned local context; Datasync context is optional/adapter-specific. |
| R5 | Multi-user per clinic | SQL Server `rowversion` concurrency + CT committed feed + SignalR wake hints, not authoritative state delivery. |
| R6 | Multi-tenancy, subscription/plan management | EF Core 10 **named** query filters (multiple independent, selectively-disableable filters per entity — a capability introduced in EF Core 10); SQL Server RLS as an available, gated hardening feature. |
| R7 | **Self-hosted, zero external-Internet dependency at runtime** | Every chosen package/runtime component runs fully offline: local SQL Server, local ASP.NET Core Identity, packaged (non-CDN) web assets, local certificate tooling. |
| R8 | Current, standard Microsoft technology | .NET 10 / ASP.NET Core 10 / SQL Server ver17 / EF Core 10 — all current LTS/GA lines, re-verified 2026-09 (§14). |

---

## 2. Platform & Language

- **Runtime:** **.NET 10 (LTS)** — GA 2025-11-11, supported to 2028-11-14. TFM
  `net10.0` everywhere (desktop, web, server, workers, shared libraries).
  **[fact]**
- **Language:** **C# 14**, latest language version shipped with .NET 10. No
  other managed language is introduced (no F#, no VB) to keep the shared
  Domain/Application code base uniform across every project type.
- **Solution shape (project-type mechanics only; *why* each library exists is
  `ARCHITECTURE.md` §7.1):** one .NET solution; every shared concern (domain
  types, application services, contracts, validation, the `IStateSyncEngine`
  abstraction) is a **class library project**, referenced by the desktop,
  web and server projects alike. Avalonia is a class library + native host
  project; Blazor Web App and the server are ASP.NET Core projects.
- **EF Core governance:** stay on **EF Core 10** (the LTS line aligned with
  .NET 10 and the pinned Datasync `10.1.2`). **EF Core 11 is already in
  preview/RC** as of this writing — **do not adopt it**; any jump is a §13
  dependency-review event, not a casual upgrade. **[decision]**

---

## 3. Client Technology (Desktop + Web)

### 3.1 Desktop — Avalonia Native (Windows + macOS)

- **Framework:** **Avalonia 12.x** (latest verified stable **12.1.2**), on
  `net10.0`. Avalonia 12 targets/supports .NET 10. **[fact]**
- **Execution model:** native process → Avalonia rendering → .NET → EF Core →
  SQLite. No browser/WebView component anywhere in this stack.
- **Why Avalonia over MAUI / Blazor Hybrid, as *technologies*, for this
  product:**

  | Criterion | Avalonia Native | MAUI Native | MAUI Blazor Hybrid |
  |---|---|---|---|
  | Windows desktop | Excellent | Excellent | Excellent |
  | macOS desktop | **Native desktop target** | Mac Catalyst (WKWebView) | Mac Catalyst + WebView |
  | Datasync offline host support | **Tested/supported** (Datasync docs) | Tested/supported | **Documented-problematic** (Datasync docs) |
  | SQLite/EF in native process | Yes | Yes | Native process, but Hybrid offline is vendor-flagged |
  | WebView dependency | **None** | None | **Required** |
  | Web UI code sharing | Low | Low/Medium | Highest |
  | Servicing horizon | .NET 10 LTS (2028-11-14) | .NET MAUI 10 EOS **2027-05-11** | Same as MAUI Native |

  MAUI remains a **valid, currently-supported Microsoft technology** — not
  rejected as broken, simply not chosen as baseline **[decision]**, for three
  technology-level reasons: (1) Animora wants a genuine native macOS desktop
  target, not Mac-Catalyst-in-a-WebView; (2) Datasync's own docs flag
  Blazor-Hybrid-in-browser offline hosting as problematic (a sync-library
  fact, not Animora policy); (3) .NET MAUI 10's servicing horizon
  (2027-05-11) is shorter than .NET 10 LTS (2028-11-14). **[fact/decision]**
- **MVVM library:** **`CommunityToolkit.Mvvm`** — source-generated
  `[ObservableProperty]` / `[RelayCommand]`; Microsoft-maintained; the
  default, trimming/AOT-friendly MVVM toolkit for line-of-business Avalonia
  apps. `ReactiveUI` remains an available, allowed dependency for isolated,
  genuinely reactive screens if a concrete technical need appears — it is not
  the baseline. **[decision]**
- **OS support matrix (Avalonia's own).** Published support matrix: Windows
  11 24H2 / macOS 26 = Tier 1; Windows 10 22H2 / macOS 15/14 = Tier 2.
  **[fact]** Animora's own product-support-tier decision on top of this
  matrix is a product decision, outside this file's scope.

### 3.2 Web — Blazor Web App (browser)

- **Framework:** **Blazor Web App**, .NET 10 / ASP.NET Core 10.
- **Render modes available (Microsoft's model):** Static Server-Side
  Rendering, Interactive Server, Interactive WebAssembly, Interactive Auto.
  **[fact]** Which mode Animora uses per page/interaction pattern is
  `ARCHITECTURE.md` §7.3. Their resource-cost mechanics, as documented:
  - **Interactive Server** holds one **circuit per interactive session** — a
    long-lived server-held connection + UI state; circuit count scales
    directly with server memory. **[fact]**
  - **Static SSR** holds no persistent circuit, but still executes queries
    and renders on every request — it is not zero database/CPU cost, only
    zero *circuit* cost. **[fact]**
  - **`[PersistentState]`** (.NET 10 Persistent Component State, including
    across enhanced navigation) avoids double-fetching data across the
    prerender → interactive handoff. **[fact]**
- **Offline capability — a hard, documented platform limitation, not an
  Animora choice:** the Datasync client's offline store (EF Core + SQLite)
  **does not run in Blazor WASM or Blazor Hybrid-in-browser** (stated
  directly in Datasync's vendor docs). **[fact/constraint]** This is the
  technical cause of the web client being scoped online-only
  (`ARCHITECTURE.md` §7.2).
- **Scale-out mechanics (Blazor's own):** `InteractiveServer` islands run
  over **SignalR circuits**; SignalR's hosting/scaling docs require a
  **backplane + sticky sessions** once the web app runs on 2+ instances.
  **[fact]** Whether/when Animora adds this: `ARCHITECTURE.md` §16.2.

### 3.3 Local data store technology (desktop only)

- **SQLite** on-device mirror via **EF Core 10**
  (`Microsoft.EntityFrameworkCore.Sqlite`).
- Critical replica uses a **project-owned EF Core DbContext**, not a mandatory
  Datasync base class. Only qualified optional SYNC-SIMPLE adapters use
  **`OfflineDbContext`** (§4); it never owns the critical journal.
- **Encryption technology:** an encrypted SQLite build —
  **`SQLitePCLRaw.bundle_e_sqlite3mc`** (SQLite3 Multiple Ciphers) or
  SQLCipher (`bundle_e_sqlcipher`) — keyed from a secret held in the **OS
  keystore** (DPAPI on Windows / Keychain on macOS). **[decision]**
  **⚠️ Known limitation:** both
  encrypted bundles are frozen at **native 2.1.11 (< 3.50.2)** and therefore
  still carry **CVE-2025-6965**. Do **not** finalize encryption until the K2
  gate (§12) proves the full combination works, and record the residual
  native-SQLite CVE as an accepted, dated risk until an encrypted ≥ 3.50.2
  build ships.
- **Freeze the exact native/RID matrix — a version string is NOT enough.**
  **[decision]** Encryption is not a pure-managed NuGet dependency: it is a
  chain of `managed package + native package + native SQLite version + cipher
  implementation + RID + CPU architecture`. "Official SQLCipher 4.10+ .NET
  build" (a floating major/minor) is **too loose**. **Security-critical
  dependencies MUST NOT be specified as a floating range (`3.x`, `10.x`,
  `>= …`) in the FINAL RELEASE MANIFEST** — every layer is pinned per RID,
  and the loaded native version is **asserted at startup** (a successful
  managed restore is NOT proof the expected native binary loaded). Pin an
  approved, per-RID native matrix and freeze every layer for each entry:

  | RID | SQLCipher/MC package | native dll/dylib | provider ver | SQLite ver | cipher ver | EF Core ver |
  |---|---|---|---|---|---|---|
  | `win-x64`   | *(pinned)* | *(pinned)* | *(pinned)* | *(asserted)* | *(asserted)* | 10.x |
  | `win-arm64` | *(pinned)* | *(pinned)* | *(pinned)* | *(asserted)* | *(asserted)* | 10.x |
  | `osx-x64`   | *(pinned)* | *(pinned)* | *(pinned)* | *(asserted)* | *(asserted)* | 10.x |
  | `osx-arm64` | *(pinned)* | *(pinned)* | *(pinned)* | *(asserted)* | *(asserted)* | 10.x |

  The concrete versions are filled in and frozen by the **K2 Security Gate**
  PoC on each RID (§12); CI publishes them in the release **native binary
  inventory** (§16, §13).
- **NU1903 / CVE-2025-6965:** the SQLite provider transitively pulls
  `SQLitePCLRaw.lib.e_sqlite3 2.1.11` (deprecated), which triggers a
  **high-severity NU1903** (GHSA-2m69-gcr7-jv3q / CVE-2025-6965: native
  SQLite **< 3.50.2** memory corruption). The advisory itself lists
  **"Patched versions: None"**, so **a version string alone does not fix
  it — the *native binary* must be ≥ 3.50.2**. What supplies that today:
  `SQLitePCLRaw.bundle_e_sqlite3` **3.0.x** (its `3.0.3` depends on
  `SourceGear.sqlite3 3.50.4.5` → native 3.50.4), or adding
  **`SourceGear.sqlite3 3.50.4.5`** explicitly. **Mandatory: probe
  `SELECT sqlite_version();` at startup and assert ≥ 3.50.2** — the NuGet
  graph is not proof. **⚠️ Encrypted bundles (`bundle_e_sqlcipher`,
  `bundle_e_sqlite3mc`) are still on the 2.1.11 native line and do NOT clear
  this CVE (§#12).** If the native fix can't be resolved on the pinned
  stack, use a **documented, dated `NuGetAuditSuppress`**, never a silent
  ignore. **[fact]**
- **SQLite provider technology limitations (documented by Microsoft):**
  SQLite does not natively support `DateTimeOffset`, `decimal`, `TimeSpan`,
  `ulong` — EF Core can read/write and do equality, but **comparison and
  ordering evaluate client-side**. **[fact]** Consequence for this stack:
  - **Money:** persist as **integer minor units + Currency**; the
    high-precision domain `Money` concept and its rounding/tax math are a
    domain-model concern (`ARCHITECTURE.md` §11.1), not a storage-technology
    concern. Currencies differ in minor-unit scale (JPY = 0, most = 2, some
    historically 3) — the scale is currency-specific at the storage layer.
  - **Date/time:** store **UTC**; use `DateTimeOffset` only via an EF
    `ValueConverter` (SQLite defaults to second-accuracy otherwise). **[fact]**

---

## 4. Sync Engine Technology

**Critical engine: project-owned .NET adapter + SQL Server Change Tracking.**
CT exposes committed net changes through `CHANGETABLE`, `CURRENT_VERSION` and
per-table `MIN_VALID_VERSION`; it is not historical CDC/event logging. Explicit
transaction-level SNAPSHOT requires `ALLOW_SNAPSHOT_ISOLATION`; statement RCSI
is not equivalent. CT retention/column tracking/auto-cleanup are configured and
monitored. Cursor, rowversion and UpdatedAt remain distinct (§6.2). Batch/epoch/
apply/receipt semantics live in `ARCHITECTURE.md` §8/§17-CT; .NET/SqlClient/EF Core
implement them without a replication-library dependency.

**Optional adapter: CommunityToolkit.Datasync (`CommunityToolkit.Datasync.*`).**
Library facts below apply **only** to qualified SYNC-SIMPLE entities. This .NET
Foundation/Community Toolkit project is not Microsoft first-party. Retaining its
seam does not mandate building/installing it at launch.

> **⚠️ MANDATORY EXACT VERSION PIN (verified 2026-09 against NuGet).** Published
> client/server versions today are `8.0.x → 9.0.x → 10.0.0 → 10.0.1 → 10.1.0 →
> 10.1.1 → **10.1.2** (latest)`. **Pin the exact `10.1.2`** on both client and
> server for this critical infrastructure — do **not** float `>= 10.x`. The 10.x
> line requires .NET 10 / EF Core 10; the 9.x line targets .NET 9 / EF Core 9 and
> is a runtime mismatch under EF Core 10. Upgrade only through the §13 dependency
> review. **[fact/constraint]**
>
> Manage the pin centrally in `Directory.Packages.props`:
> ```xml
> <PackageVersion Include="CommunityToolkit.Datasync.Client" Version="10.1.2" />
> <PackageVersion Include="CommunityToolkit.Datasync.Server" Version="10.1.2" />
> ```

> **Current documentation floor (2026-10-08).** Client/server guides state
> .NET/ASP.NET 10 or later; older 8+ examples are not pinned-10.x evidence.
> The exact package pin stays subject to §13 review; a living documentation
> site does not prove pinned-binary behavior. **[fact/PoC]**

### 4.1 Access pattern (technology integration point)

> **Domain and Application code never reference `CommunityToolkit.Datasync`
> directly.** The adapter sits behind Application-owned `IStateSyncEngine`
> interface (defined and governed in `ARCHITECTURE.md` §8.1) to isolate
> third-party dependency risk, version churn, and a possible fork
> requirement — stated here only as the integration boundary this
> library must respect; the interface's own design is an architecture
> artifact.

### 4.2 What the library technically replicates

The library's replication surface is flat, denormalized entity contracts —
technically, any EF Core entity registered with an `OfflineDbContext` /
`TableController<T>`. Every synchronizable entity, as the library requires,
carries: **`Id`** (string PK, globally unique), **`UpdatedAt`**
(`DateTimeOffset?`), **`Version`** (`string?`/`byte[]?`, opaque, changes on
every server write), **`Deleted`** (bool, soft-delete). **[fact]**
**Local-only tables** use the library's **`[DoNotSynchronize]`** attribute
(device settings, UI cache, local logs, sync metadata). **[fact]** Which
*business* entities Animora chooses to route through this replication
surface versus the separate command pipeline is `ARCHITECTURE.md` §8.2/§8.3.

- **OData v4** is this library's own wire protocol for the `/tables/*`
  surface — a Datasync implementation detail, not a general-purpose query
  language Animora exposes anywhere else (`ARCHITECTURE.md` §8.2 states the
  resulting architectural rule that no business client may depend on OData
  semantics).

### 4.3 Push/pull mechanics (as implemented by the library)

- **Push (client → server):** each local `SaveChanges` enqueues to the
  library's own operations queue (`DatasyncOperationsQueue`, read-only from
  the app's perspective). `PushAsync()` drains it. Default
  **`ParallelOperations = 1`** — raising it 1–8 may reorder operations
  server-side; Animora keeps the default for exactly this reason. **[fact]**
- **Pull (server → client):** incremental, delta-token based. Each
  entity/query stores a token in the library's **`DatasyncDeltaTokens`**
  table; its value is the **maximum `UpdatedAt`** of the pulled set (ms
  since UNIX epoch) — the library's own delta-cursor algorithm. **[fact]**
  Pages serialize per entity; different entities may parallelize. The
  library's **`QueryId`**-scoped requests handle the same entity pulled
  under different filters. Initial **`PageSize` 25–100**, tune per the
  load-test gate (§12).
- **Identity/ordering vocabulary (library-defined; do not conflate with
  Animora's own concepts below):**
  ```
  DatasyncOperationId = replication OPERATION identity (a queued CRUD op) —
                        transport bookkeeping only
  UpdatedAt           = incremental-sync CURSOR basis
  Version             = OPAQUE concurrency metadata (changes on every write)
  ```
  Animora separately defines `OperationId`, `BusinessSeq`,
  `CausationId`/`DependsOnOperationId` for its business-command pipeline —
  architecture artifacts governed in `ARCHITECTURE.md` §8.3/§8.4, not
  library concepts.
- **`UpdatedAt` collision safety — a library-level correctness question,
  gated by PoC (§12, G1):** because the delta cursor is the max
  `UpdatedAt`, many writes in the *same millisecond*, under paged pull, must
  not hide changed rows. This must be proven against the exact pinned
  `10.1.2` under: same-ms burst, insert-while-paging, delete/update
  interleaving, page retry, disconnect mid-page, duplicate page delivery,
  concurrent writes between page requests, and clock skew between service
  nodes. If the gate fails, the affected entities are bypassed/reimplemented
  behind `IStateSyncEngine` on the CT-backed critical path
  instead of this adapter's timestamp cursor; CT versions may be shared by rows.
- **Library re-sync example:** removes pending ops/delta tokens before pull.
  It is not Animora's recovery procedure and must never clear its journal.
- Client context is not thread-safe: local updates are sequential even when
  service requests run in parallel (current client guide). **[fact]**

### 4.4 Conflict-resolution primitives the library ships

The library ships exactly **two generic resolvers**:
**`ClientWinsConflictResolver`** and **`ServerWinsConflictResolver`**
(`IConflictResolver` / `IConflictResolver<T>`), implementing last-write-wins
on the whole row. **[fact]** No field-aware or domain-aware resolution
exists beyond these two; anything more sophisticated is a custom
`IConflictResolver<T>` against domain-owned rules. Which Animora data
types may use the built-ins is `ARCHITECTURE.md` §8.5 (business decision).

### 4.5 Sync-contract wire format

The library requires a stable wire shape per entity (§4.2's fields) and
provides the transport only — it has no contract-versioning policy of its
own. Animora's own DTO versioning and mixed-fleet compatibility policy on
top of this wire shape is `ARCHITECTURE.md` §8.6.

---

## 5. Server Technology

> **One host, distinct surfaces.** Business command APIs and project-owned
> CT critical-sync APIs use ASP.NET Core 10. Optional Datasync controllers are
> limited to qualified SYNC-SIMPLE entities; architecture §4/§8 owns semantics.

### 5.1 Runtime & host

- **ASP.NET Core 10 + Kestrel**, packaged local assets and hosted workers.
- **Self-hosted ASP.NET Core SignalR** (shared framework) for launch hints;
  no Redis/backplane package required in one process.
- **First-party OpenAPI** (`Microsoft.AspNetCore.OpenApi` / `AddOpenApi` /
  `MapOpenApi`) is default. Current Datasync docs support .NET 10 OpenAPI,
  NSwag and Swashbuckle: NSwag is **not a mandatory library requirement**.
  An optional integration is qualified against the pinned adapter before use.

### 5.2 Datasync server technology

- If the adapter is admitted, one **`TableController<TEntity>`** per qualified SYNC-SIMPLE entity under
  `/tables/*` (`Microsoft.AspNetCore.Mvc.RouteAttribute`). Registered via
  `AddDatasyncServices()`. Repository technology:
  `EntityTableRepository<T>` over EF Core 10 on SQL Server.
- **`TableControllerOptions`** (library configuration surface): conservative
  `PageSize` (25–100); **`EnableSoftDelete = true`**;
  **`UnsafeEntityLogging = false`** (never log full financial/PII rows —
  this is a library logging flag, applied here as a hard configuration
  requirement).

### 5.3 Identity technology

- **ASP.NET Core Identity's built-in bearer/refresh-token mode** (local) —
  a platform-provided **opaque protected token**, not JWT/OIDC or an Animora-invented format. Web = **cookies** (Microsoft's
  documented recommendation for browser apps); desktop = built-in
  **bearer + refresh** tokens. **Passkeys** are supported by .NET 10
  Identity but are optional/post-launch for this product. **[fact/decision]**
- `IAccessControlProvider<T>` is the Datasync library's own extension point
  for scoping replicated data per caller — **what Animora puts inside it
  (tenant scoping logic) is `ARCHITECTURE.md` §9.2**; this is only the
  library-provided hook.

### 5.4 Background worker technology

- .NET 10 **`BackgroundService`** (hosted service, JIT) is the worker
  hosting technology for all async processing.
- **Native AOT is an OPTIONAL optimization at the technology level, not a
  default.** **[fact/decision]** EF Core + Native AOT is officially
  *experimental / not recommended for production* by Microsoft; any worker
  using EF Core **must not** be published Native AOT at launch. Exhaust
  cheaper levers first (query shape/`AsNoTracking`, indexes, projections,
  connection pooling, caching, batching, bounded concurrency) before
  considering AOT for a future, EF-free worker only.
- **EF Core compiled models — not adopted until the EF Core 10 support
  matrix proves compatibility with Animora's named tenant filters and model
  configuration.** Phrase as "not yet adopted", not "forbidden" — a
  technology-compatibility question, to be re-checked, not a permanent ban.

### 5.5 Claim classification for technology statements

> **Separate `FACT` from `DECISION` from `VALIDATION REQUIRED`.**
> **[decision]**
> ```
> FACT               = doc-backed and true today about the technology
>                      (e.g. ".NET 10 is LTS to 2028-11")
> DECISION           = a chosen technology (e.g. "Avalonia is the desktop
>                      framework")
> VALIDATION REQUIRED = a technology combination that must be proven by PoC
>                      before it is relied on (e.g. "Avalonia + SQLCipher +
>                      Datasync together")
> ```
> This taxonomy governs every tag used throughout this file.

---

## 6. Database Technology

### 6.1 Engine & edition

- **SQL Server 2025 (ver17)** via EF Core 10. **[fact]**
- **Edition guidance** (verified against the SQL Server 2025 editions page):
  - **Development:** Developer edition (full features, non-production).
  - **Bounded development/pilot:** **Express** — max relational DB **50 GB**,
    **max 4 cores** (lesser of 1 socket or 4 cores), buffer pool
    **1,410 MB**. Plausible small-clinic tier, but **CPU/RAM will bind
    before 50 GB does**. Not the central production baseline. **[fact/decision]**
  - **Central production:** **SQL Server 2025 Standard** (lesser of 4
    sockets/32 cores, 256 GB buffer pool). **Resource Governor is available
    in 2025 Standard**, unlike earlier Standard releases. Edition ceilings
    are not a measured capacity result. **[fact/decision]**
  - **Row-Level Security is available in Express, Standard and Enterprise**
    — an RLS-based hardening layer (if used — `ARCHITECTURE.md` §9.2) works
    even on the small-clinic tier. **[fact]**
- **Why SQL Server over PostgreSQL** (Postgres is technically excellent —
  this is ecosystem alignment, not a knock): .NET-first ecosystem, EF Core
  integration, `rowversion`, RLS with `SESSION_CONTEXT`, SQL tooling,
  Windows/self-host friendliness, and a free **Express** on-prem tier.
  **[decision]**
- Apply data/index compression to keep footprint small on weak hardware.

### 6.2 Concurrency primitive

- **`rowversion`** is the SQL Server data type used for optimistic
  concurrency detection on mutable entities — EF Core adds it to the
  `WHERE` clause automatically; a 0-row update raises
  `DbUpdateConcurrencyException`. **[fact]** What Animora *does* with that
  exception (reload + domain conflict policy) is `ARCHITECTURE.md` §11.4 —
  this is the raw database/EF mechanic only.
- **SQL Server Change Tracking is the critical state feed**, separate from
  optional Datasync timestamp deltas. Functions, committed CT versions and
  rowversion tokens are distinct; CT is not an event history. SNAPSHOT enumeration
  and retention are mandatory architecture integration contracts (§4).
- SQL 2025 introduces adaptive shallow CT cleanup enabled by default for large
  side tables. Monitor effective minimum-valid versions/cleanup/storage; never
  treat configured retention days as proof a cursor is valid. **[fact]**

---

### 6.3 Row-Level Security technology

- **RLS middle-tier pattern (as documented by Microsoft):** the app sets the
  tenant on each pooled connection via **`sp_set_session_context 'TenantId'`**
  (`@read_only = 1`); a FILTER predicate scopes reads and a BLOCK predicate
  stops cross-tenant writes. Keep RLS objects in a dedicated `Security`
  schema, `SCHEMABINDING = ON`. **[fact]**
- **Documented `SESSION_CONTEXT` known issue:** wrong results/AVs under
  parallel plans after session reuse; **trace flag 11042** forces serial function
  execution. Validate exact SQL build/CU and mitigation under pooling; it is not
  a claim that an arbitrary deployment is automatically safe. Use a proven
  connection-open/reset lifecycle before setting read-only keys. **[fact/PoC]**


- Whether RLS is enabled at all, and what it may be relied upon for, is an
  architecture/security decision (`ARCHITECTURE.md` §9.2, mandatory-vs-
  hardening layering) — this section documents only the database feature's
  mechanics and known defect.

### 6.4 EF Core 10 filter technology

- EF Core 10 introduced **named query filters** — multiple **independent,
  named** filters per entity, attachable/toggle-able independently (e.g.
  `IgnoreQueryFilters(["SoftDelete"])` without dropping a separately-named
  tenant filter). **[fact]** Pre-.NET-10 EF Core allowed only a single
  anonymous filter per entity — this is the concrete technical reason the
  server `DbContext` requires EF Core 10, independent of any other EF Core
  10 feature. **[fact]**
- **⚠️ Required-navigation pitfall:**
  a *required* navigation into a filtered entity emits `INNER JOIN` and can
  **silently drop parent rows** — documented by Microsoft on the Global
  Query Filters page. Documented mitigations: an **optional** navigation
  (`LEFT JOIN`) or **matching filters on both entities**. Animora's
  application to tenant-scoped navigations: `ARCHITECTURE.md` §9.2.

---

## 7. Reliability Technology Components

> **What pattern governs reliability (Outbox, Idempotent Consumer, Competing
> Consumers, Retry/Circuit-Breaker/Bulkhead) is `ARCHITECTURE.md` §12 — this
> section lists only the .NET/SQL Server technology used to implement them.**

- **Transactional writes:** a single EF Core 10 `DbContext.SaveChanges`
  transaction covers both a business-state change and its accompanying
  Outbox-table row — an ordinary relational transaction, no special
  technology beyond EF Core + SQL Server.
- **Outbox relay:** a .NET 10 `BackgroundService` polling loop. Its atomic
  claim uses SQL atomic conditional update/OUTPUT with due/expired eligibility
  and a fenced lease token. Consumer dedup uses a uniqueness constraint with
  marker/effects in one transaction. A nullable initial lease is not accidentally
  excluded by a bare `< @now` predicate. Isolation-specific hints are PoC-gated.
- **No external message broker or distributed cache package is referenced
  at launch** — no Kafka/RabbitMQ/Azure Service Bus/Redis client library is
  added to the dependency graph until a measured, ADR-gated scale-out need
  exists (`ARCHITECTURE.md` §16.2).
- **(Scale-out only, not launch) `Microsoft.AspNetCore.SignalR.
  StackExchangeRedis`** — the one package that would be added if/when the
  web app runs on 2+ instances, per Blazor's own SignalR-backplane
  requirement (§3.2).

---

## 8. Security Technology

| Layer | Technology |
|---|---|
| AuthN | **ASP.NET Core Identity's built-in bearer/refresh-token mode** (local). Web = cookies; desktop = bearer + refresh. Passkey support exists in .NET 10 Identity (optional/post-launch). |
| AuthZ primitives | **RBAC + policy-based + resource-based authorization** — all three are first-class ASP.NET Core 10 APIs (`IAuthorizationService.AuthorizeAsync(user, resource, policy)` for resource-based checks). |
| Row-level hardening | **SQL Server RLS** (§6.3) — an available, gated database feature. |
| Transport | **HTTPS via Kestrel**, using an internally-issued certificate (no public CA at runtime). |
| At rest (desktop) | Encrypted SQLite (SQLite3 Multiple Ciphers / SQLCipher, §3.3) + OS keystore (DPAPI/Keychain) for the encryption key. |
| Web security primitives | ASP.NET Core's built-in secure-cookie options, antiforgery middleware, and standard security-header middleware. |
| Server key material | **ASP.NET Core Data Protection** — `PersistKeysToFileSystem` to a stable path, or the EF Core key store; optionally protected at rest via DPAPI/X.509. |
| TLS protocol negotiation | **`SslProtocols.None`** (let Schannel/the OS negotiate the highest mutually-supported protocol) — the .NET/OS-level API choice; obsolete protocols (SSL 3.0/TLS 1.0/TLS 1.1) are disabled at the OS/Schannel policy layer, not hard-coded in application code. |
| Local CA tooling | An internally-operated CA (technology: standard X.509 certificate issuance tooling — e.g. OpenSSL-class tooling or a Windows-native CA role) generates the server certificate; no public-CA client library is required at runtime. |

Financial-integrity behavior, offline-session policy, tenant-safe caching
rules, and the file-access model are architecture decisions
(`ARCHITECTURE.md` §9–§10) — not duplicated here.

### 8.1 Data Protection technology detail

- **What depends on it (Microsoft-documented):** cookie authentication,
  antiforgery tokens, `TempData`, session payload protection, any
  `IDataProtector`-protected value. If the key ring is lost/regenerated, all
  existing cookies/antiforgery tokens become undecryptable. **[fact]**
- **Persistence options:** `PersistKeysToFileSystem(<a stable path>)` **or**
  the **EF Core key store** (keys in SQL Server). Set a **stable
  application name** (`SetApplicationName`) so the ring is not re-scoped
  across restarts/paths. This persistence is a mandatory deployment
  invariant on the disaster-recovery matrix: `ARCHITECTURE.md` §10.6/§17.3.

### 8.2 TLS technology rationale

- **Why not hard-code `SslProtocols.Tls13`:** TLS 1.3 is not available on
  every OS in Animora's support matrix (e.g. Windows 10 / older macOS do not
  support it). **[fact]** `.NET 10` uses TLS 1.3 automatically where the OS
  supports it — which is exactly why the protocol-version choice is left to
  the OS/Schannel layer rather than hard-coded in this stack.

---

## 9. Observability Technology

- **Structured logging:** any .NET structured-logging library compatible
  with `Microsoft.Extensions.Logging` (e.g. Serilog or the built-in
  provider), writing to local file sinks.
- **Health checks:** ASP.NET Core's built-in health-check middleware
  (`Microsoft.Extensions.Diagnostics.HealthChecks`), exposing liveness and
  readiness endpoints locally.
- **Metrics/traces:** **OpenTelemetry** SDK **exporting only to a local
  collector on the LAN** — never a mandatory external endpoint. Removing the
  local collector must not break the app (an architecture invariant,
  `ARCHITECTURE.md` §15, enforced here by never wiring a default external
  exporter).
- **No Application Insights, no cloud log sink, no external metrics backend**
  is referenced as a runtime dependency anywhere in this stack.

---

## 10. Performance-Relevant Technology Features

> **The performance *strategy* (push work to clients, delta-only sync,
> static-first rendering, jittered reconnect) is `ARCHITECTURE.md` §16 —
> this section lists only the concrete .NET/EF Core/ASP.NET Core features
> that implement it.**

- **ASP.NET Core 10 automatic memory-pool eviction** — a runtime feature
  that relieves memory pressure without application code changes.
- **EF Core efficient-querying features used throughout:** `AsNoTracking`,
  projections, EF Core 10 named query filters, parameterized/compiled
  queries where beneficial, standard SQL Server indexing + index
  compression.
- **Blazor virtualization** (`Virtualize` component) for long grids
  (patients, invoices, inventory) — a Blazor-provided rendering-performance
  feature.
- **`[PersistentState]`** (.NET 10) — see §3.2.

---

## 11. Canonical Package / Component List

> Canonical dependencies. Pin all to the **.NET 10** line; pin the **exact**
> Datasync version. Manage centrally in `Directory.Packages.props`.

**Client (desktop — Avalonia Native; §3.1)**
- `Avalonia` **12.x** (verified stable **12.1.2**) + Avalonia desktop platform packages
- `CommunityToolkit.Mvvm` (source-generated MVVM)
- `Microsoft.EntityFrameworkCore.Sqlite` **10.x** (EF Core 10 line)
- **Optional SYNC-SIMPLE:** `CommunityToolkit.Datasync.Client` **= 10.1.2** behind `IStateSyncEngine`
- Native SQLite with **runtime version ≥ 3.50.2** (CVE-2025-6965 — §3.3):
  `SQLitePCLRaw.bundle_e_sqlite3` **3.0.x** (transitively `SourceGear.sqlite3
  3.50.4.5`) **or** explicit `SourceGear.sqlite3 3.50.4.5`; **encrypted** builds
  (`bundle_e_sqlite3mc` / `bundle_e_sqlcipher`) are still on native 2.1.11 →
  PoC-gated residual CVE risk (§3.3, §12 Gate K2)

**Shared class libraries (referenced by both clients + server)**
- `Animora.Domain` · `Animora.Application` · `Animora.Contracts`
- Application-owned `IStateSyncEngine` contract and critical .NET adapter;
  `Animora.Sync.Abstractions` is currently a scaffold. Optional Datasync adapter
  is an Infrastructure component, **not** a shared Domain/client/server dependency
  or a claim that `Animora.Sync.Datasync` already exists.

**Web**
- Blazor Web App — Static SSR + selective `InteractiveServer` islands (technology capability; usage policy in `ARCHITECTURE.md` §7.3)

**Server (sync surface + business API + app; §5)**
- ASP.NET Core 10 (Kestrel)
- **Optional SYNC-SIMPLE:** `CommunityToolkit.Datasync.Server` **= 10.1.2** + `.Server.EntityFrameworkCore` **= 10.1.2**
- `Microsoft.AspNetCore.OpenApi` **10.x** — first-party OpenAPI for the Business API
- `CommunityToolkit.Datasync.Server.NSwag` — optional only if chosen/qualified instead of first-party OpenAPI
- `Microsoft.EntityFrameworkCore.SqlServer` **10.x** (EF Core 10 line)
- Local ASP.NET Core Identity + in-process SignalR shared-framework support; passkeys optional/post-launch
- **(scale-out only)** `Microsoft.AspNetCore.SignalR.StackExchangeRedis`

**Workers**
- .NET 10 `BackgroundService`. **Do NOT publish EF-Core workers as Native AOT** (§5.4).

**Database**
- **SQL Server 2025 Standard (ver17)** — Change Tracking + SNAPSHOT, `rowversion`,
  Resource Governor and optional gated RLS. No CDC/broker is required for sync.

> **EF Core 11 governance note.** EF Core 11 is already in preview/RC as of this
> writing. **Do not adopt it** — stay on the **EF Core 10 LTS line aligned with
> .NET 10 LTS and the pinned Datasync 10.1.2**. Any jump is a §13 dependency-review
> event, not a casual upgrade. **[decision]**

> **Supply-chain governance (SBOM + native inventory + suppression policy).** **[decision]**
> - **SBOM + dependency pin + `NuGetAuditMode=all` + lock-file mode** are mandatory,
>   **plus** a **native binary inventory** in the release manifest (`DLL · DYLIB ·
>   SQLite · SQLCipher/e_sqlite3mc · OS-native deps` with version + RID — §3.3, §16).
>   A NuGet SBOM alone does **not** cover the native crypto/DB layer.
> - **Any vulnerability suppression must record:** `advisory ID · justification ·
>   affected package · compensating control · expiry date · owner`. **No *permanent*
>   suppression for native database/crypto** (SQLCipher / e_sqlite3 / e_sqlite3mc) —
>   those must be time-boxed and re-reviewed.

---

## 12. Technology Validation Gates (PoCs)

> **These gates prove that a specific pinned *technology* behaves as
> required.** The full behavioral/architectural test suite (business-command
> idempotency, tenant-isolation-with-hardening-off, sync-failure UX,
> mixed-fleet compatibility, file lifecycle, licensing anti-cloning, etc.) is
> `ARCHITECTURE.md` §19 and is **not** duplicated here — this section is
> deliberately narrow.

| Gate | What it proves (about the technology) |
|---|---|
| **G1 — Optional Datasync qualification** | Against the exact pinned `10.1.2`: paged incremental pull under same-ms bursts, inserts-while-paging, delete/update interleaving, page retry, disconnect mid-page, duplicate page delivery, concurrent writes between pages, and clock skew → **zero unrecoverable skip AND zero unrecoverable duplicate**. Failure ⇒ use the existing custom CT critical path behind `IStateSyncEngine` (§4.3), not a new timestamp-based sequence. |
| **K2 — SQLCipher/Multiple-Ciphers integration** | The full encrypted-data-path lifecycle **per RID** in the approved native matrix (§3.3): create encrypted DB → close → reopen with correct key succeeds → reopen with wrong key fails → a plain (non-cipher) SQLite reader cannot read it → EF Core 10 migration succeeds on the encrypted DB → WAL mode + crash-recovery → backup → restore (still encrypted, still openable) → `PRAGMA rekey` (old key fails, new key works). Passing K2 **freezes** the native/RID matrix values in §3.3. |
| **Native SQLite version assertion** | At every process startup (desktop and server, every RID), `SELECT sqlite_version();` returns **≥ 3.50.2**; a successful managed NuGet restore is explicitly **not** accepted as proof — only the runtime probe is (§3.3). |
| **SQL RLS + `SESSION_CONTEXT` + connection pooling** | If RLS is enabled at all: a pooled connection never inherits a prior request's `SESSION_CONTEXT`, and a parallel-plan read never returns another tenant's rows, **with trace flag 11042 active** (§6.3). If this cannot be reliably proven, RLS stays **off** — the mandatory application-owned isolation layers (`ARCHITECTURE.md` §9.2) must already be sufficient without it. |
| **CT snapshot/provider integration** | Real SQL 2025: snapshot/min/current/changes/rows under concurrent cleanup and DML, direct DML visibility, same-version ties, nullable functions, epoch reset and bounded nonbuffering extraction; architecture Gates F/W/AI/AO still required. |

**G1** gates only entities electing optional Datasync, never the custom CT engine.
**K2** gates encrypted local storage per RID; CT/provider integration gates the
critical feed. These are obligations, not results of this document revision.

---

## 13. Dependency Risk Register (verified 2026-09)

The critical sync platform uses Microsoft SQL/.NET primitives with **Animora-owned
correctness code**; optional Datasync is a contained third-party adapter. Library
risk is not a reason to omit verification of the custom engine.

| # | Risk | Evidence | Impact | Mitigation |
|---|---|---|---|---|
| D1 | **Datasync major-line ↔ EF/.NET mismatch** | 9.x targets .NET 9/EF Core 9; 10.x requires .NET 10/EF Core 10. Latest published = **10.1.2** (NuGet flat-container, 2026-09). | Runtime failure of the offline-sync technology if lines are crossed. | **Pin exact `10.1.2`** client+server + EF Core 10 everywhere; CI fails if a 9.x toolkit or EF Core 11 resolves. Upgrade only via this register. |
| D2 | **Datasync maintenance / bus-factor; NOT first-party** | .NET Foundation / Community Toolkit project; community-maintained. | Slower fixes for future LTS jumps; possible self-maintenance. | (a) Optional adapter usage **behind `IStateSyncEngine`** — swappable (`ARCHITECTURE.md` §8.1). (b) **Vendor/fork-ready:** track the exact commit, be able to build from source. (c) Prefer the standard OData v4 CRUD/delta contract (re-implementable). |
| D3 | **Datasync offline unsupported in browser hosts** | Docs: EF Core/SQLite offline store unsupported in Blazor WASM / Blazor Hybrid-in-browser; **tested offline hosts = Avalonia / MAUI / Uno / WinUI 3 / WPF**. | Web can't be offline; a Hybrid desktop would be unstable offline. | **Avalonia native desktop** (a *tested* host) is the baseline (§3.1); web is scoped online-only at the architecture level (`ARCHITECTURE.md` §7.2). |
| D4 | **Local SQLite NU1903 / CVE-2025-6965** | `lib.e_sqlite3 2.1.11` (deprecated) pulled transitively; advisory GHSA-2m69-gcr7-jv3q lists **"Patched versions: None"**. **Encrypted bundles still on 2.1.11 native.** | High-severity audit warning; unpatched native SQLite; **encryption re-opens the CVE**. | Ensure **native SQLite ≥ 3.50.2 at runtime** via `bundle_e_sqlite3 3.0.x` / `SourceGear.sqlite3 3.50.4.5`; **probe `sqlite_version()` on startup**. For encryption, gate on the §12 K2 PoC and record residual risk. Documented `NuGetAuditSuppress` if unresolved. |
| D5 | **Web can't scale horizontally without a backplane** | ASP.NET Core 10 SignalR hosting/scaling: Redis backplane + sticky sessions for multi-instance Blazor circuits. | Broken circuits when a 2nd web node is added. | Launch single-instance; add **Redis backplane** as a deploy switch, not a code change (`ARCHITECTURE.md` §16.2). |
| D6 | **`decimal`/`DateTimeOffset` SQLite limitations** | EF Core SQLite provider limitations doc. | Wrong range/ordering for money/time if modelled naively. | **Integer minor units** for money storage; `DateTimeOffset` via `ValueConverter` (§3.3). |

> **Golden rule:** domain/repository code must never bind directly to a Datasync
> concrete type where an interface will do. This keeps D1/D2/D3 contained to the
> infrastructure layer — the single most important lever for surviving a Datasync
> problem (swap engine) without a domain rewrite.

---

## 14. Source Basis (why this is current, not guessed)

Alignment facts rechecked **2026-10-08** against the primary-source bodies
registered in `ARCHITECTURE.md` §24: CT/SNAPSHOT/cleanup, SQL 2025 Standard
Resource Governor, EF buffering/pooling and current Datasync server/OpenAPI docs.
Other package/native entries below retain their prior verification dates.

Verified **2026-09** against live sources:

- **.NET 10 (LTS, GA 2025-11-11, supported to 2028-11-14)** — support policy;
  latest patch line 10.0.x. TFM `net10.0`, C# 14. **[fact]**
- **CommunityToolkit.Datasync** — client offline guide (`.NET 10.x or later`;
  offline unsupported in WASM/Hybrid; tested hosts Avalonia/MAUI/Uno/WinUI/WPF;
  `Id`/`UpdatedAt`/`Version`/`Deleted`; op-queue + `PushAsync` `ParallelOperations`
  1–8 default 1; delta token = max `UpdatedAt` in `DatasyncDeltaTokens`; `QueryId`
  scopes; `ClientWins`/`ServerWins` built-in resolvers; `[DoNotSynchronize]`; NU1903
  / issue #492). **Latest published version `10.1.2`** (NuGet flat-container API).
  **Re-verified 2026-10-07:** client, server and server.EntityFrameworkCore all publish `10.1.2`; the client
  nuspec targets `net10.0` + EF Core `10.0.9`.
  **Not a Microsoft first-party product.**
- **Avalonia 12** — .NET 10-ready; latest stable **12.1.2** (NuGet). **[fact]**
- **EF Core 10** — **Named Query Filters** (multiple independent filters per entity,
  selectively disableable), SQLite provider + limitations, optimistic concurrency,
  transactions, efficient querying; **EF Core 11 already in preview/RC — not
  adopted**. **[fact]**
- **ASP.NET Core 10** — Blazor render modes (Static SSR / Interactive Server /
  Interactive WebAssembly / Interactive Auto; circuit = per-session server state),
  `[PersistentState]`, virtualization, SignalR hosting/scaling (Redis backplane +
  sticky sessions), ASP.NET Core Identity (passkeys available). **[fact]**
- **SQL Server 2025 (ver17)** — editions page: Express **50 GB / max 4 cores /
  1,410 MB buffer pool**; Standard **32 cores / 256 GB**; **RLS available in all
  editions incl. Express**; RLS `SESSION_CONTEXT` middle-tier pattern. **[fact]**
- **EF Core Native AOT / precompiled queries** — documented experimental / not for
  production → EF-Core workers stay JIT. **[fact]**

> When updating this file, re-verify against the same doc set for the then-current
> .NET LTS and SQL Server versions and the exact published Datasync version; never
> downgrade to pre-.NET-10 patterns.

---

## 15. Technology Usage Rules for AI Agents (mandatory)

> **These rules govern technology usage only.** Behavioral/business rules
> (idempotency, conflict policy, tenancy authorization, offline-session
> policy, licensing, file-access model, etc.) are `ARCHITECTURE.md` §21 —
> not repeated here.

```
T-RULE 001  Domain references no sync abstraction; Application owns IStateSyncEngine, Infrastructure owns CT/Datasync adapters.
T-RULE 002  Never change the pinned Datasync version without the full integration
            suite (§12) — client and server versions move together, always exact.
T-RULE 003  Never let a 9.x Datasync package or EF Core 11 resolve in the
            dependency graph — CI fails the build if it does.
T-RULE 004  Package/managed version is NOT proof of the loaded native binary
            version; assert the actual native SQLite version at startup (§3.3).
T-RULE 005  Static SSR is a circuit-cost fact (no circuit), not a database-load
            guarantee — do not describe it as "free" (§3.2).
T-RULE 006  UpdatedAt / Version / DatasyncOperationId are library-defined
            concepts; never redefine or repurpose them as a business concept.
T-RULE 007  No business client depends on OData query semantics — OData is a
            Datasync `/tables/*` implementation detail only (§4.2).
T-RULE 008  A timestamp column is a storage/cursor fact only — never treat it as
            a critical synchronization cursor; optional Datasync requires §12 G1 evidence.
T-RULE 009  Never introduce a message broker, Redis, or a clustering technology
            package without the explicit ADR the architecture requires.
T-RULE 010  Never upgrade a critical infrastructure package (Datasync, EF Core,
            SQL Server client libraries) without integration + security review (§13).
T-RULE 011  Never assume a library API exists because an older version's docs
            said so — verify against the exact pinned version before coding.
T-RULE 012  Do not hard-code a single TLS protocol version — use
            `SslProtocols.None` and let the OS/Schannel negotiate (§8.2).
T-RULE 013  Do not adopt EF Core compiled models until compatibility with named
            tenant filters is proven on the current EF Core 10 line (§5.4).
T-RULE 014  Do not publish an EF-Core-using BackgroundService as Native AOT (§5.4).
T-RULE 015  Any vulnerability suppression (`NuGetAuditSuppress`) must be dated,
            justified, and owned — never silent, never permanent for native
            database/crypto packages (§11).
```

---

## 16. Files & Storage Technology

> **The file lifecycle, reconciliation policy, and access model (Valet Key
> pattern) are architecture decisions in `ARCHITECTURE.md` §14 — this
> section states only the storage technology and delivery/packaging
> technology facts.**

- **Storage technology:** SQL metadata row (ordinary relational columns) +
  **binary content on the server filesystem** — a plain filesystem path
  layout, not a blob-storage service (no object-storage SDK at launch). Do
  not store every image/PDF inside SQL rows (row-size/backup-size concern).
- **Zero-Internet packaging consequence:** all JS/CSS/fonts/icons/images
  and Blazor framework assets are packaged with the app; **no `cdn…` /
  `fonts.googleapis…`** reference exists anywhere in the built output —
  verified by the isolation tests (`ARCHITECTURE.md` §17.4).
- **Local CA / certificate tooling** — see §8; the *trust-provisioning
  process* (installer/MDM) is `ARCHITECTURE.md` §10.5.
- **Desktop installer technology — a delivery preference, not an
  architectural decision.** **[decision]**
  ```
  MSIX     = preferred WHEN constraints permit
  WiX/MSI  = supported fallback
  ```
  Some peripherals, elevated installers, services, drivers, shell
  integration or enterprise policy can constrain MSIX — "preferred" is a
  per-site delivery choice.
- **macOS distribution requirement (Apple's own, not an Animora
  invention).** **[decision]** Whatever the container (DMG/PKG), every
  macOS build must be **signed · notarized · stapled · offline-installable
  · rollback-capable**; updates verify against the **same trust chain**.
- **Native binary inventory (supply chain).** **[decision]** A NuGet SBOM
  alone is insufficient. The release manifest must also record every
  **native** artifact — `DLL · DYLIB · SQLite · SQLCipher/e_sqlite3mc ·
  OS-specific native deps` — with its version and RID (ties into the §3.3
  native/RID matrix and §13).

---

## 17. Explicit Non-Goals & Rejected Technologies

**Non-goals (technology-level):**
- **No SQLite/EF Core running in a browser context** — a documented
  technology limitation (§3.2, D3), not a policy choice we could reverse
  by trying harder.
- **No external message broker / Redis package at launch.**
- **No per-tenant database technology (separate SQL Server instance per
  tenant) at launch.**
- **No cloud-hosted database, identity provider, or storage service** is
  referenced by any project in the solution.
- **No payment gateway / billing-processor SDK in this solution.** Payment
  collection, tax and invoicing belong to the vendor-side public website
  (`ARCHITECTURE.md` §3, §13.4, §17), which is a **separate system** outside
  this solution; the clinic-serving server only receives an async, idempotent
  *onboarding/provisioning intent* and records `BillingPeriod` amounts for
  reference — it never charges a card. This keeps the zero-Internet runtime
  rule intact.
- This file defines the **technology stack**, not schemas/endpoints/feature
  logic/behavior.

**Rejected technologies for launch:**
```
MAUI Blazor Hybrid as production desktop baseline  → Datasync flags Hybrid offline
Blazor WASM as an offline desktop/web technology    → EF Core/SQLite don't run in browser
Kafka / RabbitMQ / Azure Service Bus                → unnecessary at launch; no ADR yet
Redis                                               → only when multi-instance scale-out
Cloud-only authentication provider / CDN-dependent UI → breaks zero-Internet runtime
Cloud-only telemetry SDK/endpoint                    → breaks zero-Internet runtime
EF Core 11 at launch                                 → stay on EF Core 10 LTS
"TLS 1.3 only" hard-coded policy                     → OS-negotiated protocol instead (§8.2)
Cloud-only license validation service                → breaks zero-Internet runtime
Native AOT for any EF-Core-using worker              → EF Core + AOT is experimental (§5.4)
Microsoft Sync Framework 1.0 / 2.1                  → end-of-support historical products, not a new sync dependency
```

> Whether a rejected technology can ever be reconsidered (e.g. Redis, a
> broker, MAUI) is governed by the architecture's explicit ADR requirement
> (`ARCHITECTURE.md` §16.2/§20/§22) — this file states only that no such
> package is part of the launch dependency graph.

---

## 18. Consolidated Technology Decision (at-a-glance)

| Concern | Technology for Animora |
|---|---|
| Runtime | **.NET 10 (LTS)**, C# 14, end-to-end |
| Desktop framework | **Avalonia 12 Native** (Windows + macOS, one UI codebase) |
| Desktop UI pattern library | **`CommunityToolkit.Mvvm`** |
| Web framework | **Blazor Web App** |
| Web rendering technology | **Static SSR + selective `InteractiveServer` islands** (both capabilities of the same framework) |
| Local store (desktop) | **SQLite, encrypted at rest** (SQLite3 Multiple Ciphers / SQLCipher) |
| ORM | **EF Core 10** (not EF Core 11) |
| Offline sync engine | **Custom CT-backed engine** behind `IStateSyncEngine`; optional Datasync `10.1.2` adapter |
| Sync wire protocol | **Project-owned immutable critical batches**; OData only in qualified optional Datasync adapter |
| Backend host | **ASP.NET Core 10 + Kestrel** |
| Server DB | **SQL Server 2025 Standard** production baseline; Express bounded dev/pilot |
| Concurrency primitive | **SQL Server `rowversion`** |
| Row-security hardening tech | **SQL Server RLS** (gated by §12) |
| Identity provider | **Local ASP.NET Core Identity**; no cloud IdP; passkeys optional/post-launch |
| Background worker host | **`BackgroundService`** (JIT); Native AOT optional, never for EF-Core workers |
| Notifications / broker / backplane | **Self-hosted SignalR hints** at launch; no broker/Redis/backplane |
| Web offline technology | **Not available** (documented WASM/Hybrid limitation) |
| Files | **Filesystem + SQL metadata** (no object-storage SDK at launch) |
| Server reference hardware | **4 vCPU / 16 GB / SSD-NVMe**, architecture §69 reference only; measured capacity required |
| Multi-instance web (future) | Adds **Redis SignalR backplane package** (deploy switch, not new architecture) |

---

## 19. Authoritative One-Line Stack

> **C# 14 + .NET 10 LTS + Avalonia 12 Native Desktop (CommunityToolkit.Mvvm) +
> encrypted SQLite + EF Core 10 + custom CT-backed critical engine behind
> Application-owned `IStateSyncEngine` (Datasync 10.1.2 optional SYNC-SIMPLE) +
> ASP.NET Core 10 / Kestrel / in-process SignalR hints + Blazor Web App
> (Static SSR + selective InteractiveServer) + SQL Server 2025 Standard
> (Change Tracking + SNAPSHOT + rowversion + Resource Governor, RLS gated) +
> local ASP.NET Core Identity + zero mandatory external-Internet dependency.**
>
> For how this technology set is *organized and made to behave correctly*
> (bounded contexts, business-command pipeline, tenancy isolation layering,
> security planes, reliability patterns, deployment/recovery model), see
> `ARCHITECTURE.md`.
