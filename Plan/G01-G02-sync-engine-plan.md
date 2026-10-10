# PLAN — Phase G01 + G02 (Sync engine abstractions + Immutable-batch HTTP adapter)

> **خواننده: مدل هوش مصنوعی.** این فایل خودبسنده (self-contained) است. تمام امضاها، مسیرها، خطوط لنگر (anchor)
> و تصمیم‌های از-پیش-تأییدشده اینجاست. **ریپو را دوباره برای «درک» باز نکن.** فقط وقتی یک خطای کامپایل ثابت کرد
> اسنیپت با درخت فرق دارد، کوچک‌ترین اصلاح را بکن و در `PROGRESS.md` (Decision & deviation log) ثبت کن.
>
> **قانون قفل‌شدهٔ پوش:** بعد از تکمیل *هر تسک* (`[x]`) حتماً اجرا کن:
> `scripts/git/push.sh -b genspark_ai_developer "<پیام کانونشنال>"`
> اگر exit code = 3 بود، یک‌بار ابزار `setup_github_environment` را صدا بزن و دوباره اجرا کن.
> نپوش‌کردن = شکست. (تسک‌های «پوش» پایین صریحاً لیست شده‌اند.)
>
> **محیط (probe نکن):** build/test/run فقط از طریق اسکریپت‌های سندباکس (سقف حافظه ~985MiB؛ هرگز `dotnet build` خام):
> `scripts/sandbox/build.sh [fast|full]` (انتظار 0 warning/0 error)، `scripts/sandbox/test.sh [filter]`،
> `scripts/sandbox/run.sh` (دسکتاپ → `SMOKE RUN OK`). یک‌بار `scripts/sandbox/install-prereqs.sh` (idempotent).
> **تست/بیلد طولانی و full-test بی‌مورد ممنوع** — فقط فیلتر مربوطه (`Sync`, `Layering`) و در گام‌های مقرر.

---

## 0) وضعیت پایه (verified 2026-10-09/10 روی درخت فعلی) — دانش خالص، بدون نیاز به بررسی مجدد

این‌ها را خودم بررسی و تأیید کردم؛ در پیاده‌سازی به‌عنوان حقیقت استفاده کن:

- **branch شروع کار:** `main` است. اولین پوش با `-b genspark_ai_developer` خودش برنچ را می‌سازد/سوییچ می‌کند.
- **`Animora.Sync.Abstractions` امروز:** فقط `.csproj` (۲ ProjectReference: Domain + Contracts.Wire) و یک `README.md` placeholder. **هیچ `.cs` ندارد.**
- **`tests/Animora.Sync.Tests` امروز:** csproj (xunit + Test.Sdk + ۱ ProjectReference به Sync.Abstractions) + `PlaceholderTests.cs`.
- **`Directory.Packages.props`:** `Microsoft.Extensions.DependencyInjection` = **10.0.12** موجود. `Microsoft.Extensions.Options` = **10.0.12** موجود.
  **`Microsoft.Extensions.Http` موجود نیست** → G02 باید یک `<PackageVersion Include="Microsoft.Extensions.Http" Version="10.0.12" />` به `Directory.Packages.props` اضافه کند (نسخه هم‌خانوادهٔ 10.0.12).
- **`src/Animora.Domain/Results/`** دارد: `ErrorCode.cs`, `Result.cs`, `ErrorCodes.cs`.
  - `ErrorCode` (readonly record struct): categoryهای ثابت `Validation, NotFound, Conflict, Forbidden, Integrity, Infrastructure, Entitlement`؛ `ErrorCode.None`؛ `bool IsNone`.
  - `Result` / `Result<T>`: `Ok()`, `Fail(ErrorCode, string? detail=null)`, `IsSuccess/IsFailure/Error/Detail`, `Result<T>` دارای `Value`, `implicit operator Result<T>(T)`.
  - `ErrorCodes.cs` — در `#region Cross-cutting`: `ValidationFailed` (خط ~14)، `ConcurrencyConflict` (خط ~25).
    در `#region Sync feed (S12)` (خط ~690): **`ResyncRequired = new("RESYNC_REQUIRED", ErrorCode.Conflict)`** (خط 693) و **`BatchNotFound = new("BATCH_NOT_FOUND", ErrorCode.NotFound)`** (خط 696) — **از قبل وجود دارند** (S12 ساخته). در G02 **reuse** می‌شوند، هرگز redeclare نشوند.
  - **`Unauthorized` در ErrorCodes وجود ندارد** (grep خالی). → G02 باید `Unauthorized = new("UNAUTHORIZED", ErrorCode.Forbidden)` را در region جدید «Sync transport» اضافه کند.
- **`IClock`** در `src/Animora.Domain/Time/IClock.cs`: `public interface IClock { DateTimeOffset UtcNow { get; } }`.
- **الگوی مرجع offline fallback** — `src/Animora.Application/Commands/NullCommandTransport.cs`:
  `public sealed class NullCommandTransport : ICommandTransport` که `Task.FromResult(Result<...>.Ok(...Deferred...))` با Detail «No command transport configured (offline).» برمی‌گرداند؛ ثبت با `TryAddSingleton`. (NullSyncEngine باید همین الگو را آینه کند.)
- **`SyncCursor`** — `src/Animora.Persistence.Sqlite/Entities/SyncCursor.cs`: `const string DefaultScope = "default"`. (G01 به Persistence رفرنس نمی‌دهد؛ برابری با literal `"default"` در تست چک می‌شود.)
- **`ISyncStatusService`** — `src/Animora.Contracts/Sync/ISyncStatusService.cs`:
  خطوط ۳۵–۳۶ در XML-doc «Backend-ready seam» شامل `<c>Animora.Sync.Abstractions.ISyncEngine</c>` است → G01 فقط همین کامنت را به `IStateSyncEngine` اصلاح می‌کند (Contracts هیچ رفرنسی نمی‌گیرد).
- **`App.axaml.cs`** — `src/Animora.Desktop/App.axaml.cs`:
  خط ۳۹۹: `services.AddSingleton<LocalSyncStatusService>();` و خط ۴۰۰: `services.AddSingleton<ISyncStatusService>(...)`. کامنت خط ۳۹۸.
  G01: درست **بالای** خط ۳۹۹ یک خط `services.AddAnimoraSyncEngine();` + `using Animora.Sync.Abstractions;`.
  G02: درست **زیر** `services.AddAnimoraSyncEngine();` یک خط `services.AddAnimoraHttpSyncEngine();` + `using Animora.Sync.Custom;`.
- **csproj رفرنس‌ها:**
  - `Animora.Client.Services.csproj`: ۴ ProjectReference (Application, Contracts, Persistence.Sqlite, Infrastructure.Local). G01 یک ProjectReference به Sync.Abstractions اضافه می‌کند.
  - `Animora.Desktop.csproj`: ProjectReferenceها از خط ۱۱۷ (Contracts) و ۱۱۸ (Client.Services). G02 یک ProjectReference «Desktop → Sync.Custom» اضافه می‌کند.
- **`Animora.sln`**: خط ۱۴ ورودی `Animora.Sync.Abstractions` (GUID `754FD019-F0BC-4825-BF62-9CC46076F283`)، خط ۳۴ `Animora.Sync.Tests` (GUID `B7FFD015-9CAE-4310-8583-B9C2CFFBB2C8`). G02 یک ورودی برای `Animora.Sync.Custom` اضافه می‌کند (کنار Sync.Abstractions؛ GUID جدید).
- **تست لایه‌بندی** — `tests/Animora.Domain.Tests/LayeringRulesTests.cs`:
  - `ReferencedAssembliesOf = assembly.GetReferencedAssemblies()` → **فقط رفرنس‌های مستقیم** (metadata کامپایلر)، نه transitive.
  - `Desktop_HasNoStoreOrNetworkReferences` (خط ۱۳۲): روی اسمبلی `Animora.Desktop.App` چک می‌کند prefixهای `Microsoft.EntityFrameworkCore`, `System.Data.Common`, `System.Net.Http`, `CommunityToolkit.Datasync` **مستقیم** رفرنس نشوند.
    → **نتیجهٔ کلیدی برای G02:** چون Desktop فقط به `Sync.Custom` ProjectReference می‌دهد و خودِ کد Desktop هیچ تایپ `HttpClient`/EF لمس نمی‌کند، `System.Net.Http` به‌صورت مستقیم در Desktop ظاهر نمی‌شود و تست سبز می‌ماند. مطمئن شو در `App.axaml.cs` فقط `AddAnimoraHttpSyncEngine()` صدا زده شود (هیچ تایپ Http).
  - `Contracts_HaveExactlyOneRealRegistration` (خط ۱۱۴): impl باید در اسمبلی `Animora.Client.Services` باشد — دست نزن.

### 0.1) ⚠️ کشفِ بحرانیِ G02 — تناقض سند با سرور واقعی (حتماً بخوان؛ باعث شکست تست‌های integrity می‌شود)

سند G02 در §Design decisions می‌گوید «PageHash را روی **بدنهٔ JSON دیکدشده** تأیید کن» و «`AutomaticDecompression=GZip` (HttpClient خودکار دیکمپرس)». **این با سرور واقعی اشتباه است.** کد سرور را خواندم:

- **`src/Animora.Server/Sync/SyncEndpoints.cs` → `Download`:** پاسخ صفحه را به‌صورت **بایت خام gzip** با `Content-Type: application/gzip` می‌فرستد (نه `Content-Encoding: gzip`). پس **HttpClient خودکار دیکمپرس نمی‌کند**؛ کلاینت باید خودش `GZipStream` روی بدنه بگذارد.
- **`ETag`** = `"<page.PageHash>"` و `page.PageHash` (از `FeedPageReader`) = **SHA-256 (hex lowercase) روی بایت‌های فایل `.gz` فشرده** (نه روی JSON). مرجع: `FeedPageWriter.cs` خط ۵۹ «PageHash cannot hash itself; the authoritative compressed-byte hash is metadata/ETag» و `HashFile` (خطوط ۹۳–۹۷) = `Convert.ToHexStringLower(SHA256.HashData(<gz file bytes>))`.
- داخل JSON فشرده‌شده، فیلد `SyncPage.PageHash` عمداً **`""` (خالی)** سریالایز شده (`FeedPageWriter.cs` خط ۶۰). پس نمی‌توان PageHash را از داخل JSON خواند.
- **`ContentHash`** (در `BatchReady`) = `Convert.ToHexStringLower(SHA256( concat( ASCII(هر PageHash hex-lowercase) ) ))` — یعنی هش SHA-256 روی به‌هم‌چسبیدنِ **بایت‌های ASCII** رشته‌های per-page PageHash. مرجع: `FeedPageWriter.cs` خطوط ۸۳ (`contentHash.AppendData(Encoding.ASCII.GetBytes(hash))`) و ۹۱.
- **`SyncSerializerV2.Options`** (سرور) = `new JsonSerializerOptions(JsonSerializerDefaults.Web) { DictionaryKeyPolicy = CamelCase }`. کلاینت باید با `JsonSerializerDefaults.Web` دیسریالایز کند تا با wire سازگار باشد.

**تصمیم عملی G02 (این را پیاده کن، و در PROGRESS deviation log ثبت کن):**
1. در `SyncFeedClient.GetPageAsync`: بدنهٔ خام بایت را بخوان (`ReadAsByteArrayAsync`). `rawGzipBytes` را نگه دار.
   - **page hash واقعی = `IPageIntegrity.ComputePageHash(rawGzipBytes)` = SHA-256 hex-lowercase روی همان بایت‌های gzip** — و باید با ETag (بدون کوتیشن) برابر شود.
   - سپس برای به‌دست آوردن `SyncRecord`ها: `rawGzipBytes` را با `GZipStream` دیکمپرس کن و به `SyncPage` (با `JsonSerializerDefaults.Web`) دیسریالایز کن.
   - از آنجا که فیلد `PageHash` داخل JSON خالی است، مقدار hashِ تأییدشده (= ETag/compressed-byte hash) را به بالادست پاس بده (مثلاً `SyncPage` را با `PageHash` = hash محاسبه‌شده بازسازی کن یا یک wrapper برگردان).
2. `IPageIntegrity.ComputePageHash(ReadOnlySpan<byte> gzipBytes)` = `Convert.ToHexStringLower(SHA256.HashData(gzipBytes))`.
3. `IPageIntegrity.ComputeContentHash(IReadOnlyList<string> pageHashes)` = `Convert.ToHexStringLower(SHA256.HashData( Encoding.ASCII.GetBytes(string.Concat(pageHashes)) ))`.
4. **`AutomaticDecompression` را روشن نکن** (چون `application/gzip` است نه `Content-Encoding`). دیکمپرس دستی.
5. تست `PageIntegrityTests.ContentHash_IsSha256OfConcatenatedPageHashes` دقیقاً همین concat-ASCII→SHA256→hex-lowercase را چک می‌کند — مطمئن شو helper با آن می‌خواند.

> اگر سند و واقعیت تناقض دارند، **واقعیتِ سرور ملاک است** (zero-exploration contract: «اگر کامپایل/تست ثابت کرد اسنیپت با درخت فرق دارد، کوچک‌ترین چیز را اصلاح کن و لاگ کن»). این deviation از قبل اینجا حل شده؛ لازم نیست دوباره کشف کنی.

### 0.2) امضاهای wire موجود (S12) که G02 reuse می‌کند — verbatim از درخت، **بازتعریف نکن**

namespace `Animora.Contracts.Wire.Sync` (در پروژهٔ `Animora.Contracts.Wire` که G02 رفرنس می‌دهد):
```csharp
public sealed record SyncManifest(int ContractVersion, long Epoch, long Generation, IReadOnlyList<string> ProjectionOrder, int PageTargetBytes);
public sealed record OpenBatchRequest(long Epoch, long FromSequence);
public sealed record BatchReady(string BatchId, long Epoch, long Generation, long FromSequenceExclusive, long Watermark, int PageCount, string ContentHash);
public sealed record UpToDate(long Watermark);
public sealed record ResyncRequired(string Reason, long Epoch);
public sealed record OpenBatchResponse(string Kind, BatchReady? Batch = null, UpToDate? Current = null, ResyncRequired? Resync = null);
public sealed record SyncPage(string BatchId, int PageOrdinal, int PageCount, IReadOnlyList<SyncRecord> Records, string PageHash);
public sealed record SyncRecord(string Entity, string EntityId, char Op, string? Version, DateTimeOffset? UpdatedAt, bool Deleted, JsonElement? Payload);
```
- discriminator `OpenBatchResponse.Kind` مقادیر: **`"BatchReady"` | `"UpToDate"` | `"ResyncRequired"`** (verbatim از `BatchFreezer.cs`: `new("ResyncRequired", Resync: new("BEYOND_RETENTION"...))`, `new("ResyncRequired", Resync: new("EPOCH_CHANGED"...))`, `Ready(...)` → `new("BatchReady", new(...))`).
- `ResyncRequired.Reason` مقادیر: `EPOCH_CHANGED | BEYOND_RETENTION | CONTRACT_UNSUPPORTED`.
- **`WireContract`** — `src/Animora.Contracts.Wire/WireContract.cs`: `public const int Version = 2;`، `public const string HeaderName = "X-Animora-Contract-Version";`، `public const string SupportedVersionsHeaderName = "X-Animora-Supported-Versions";`.
- اندپوینت‌ها (consume، نه introduce): `GET /sync/manifest`، `POST /sync/batches` (body `OpenBatchRequest`)، `GET /sync/batches/{batchId}/pages/{ordinal:int}` (ETag=PageHash، honours `If-None-Match`→304)، `POST /sync/snapshots` (G02 صدا نمی‌زند).
- نگاشت status سرور (از `SyncEndpoints.cs`): نسخهٔ قرارداد ناسازگار → **426** با بدنه `ResyncRequired("CONTRACT_UNSUPPORTED", 0)`؛ batch غایب/خارجی → **404**؛ عمر snapshot تمام → **503** با هدر `Retry-After: 1`.

---

## 1) G01 — Sync engine abstractions (`IStateSyncEngine`, `SyncScope`, `SyncOutcome`, `SyncResult`, `NullSyncEngine`, DI)

**Satisfies:** R-B03, R-B13 · **Depends on:** S12 (done) · **No migration, no new wire DTO, no new ErrorCode.**
**لایه‌بندی:** `Animora.Sync.Abstractions` فقط `Animora.Domain` + `Animora.Contracts.Wire` (+ پکیج pinned `Microsoft.Extensions.DependencyInjection` 10.0.12) — هیچ Npgsql/EF/`System.Net.Http`/server/Application/Persistence.
**استایل C# (§4):** `sealed` پیش‌فرض؛ `sealed record` برای DTO؛ nullable محترم؛ هر متد async عمومی `CancellationToken ct = default`؛ `ConfigureAwait(false)` در کد library؛ خطاهای مورد-انتظار = `Result`؛ زمان از `IClock`؛ هر عضو عمومی XML-doc تک‌خطی.

### مشخصات دقیق تایپ‌ها (fixed — عیناً پیاده کن)

- **`SyncScope.cs`** — `public readonly record struct SyncScope`:
  - backing field خصوصی `string? _name`؛ `public string Name => _name ?? "default";` (پس `default(SyncScope)` معتبر و برابر Default).
  - `public static readonly SyncScope Default = new("default");` — سازندهٔ خصوصی/داخلی که `_name` را ست می‌کند.
  - `public static Result<SyncScope> Create(string name)`: `name` را trim کن؛ رد کن null/whitespace، طول > 64، یا هر کاراکتر خارج از `[a-z0-9._-]` با
    `Result.Fail(ErrorCodes.ValidationFailed, "Sync scope must be 1–64 chars of a-z 0-9 . _ -.")`؛ در موفقیت `Result<SyncScope>.Ok(new SyncScope(trimmed))`.
  - `public override string ToString() => Name;`.
  - **نکته:** Default باید **برابر literal `"default"`** باشد (= `SyncCursor.DefaultScope` ولی بدون رفرنس به Persistence؛ تست با literal چک می‌کند).
- **`SyncOutcome.cs`** — `public enum SyncOutcome { UpToDate = 0, Applied = 1, Deferred = 2, ResyncRequired = 3 }` (intها صریح).
  - معنا: `Deferred` = موتور پیکربندی‌نشده/آفلاین (پاسخ NullSyncEngine، **موفقیت**)؛ `ResyncRequired` = سرور گفته checkpoint کلاینت غیرقابل‌استفاده است. خطاهای واقعی (شبکه/integrity/cancel) → `Result.Fail`، نه outcome.
- **`SyncResult.cs`** — `public sealed record SyncResult(SyncOutcome Outcome, long? Watermark, int BatchesApplied, int RecordsApplied, DateTimeOffset CompletedAtUtc, string? Detail)` + چهار factory (بدنهٔ سازنده throw نمی‌کند):
  - `public static SyncResult Deferred(DateTimeOffset nowUtc, string? detail = null) => new(SyncOutcome.Deferred, null, 0, 0, nowUtc, detail);`
  - `public static SyncResult UpToDate(long watermark, DateTimeOffset nowUtc) => new(SyncOutcome.UpToDate, watermark, 0, 0, nowUtc, null);`
  - `public static SyncResult Applied(long watermark, int batches, int records, DateTimeOffset nowUtc) => new(SyncOutcome.Applied, watermark, batches, records, nowUtc, null);`
  - `public static SyncResult ResyncRequired(string reason, DateTimeOffset nowUtc) => new(SyncOutcome.ResyncRequired, null, 0, 0, nowUtc, reason);`
- **`IStateSyncEngine.cs`**:
  ```csharp
  public interface IStateSyncEngine
  {
      /// <summary>False for NullSyncEngine (no endpoint configured); true for a real adapter.</summary>
      bool IsConfigured { get; }
      /// <summary>Pull-only (G01–G04): download immutable batches for scope and apply atomically. Never touches pending overlay.</summary>
      Task<Result<SyncResult>> PullAsync(SyncScope scope, CancellationToken ct = default);
  }
  ```
  **بدون `PushAsync`** (قصد تجاری فقط از `ICommandTransport` در G05). هیچ تایپ server/HttpClient/EF در interface.
- **`NullSyncEngine.cs`** — `public sealed class NullSyncEngine(IClock clock) : IStateSyncEngine`:
  - `public bool IsConfigured => false;`
  - `PullAsync`: `ct.ThrowIfCancellationRequested();` سپس `return Task.FromResult(Result<SyncResult>.Ok(SyncResult.Deferred(clock.UtcNow, "No sync engine configured (offline).")));` (هر scope پذیرفته).
- **`SyncAbstractionsServiceCollectionExtensions.cs`** — `public static class`:
  - `public static IServiceCollection AddAnimoraSyncEngine(this IServiceCollection services)`:
    `ArgumentNullException.ThrowIfNull(services);` → `services.TryAddSingleton<IStateSyncEngine, NullSyncEngine>();` → `return services;`
  - (`TryAdd` تا adapter واقعی بعداً با `AddSingleton`/`Replace` برنده شود.) نیاز به `using Microsoft.Extensions.DependencyInjection.Extensions;` برای `TryAddSingleton`.

### ویرایش‌های جانبی G01
- `Animora.Sync.Abstractions.csproj`: اضافه `<ItemGroup><PackageReference Include="Microsoft.Extensions.DependencyInjection" /></ItemGroup>` (نسخه از props). ۲ ProjectReference دست‌نخورده → در نهایت `grep -c ProjectReference` = **2**.
- `README.md` (Sync.Abstractions): جایگزین placeholder با ۶ خط: هدف، ۳ تایپ اصلی (`IStateSyncEngine`/`SyncScope`/`SyncResult`)، «no transport/database refs — see G02».
- `Animora.Client.Services.csproj`: اضافه `<ProjectReference Include="..\Animora.Sync.Abstractions\Animora.Sync.Abstractions.csproj" />`.
- `App.axaml.cs`: `using Animora.Sync.Abstractions;` + خط `services.AddAnimoraSyncEngine();` دقیقاً **بالای** خط ۳۹۹ (`AddSingleton<LocalSyncStatusService>`). هیچ خط دیگری تغییر نکند.
- `ISyncStatusService.cs`: در کامنت، `Animora.Sync.Abstractions.ISyncEngine` → `Animora.Sync.Abstractions.IStateSyncEngine` (فقط کامنت).

### تست‌های G01 — در `tests/Animora.Sync.Tests/` (حذف `PlaceholderTests.cs`)
نام‌های دقیق (Acceptance آن‌ها را چک می‌کند):
- `SyncScopeTests.Default_EqualsLiteralDefault_MatchingSyncCursorDefaultScope` → `SyncScope.Default.Name == "default"`.
- `SyncScopeTests.DefaultStruct_IsDefaultScope_NeverNull` → `default(SyncScope).Name == "default"` و null نیست.
- `SyncScopeTests.Create_RejectsEmptyTooLongAndIllegalChars` → خالی/۶۵کاراکتر/`"BAD!"` → `IsFailure` با `ErrorCodes.ValidationFailed`؛ یک معتبر (مثل `"pets"`) → `IsSuccess`.
- `SyncResultTests.Factories_SetOutcomeAndWatermark` → `UpToDate`/`Applied` watermark و counts درست.
- `SyncResultTests.Deferred_HasNoWatermark` → `Deferred(...).Watermark is null` و `Outcome == Deferred`.
- `NullSyncEngineTests.Pull_ReturnsDeferredSuccess_NotConfigured` → `IsConfigured false`، `PullAsync` → `Ok` با `Outcome == Deferred`.
- `NullSyncEngineTests.Pull_PreCancelledToken_Throws` → با `new CancellationToken(true)` → `OperationCanceledException`.
- `NullSyncEngineTests.Pull_UsesInjectedClock` → `clock.UtcNow` ثابتِ fake در `CompletedAtUtc`.
- `SyncDiTests.AddAnimoraSyncEngine_RegistersNullEngine_AndRealRegistrationWins` → بعد از `AddAnimoraSyncEngine()` resolve = `NullSyncEngine`؛ بعد از `AddSingleton<IStateSyncEngine, FakeEngine>()` resolve = Fake (TryAdd برنده نمی‌شود). نیاز به یک `FakeEngine`/`FakeClock` کوچک داخل تست.
- `SyncLayeringTests.SyncAbstractions_ReferencesOnlyDomainWireAndFramework` → `typeof(IStateSyncEngine).Assembly.GetReferencedAssemblies()` نباید prefix `Npgsql`/`Microsoft.EntityFrameworkCore`/`System.Net.Http`/`Animora.Persistence`/`Animora.Application`/`Animora.Server` داشته باشد؛ باید `Animora.Domain` و `Animora.Contracts.Wire` داشته باشد.
- csproj تست: ChangeNeeded نیست (ProjectReference به Sync.Abstractions از قبل هست).

### Acceptance G01 (مکانیکی)
1. `scripts/sandbox/build.sh full` = 0/0؛ `scripts/sandbox/run.sh` = `SMOKE RUN OK`.
2. `scripts/sandbox/test.sh Sync` سبز با ۱۰ تست بالا.
3. `grep -rn "Npgsql\|EntityFramework\|System.Net.Http\|Datasync" src/Animora.Sync.Abstractions` خالی؛ `grep -c "ProjectReference" src/Animora.Sync.Abstractions/Animora.Sync.Abstractions.csproj` = 2.
4. `grep -rn "ISyncEngine" src tests` خالی (یعنی هیچ `ISyncEngine` نماند — فقط `IStateSyncEngine`)؛ `grep -rn "IStateSyncEngine" src --include=*.cs -l` دقیقاً: `IStateSyncEngine.cs`, `NullSyncEngine.cs`, `SyncAbstractionsServiceCollectionExtensions.cs`, `ISyncStatusService.cs`.
   - **توجه:** مطمئن شو کامنت `ISyncStatusService` دیگر `ISyncEngine` ندارد وگرنه بند ۴ شکست می‌خورد.
5. `scripts/sandbox/test.sh Layering` سبز (`Desktop_HasNoStoreOrNetworkReferences`, `Contracts_HaveExactlyOneRealRegistration`).

### PROGRESS.md (پایان G01)
ردیف `G01` در «Phase status» = `✅ Done` (+ تاریخ + خلاصهٔ یک‌خطی)؛ «Next phase» → `G02`؛ یک `### G01` در «Repo state log» (۶ تایپ، call site DI، کلاس‌های تست)؛ هر deviation در «Decision & deviation log».

---

## 2) G02 — Custom immutable-batch HTTP adapter (`Animora.Sync.Custom`)

**Satisfies:** R-B03, R-B13, R-B31 · **Depends on:** G01 · **No migration, no new wire DTO** (reuse S12 `Animora.Contracts.Wire.Sync`).
**لایه‌بندی:** `Animora.Sync.Custom` فقط ۴ ProjectReference: `Animora.Sync.Abstractions`, `Animora.Application`, `Animora.Persistence.Sqlite`, `Animora.Contracts.Wire` + پکیج‌های `Microsoft.Extensions.Http` + `Microsoft.Extensions.DependencyInjection`. **هرگز** Npgsql / `CommunityToolkit.Datasync` / `Animora.Server*` / `Animora.Persistence.PostgreSql` / `Animora.Desktop`.
**استایل:** مثل G01 + `ConfigureAwait(false)` در همه‌جا؛ هیچ `.Result`/`.Wait()`؛ backoff jitter از `IClock` نه `Thread.Sleep`.

### تایپ‌های دقیق (fixed)

- **`ServerEndpointOptions.cs`** — `public sealed record ServerEndpointOptions`:
  `string? BaseAddress`, `TimeSpan RequestTimeout = TimeSpan.FromSeconds(30)`, `int MaxBatchPages = 4096`, `int MaxPageBytes = 262144`؛
  `public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseAddress);`؛ bind از کلید configuration `Animora:Server`.
- **`IPageIntegrity.cs`** — `public static class PageIntegrity` (یا interface + static؛ نام تست `PageIntegrityTests`):
  - `ComputePageHash(ReadOnlySpan<byte> gzipBytes) => Convert.ToHexStringLower(SHA256.HashData(gzipBytes));` (**روی بایت gzip خام**، §0.1).
  - `ComputeContentHash(IReadOnlyList<string> pageHashes) => Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(string.Concat(pageHashes))));`
- **`SyncFeedClient.cs`** — typed client روی `HttpClient` تزریق‌شده (named `"animora-sync"`):
  - ساختن client: `BaseAddress` از options؛ هدر `WireContract.HeaderName` = `WireContract.Version.ToString()`؛ `Accept: application/json`. (دیکمپرس خودکار روشن **نشود** — §0.1.)
  - `OpenBatchOutcome` یک `sealed record` union محلی: سه حالت `Ready(BatchReady Batch)`, `Current(long Watermark)`, `Resync(string Reason, long Epoch)` (مثلاً با یک discriminator enum یا سه فیلد nullable + factory).
  - متدها (هر کدام `Result<T>`، هرگز throw برای status مورد-انتظار):
    - `Task<Result<SyncManifest>> GetManifestAsync(CancellationToken ct)` → `GET /sync/manifest`.
    - `Task<Result<OpenBatchOutcome>> OpenBatchAsync(long epoch, long fromSequence, CancellationToken ct)` → `POST /sync/batches` با `OpenBatchRequest(epoch, fromSequence)`؛ بدنهٔ `OpenBatchResponse` را بر اساس `Kind` به outcome نگاشت کن.
    - `Task<Result<SyncPage?>> GetPageAsync(string batchId, int ordinal, string? ifNoneMatch, CancellationToken ct)` → `GET /sync/batches/{batchId}/pages/{ordinal}`؛ اگر `ifNoneMatch` داده شد هدر `If-None-Match` بگذار؛ `304` → `Result<SyncPage?>.Ok(null)`؛ در `200`: بایت خام بخوان، `ComputePageHash` را با ETag (بدون کوتیشن) مقایسه کن (نابرابری → `Fail(SyncIntegrityFailed)`)، دیکمپرس gzip، به `SyncPage` دیسریالایز (Web defaults)، و `SyncPage` را با `PageHash = computedHash` بازگردان.
  - **نگاشت HTTP→Result:** `401/403` → `Fail(ErrorCodes.Unauthorized)`؛ `404` → `Fail(ErrorCodes.BatchNotFound)`؛ `426` → در OpenBatch: `OpenBatchOutcome.Resync("CONTRACT_UNSUPPORTED", epoch)`؛ `429`/`503` با `Retry-After` → `Fail(ErrorCodes.ServerBusy, detail=<retry-after>)`؛ `HttpRequestException`/`TaskCanceledException`(غیرِ-کاربر) / timeout → `Fail(ErrorCodes.NetworkUnavailable)`؛ بدنهٔ بیش از `MaxPageBytes` یا page با ساختار نامعتبر → `Fail(ErrorCodes.SyncProtocolViolation)`.
- **`ISyncApplyStore.cs`** (seam بین download+verify اینجا و apply واقعی در G04):
  ```csharp
  public interface ISyncApplyStore
  {
      Task<Result<SyncCheckpoint>> ReadCheckpointAsync(SyncScope scope, CancellationToken ct = default);
      Task<Result<ISyncApplySession>> BeginAsync(SyncScope scope, BatchReady batch, CancellationToken ct = default);
  }
  public interface ISyncApplySession : IAsyncDisposable
  {
      Task<Result> AcceptPageAsync(SyncPage page, CancellationToken ct = default);
      Task<Result<SyncCheckpoint>> CommitAsync(CancellationToken ct = default);
      Task<Result> AbortAsync(CancellationToken ct = default);
  }
  public readonly record struct SyncCheckpoint(long Epoch, long Watermark);
  ```
- **`BufferingSyncApplyStore.cs`** — impl پیش‌فرض این فاز: `ReadCheckpointAsync` → `(0,0)` (یا آخرین commit درون‌حافظه‌ای)؛ `BeginAsync` → session که صفحات را فقط می‌شمارد/verify و رکوردها را count می‌کند (bounded، کل batch را نگه نمی‌دارد)؛ `CommitAsync` → `SyncCheckpoint(batch.Epoch, batch.Watermark)` و همان را درون‌حافظه نگه می‌دارد؛ `AbortAsync` → دور ریختن، checkpoint بدون تغییر. **هیچ نوشتن SQLite.** (G04 با `Replace`/`AddSingleton` جایگزین می‌کند.)
- **`HttpStateSyncEngine.cs`** — `public sealed class HttpStateSyncEngine : IStateSyncEngine` (تزریق: `SyncFeedClient`, `ISyncApplyStore`, `ServerEndpointOptions`, `IClock`):
  - `IsConfigured => _options.IsConfigured;`
  - `PullAsync(scope, ct)` الگوریتم (checkpoint-disciplined):
    1. اگر `!IsConfigured` → `Result.Ok(SyncResult.Deferred(clock.UtcNow, "No server endpoint configured (offline)."))` (دفاعی).
    2. `ReadCheckpointAsync` → `(epoch0, from0)`.
    3. `GetManifestAsync` (fail→return fail). اگر `manifest.Epoch != epoch0 && epoch0 != 0` → مثل resync رفتار کن (پایین).
    4. `OpenBatchAsync(manifest.Epoch, from0)`:
       - `Current(w)` → `SyncResult.UpToDate(w, now)`.
       - `Resync(reason,_)` → `SyncResult.ResyncRequired(reason, now)` (snapshot را G04/G03 صدا می‌زند، اینجا نه).
       - `Ready(batch)` → `BeginAsync(scope, batch)`؛ حلقهٔ `ordinal = 0..batch.PageCount-1`: `GetPageAsync` (304→از صفحهٔ staged استفاده کن؛ خطای transient تا ۳ بار با backoff jittered از `IClock` — بدون `Thread.Sleep`)، `AcceptPageAsync` هر صفحهٔ verifyشده، PageHashها را جمع کن؛ بعد از آخرین صفحه `ComputeContentHash(pageHashes) == batch.ContentHash` را چک کن (نابرابری → Abort + `Fail(SyncIntegrityFailed)`)؛ سپس `CommitAsync` → `SyncResult.Applied(checkpoint.Watermark, 1, recordsApplied, now)`.
    5. هر `Fail` → `AbortAsync` + برگرداندن همان `Result.Fail` (checkpoint دست‌نخورده، هرگز apply پاره‌پاره).
    6. `ct` در کل رعایت شود؛ cancel کاربر فقط اگر caller کنسل کند surface شود.
  - **بدون `PushAsync`، بدون اثر money/stock.**
- **`SyncCustomServiceCollectionExtensions.cs`** — `public static class`:
  - `public static IServiceCollection AddAnimoraHttpSyncEngine(this IServiceCollection services, Action<ServerEndpointOptions>? configure = null)`:
    `ArgumentNullException.ThrowIfNull(services);`؛ `ServerEndpointOptions` را bind/configure کن؛ named `HttpClient` `"animora-sync"` اضافه کن؛ `SyncFeedClient` را register کن؛ `services.TryAddSingleton<ISyncApplyStore, BufferingSyncApplyStore>();`؛ سپس **فقط اگر** `options.IsConfigured` بود → `services.AddSingleton<IStateSyncEngine, HttpStateSyncEngine>();` (در غیر این صورت NullSyncEngine از G01 می‌ماند). `return services;`
    - برای خواندن `options.IsConfigured` در زمان register: options را از `configure` یا پیش‌فرض بساز و ارزیابی کن (نه از DI container).

### ErrorCodes جدید (G02) — `src/Animora.Domain/Results/ErrorCodes.cs`
یک `#region Sync transport` جدید اضافه کن (هیچ کد موجود rename نشود). دقیقاً این ۴ (و `Unauthorized` چون غایب است، پس ۵ خط `new(`):
```csharp
public static readonly ErrorCode NetworkUnavailable   = new("NETWORK_UNAVAILABLE",     ErrorCode.Infrastructure);
public static readonly ErrorCode ServerBusy           = new("SERVER_BUSY",             ErrorCode.Infrastructure);
public static readonly ErrorCode SyncIntegrityFailed  = new("SYNC_INTEGRITY_FAILED",   ErrorCode.Integrity);
public static readonly ErrorCode SyncProtocolViolation= new("SYNC_PROTOCOL_VIOLATION", ErrorCode.Integrity);
public static readonly ErrorCode Unauthorized         = new("UNAUTHORIZED",            ErrorCode.Forbidden);
```
- `BatchNotFound` / `ResyncRequired` را **reuse** کن (در `#region Sync feed (S12)` موجودند؛ redeclare ممنوع).
- Acceptance بند ۴: `grep -c "NETWORK_UNAVAILABLE\|SERVER_BUSY\|SYNC_INTEGRITY_FAILED\|SYNC_PROTOCOL_VIOLATION" ErrorCodes.cs` = 4 (این ۴؛ `UNAUTHORIZED` جدا).

### ویرایش‌های جانبی G02
- `Directory.Packages.props`: اضافه `<PackageVersion Include="Microsoft.Extensions.Http" Version="10.0.12" />` (کنار بقیهٔ Extensions).
- `src/Animora.Sync.Custom/Animora.Sync.Custom.csproj` (new): SDK `Microsoft.NET.Sdk`؛ ۴ ProjectReference (بالا)؛ ۲ PackageReference (`Microsoft.Extensions.Http`, `Microsoft.Extensions.DependencyInjection`). → `grep -c ProjectReference` = **4**.
- `Animora.sln`: ورودی `Animora.Sync.Custom` کنار `Animora.Sync.Abstractions` (GUID جدید؛ هم Project(...) هم بخش‌های GlobalSection build config). **با `dotnet sln add` نه — چون memory-cap؛ دستی ویرایش کن یا اگر لازم شد با احتیاط.** (ترجیحاً دستی، الگوی خط ۱۴ را کپی کن.)
- `src/Animora.Desktop/App.axaml.cs`: `using Animora.Sync.Custom;` + خط `services.AddAnimoraHttpSyncEngine();` دقیقاً **زیر** `services.AddAnimoraSyncEngine();`.
- `src/Animora.Desktop/Animora.Desktop.csproj`: اضافه `<ProjectReference Include="..\Animora.Sync.Custom\Animora.Sync.Custom.csproj" />`.
- `tests/Animora.Sync.Tests/Animora.Sync.Tests.csproj`: اضافه ProjectReference به `Animora.Sync.Custom`.

### تست‌های G02 — `tests/Animora.Sync.Tests/`
روی یک `HttpMessageHandler` fake (برای status 200/304/404/426/429/503/network). نام‌های دقیق:
- `SyncFeedClientTests.Manifest_Returns_SyncManifest`
- `SyncFeedClientTests.Open_MapsUpToDate_Ready_Resync` (هر سه `Kind`)
- `SyncFeedClientTests.Page_304_ReturnsNullOk`
- `SyncFeedClientTests.Status404_MapsBatchNotFound`
- `SyncFeedClientTests.Status426_MapsResyncContractUnsupported`
- `SyncFeedClientTests.Status503_WithRetryAfter_MapsServerBusy`
- `SyncFeedClientTests.NetworkFault_MapsNetworkUnavailable`
- `PageIntegrityTests.ContentHash_IsSha256OfConcatenatedPageHashes` (concat ASCII → SHA256 → hex lowercase)
- `HttpStateSyncEnginePullTests.UpToDate_ReturnsUpToDate`
- `HttpStateSyncEnginePullTests.Ready_DownloadsAppliesAll_ReturnsApplied` (fake handler باید صفحات gzip واقعی بسازد تا ComputePageHash/ContentHash بخواند — helper بساز که `SyncPage` را به gzip سریالایز کند و hash gzip را ETag/ContentHash کند، مطابق §0.1)
- `HttpStateSyncEnginePullTests.Resume_304Page_ReusesStaged`
- `HttpStateSyncEnginePullTests.IntegrityMismatch_AbortsSession_NoCheckpointAdvance`
- `HttpStateSyncEnginePullTests.Resync_ReturnsResyncRequired`
- `HttpStateSyncEnginePullTests.Unconfigured_ReturnsDeferred`
- `SyncCustomDiTests.Configured_HttpEngineWins` (BaseAddress ست → resolve = `HttpStateSyncEngine`)
- `SyncCustomDiTests.Unconfigured_NullEngineStays` (BaseAddress خالی → بعد از `AddAnimoraSyncEngine()`+`AddAnimoraHttpSyncEngine()` resolve = `NullSyncEngine`)
- `SyncCustomLayeringTests` → `typeof(HttpStateSyncEngine).Assembly.GetReferencedAssemblies()` نباید `Npgsql`/`Animora.Server`/`Animora.Persistence.PostgreSql`/`CommunityToolkit.Datasync` داشته باشد.

### Acceptance G02 (مکانیکی)
1. `scripts/sandbox/build.sh full` = 0/0؛ `scripts/sandbox/run.sh` = `SMOKE RUN OK` (unconfigured → Null engine).
2. `scripts/sandbox/test.sh Sync` سبز با ۱۶ تست بالا.
3. `grep -rn "Npgsql\|CommunityToolkit.Datasync\|Animora.Server\|Animora.Persistence.PostgreSql" src/Animora.Sync.Custom` خالی؛ `grep -c "ProjectReference" src/Animora.Sync.Custom/Animora.Sync.Custom.csproj` = 4.
4. `grep -c "NETWORK_UNAVAILABLE\|SERVER_BUSY\|SYNC_INTEGRITY_FAILED\|SYNC_PROTOCOL_VIOLATION" src/Animora.Domain/Results/ErrorCodes.cs` = 4؛ `grep -rn "RESYNC_REQUIRED\|BATCH_NOT_FOUND" src/Animora.Sync.Custom` فقط **رفرنس** (بدون `new(`).
5. `scripts/sandbox/test.sh Layering` سبز (`Desktop_HasNoStoreOrNetworkReferences`)؛ `grep -rn "PushAsync" src/Animora.Sync.Custom` خالی.

### PROGRESS.md (پایان G02)
ردیف `G02` = `✅ Done` (+ تاریخ + خلاصه)؛ «Next phase» → `G03`؛ یک `### G02` در «Repo state log» (پروژهٔ Sync.Custom + تایپ‌های عمومی + call site DI + ۴ ErrorCode جدید + کلاس‌های تست)؛ هر deviation (خصوصاً §0.1) در «Decision & deviation log».

---

## 3) Out of scope هر دو فاز (پیاده نکن)
- apply واقعی روی SQLite + checkpoint table + overlay/shadow + conflict policy → **G04** (جایگزین `BufferingSyncApplyStore`؛ `POST /sync/snapshots` آنجا).
- scheduler / jitter policy / `SyncCursor` integration / `ISyncStatusService` wiring → **G03**.
- bearer token روی HttpClient → **G06**. command transport → **G05**. خودِ export سرور → **S12** (reuse).
- optional Datasync `/tables/*` adapter → معوق (ROADMAP §5).

---

## 4) تسک لیست اجرا (قانون پوش قفل‌شده: بعد از هر `[x]` بلافاصله `scripts/git/push.sh -b genspark_ai_developer "<msg>"`)

> هر تسک را که تمام کردی، همین‌جا `[ ]` را به `[x]` تغییر بده، سپس پوش کن. اگر فیکس سنگین/کد زیاد وسط تسک نوشتی، همان‌جا هم یک پوش میانی با پیام واضح بزن.

### G01
- [x] **T0 — ساخت فایل پلن.** این فایل در `Plan/` ساخته شد. → پوش: `docs(plan): add G01+G02 sync engine implementation plan`
- [x] **T1 — تایپ‌های G01.** ساخت ۶ فایل در `src/Animora.Sync.Abstractions/`: `SyncScope.cs`, `SyncOutcome.cs`, `SyncResult.cs`, `IStateSyncEngine.cs`, `NullSyncEngine.cs`, `SyncAbstractionsServiceCollectionExtensions.cs` (طبق §1). + ویرایش `.csproj` (PackageReference DI) و `README.md`. → پوش: `feat(sync): G01 sync engine abstractions + NullSyncEngine`
- [x] **T2 — wiring G01.** `Animora.Client.Services.csproj` (ProjectReference)، `App.axaml.cs` (using + AddAnimoraSyncEngine بالای خط ۳۹۹)، `ISyncStatusService.cs` (کامنت ISyncEngine→IStateSyncEngine). → پوش: `feat(sync): wire G01 engine registration + fix ISyncStatusService doc`
- [x] **T3 — تست‌های G01.** حذف `PlaceholderTests.cs`؛ ساخت ۵ کلاس تست (۱۰ تست §1). → پوش: `test(sync): G01 engine abstractions tests`
- [x] **T4 — verify G01.** `build.sh full` (0/0)، `test.sh Sync` (سبز)، `test.sh Layering` (سبز)، `run.sh` (`SMOKE RUN OK`)، و grepهای Acceptance G01 بند ۳/۴. اصلاح هر خطا تا سبز شدن. → پوش: `chore(sync): G01 build/test green` (یا پیام فیکس اگر لازم شد)
- [ ] **T5 — PROGRESS G01.** به‌روزرسانی `backend-roadmap/PROGRESS.md` طبق §1. → پوش: `docs(progress): mark G01 done, next G02`

### G02
- [ ] **T6 — scaffold G02 project.** ساخت `src/Animora.Sync.Custom/Animora.Sync.Custom.csproj` (۴ ProjectRef + ۲ package)؛ افزودن `Microsoft.Extensions.Http` به `Directory.Packages.props`؛ افزودن پروژه به `Animora.sln` (ویرایش دستی، الگوی خط ۱۴). → پوش: `feat(sync): G02 scaffold Animora.Sync.Custom project`
- [ ] **T7 — تایپ‌های G02.** `ServerEndpointOptions.cs`, `IPageIntegrity.cs` (hash روی gzip خام §0.1), `SyncFeedClient.cs` (+ `OpenBatchOutcome`), `ISyncApplyStore.cs` (+session+checkpoint), `BufferingSyncApplyStore.cs`, `HttpStateSyncEngine.cs`, `SyncCustomServiceCollectionExtensions.cs` (طبق §2). → پوش: `feat(sync): G02 immutable-batch HTTP adapter core types`
- [ ] **T8 — ErrorCodes G02.** افزودن `#region Sync transport` با ۴+۱ کد (طبق §2). → پوش: `feat(domain): add Sync transport error codes for G02`
- [ ] **T9 — wiring G02.** `App.axaml.cs` (using + AddAnimoraHttpSyncEngine زیر AddAnimoraSyncEngine)، `Animora.Desktop.csproj` (ProjectReference به Sync.Custom)، `tests/Animora.Sync.Tests.csproj` (ProjectReference). → پوش: `feat(sync): wire G02 HTTP engine registration`
- [ ] **T10 — تست‌های G02.** ۱۶ تست §2 با HttpMessageHandler fake و gzip-page helper (§0.1). → پوش: `test(sync): G02 HTTP adapter tests`
- [ ] **T11 — verify G02.** `build.sh full` (0/0)، `test.sh Sync` (سبز)، `test.sh Layering` (سبز)، `run.sh` (`SMOKE RUN OK`)، grepهای Acceptance G02 بند ۳/۴/۵. اصلاح تا سبز. → پوش: `chore(sync): G02 build/test green`
- [ ] **T12 — PROGRESS G02.** به‌روزرسانی `backend-roadmap/PROGRESS.md` طبق §2 (+ ثبت deviation §0.1). → پوش: `docs(progress): mark G02 done, next G03`

### پایان
- [ ] **T13 — حذف این فایل پلن.** فقط `Plan/G01-G02-sync-engine-plan.md` حذف شود (به rodmap و فایل‌های دیگر دست نزن). → پوش: `chore(plan): remove completed G01+G02 plan`

---

## 5) یادآوری‌های کلیدی برای مدلِ اجراکننده
- فقط اسکریپت‌های سندباکس برای build/test/run؛ فیلتر `Sync`/`Layering`؛ full-test بی‌مورد نزن.
- هر عضو عمومی XML-doc تک‌خطی؛ `sealed`/`sealed record`؛ `ConfigureAwait(false)` در library.
- **§0.1 را نادیده نگیر** — hash روی بایت gzip خام است، نه JSON؛ دیکمپرس دستی؛ `AutomaticDecompression` خاموش. وگرنه تست‌های integrity شکست می‌خورند.
- `BatchNotFound`/`ResyncRequired` reuse (S12)؛ `Unauthorized` + ۴ کد جدید add.
- بعد از هر تسک: پوش. نپوش‌کردن = شکست.
