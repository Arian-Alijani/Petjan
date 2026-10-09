# TODO — Phase S01: Server host scaffold (`Animora.Server`)

> **برای مدل اجراکننده:** این فایل خودکفا است. تمام اطلاعاتِ استخراج‌شده از ریپو (مسیرها، امضاها،
> ثابت‌ها، الگوها) اینجا هست. **نیازی به بازخوانی دوباره‌ی ریپو برای «فهمیدن» نداری** — مستقیم پیاده‌سازی کن.
> منبعِ فاز: `backend-roadmap/phases/PHASE-S01-server-host-scaffold.md` (این فایل قرار است در تسک پایانی حذف شود).
>
> **قانون قفل‌شده‌ی push (مرگ/زندگی):** بعد از اتمام هر تسک ⟵ `scripts/git/push.sh "type(scope): msg"` را اجرا کن.
> اگر exit code = 3 بود، یک‌بار ابزار `setup_github_environment` را صدا بزن و دوباره اجرا کن. اگر exit code = 2
> (تعارض rebase) بود، تعارض را با اولویت کد remote حل کن و دوباره اجرا کن.
>
> **محیط ساخت/تست (هرگز `dotnet build` خام نزن — سقف حافظه ~985 MiB):**
> - `scripts/sandbox/build.sh full` ⟵ باید 0 warning / 0 error بدهد.
> - `scripts/sandbox/test.sh Server` ⟵ تست‌های Server سبز.
> - `scripts/sandbox/run.sh` ⟵ باید `SMOKE RUN OK` چاپ کند (دسکتاپ نباید دست بخورد).
> - `source scripts/sandbox/_env.sh` قبل از هر `dotnet sln`/`dotnet` خام (برای PATH ابزارها).
> - `scripts/sandbox/install-prereqs.sh` یک‌بار (idempotent) اگر SDK نصب نیست.
> - `dotnet-ef` در `.sandbox/tools/dotnet-ef` است (در S01 لازم نیست؛ هیچ migration نداریم).

---

## 0) درکِ فشرده‌ی فاز (خلاصه‌ی خالص)

**هدف:** ساختِ هاستِ سرور `src/Animora.Server` (ASP.NET Core 10 / Kestrel، یک پراسس) که:
- یک **بلوک startup شماره‌دار و ثابت** دارد (مراحل 1..7).
- options را با `ValidateOnStart` اعتبارسنجی می‌کند (شکست پیکربندی = شکست در boot، نه اولین request).
- `SecretGuard` دارد: اگر connection-string/secret واقعی داخل `appsettings*.json` باشد، boot را رد می‌کند.
- `GlobalExceptionMiddleware` دارد که هر exception مدیریت‌نشده را به `WireApiError` (HTTP 500) تبدیل می‌کند و
  بیرون از Development هرگز stack را لو نمی‌دهد.
- دو health endpoint **ناشناس (anonymous)** دارد: `/health/live` و `/health/ready`.
- پنج rate-limit policy نام‌دار دارد.
- OpenAPI first-party فقط در Development (یا با فلگ `Server:EnableOpenApi=true`).
- **پایگاه‌داده، identity، هیچ route کسب‌وکاری ندارد** — فقط boot می‌شود، health پاسخ می‌دهد، secret-in-config را رد می‌کند.
- پروژه‌ی تست `tests/Animora.Server.Tests` را به subject وصل می‌کند و `LayeringRules` را برای جداسازی client/server گسترش می‌دهد.

**چیزی که در S01 نیست (خارج از scope — دست نزن):** DB/`DbContext`/DataProtection/readiness DB & key probe → S02. auth/tenant/authZ/entitlement → S04/S05. `/api/commands` → S06. `/sync/*` → S11/S12. `/operator/*` → S14. `/admin` → S19. files → S17. OpenTelemetry/metrics → S18. packaging → G09.

**اصل Offline (ثابت):** دسکتاپ باید کاملاً آفلاین و بدون سرور/PostgreSQL/شبکه استارت و کار کند. این فاز چیزی به نیازهای استارتِ دسکتاپ اضافه نمی‌کند. **کلاینت دست نمی‌خورد** (acceptance #4).

---

## 1) حقایق خالصِ ریپو (verified — نیازی به کشفِ دوباره نیست)

**مسیرها و پروژه‌های موجود `src/`:** `Animora.Domain`, `Animora.Application`, `Animora.Contracts`,
`Animora.Contracts.Wire`, `Animora.Persistence.Sqlite`, `Animora.Infrastructure.Local`, `Animora.Client.Services`,
`Animora.Desktop`, `Animora.Sync.Abstractions`. (پروژه‌های سرور/Postgres هنوز **وجود ندارند**.)
`src/Animora.Server` اکنون **وجود ندارد** (باید ساخته شود).

**تست‌های موجود `tests/`:** `Animora.Application.Tests`, `Animora.Domain.Tests`, `Animora.Persistence.Tests`,
`Animora.Server.Tests` (placeholder), `Animora.Sync.Tests`.

**`tests/Animora.Server.Tests` امروز:**
- `Animora.Server.Tests.csproj` = فقط xunit refs + کامنت `<!-- No subject reference until D01 adds Animora.Server. -->`
- `PlaceholderTests.cs` = `namespace Animora.Server.Tests; public sealed class PlaceholderTests { [Fact] public void Project_IsWiredUp() => Assert.True(true); }`
- csproj فعلی:
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
      <IsPackable>false</IsPackable>
      <IsTestProject>true</IsTestProject>
    </PropertyGroup>
    <ItemGroup>
      <PackageReference Include="Microsoft.NET.Test.Sdk" />
      <PackageReference Include="xunit" />
      <PackageReference Include="xunit.runner.visualstudio" />
    </ItemGroup>
    <!-- No subject reference until D01 adds Animora.Server. -->
  </Project>
  ```

**`Directory.Build.props` (ارثی برای همه):** `net10.0`, `LangVersion 14.0`, `Nullable enable`, `ImplicitUsings enable`,
`NuGetAuditMode=all`, `EnableNETAnalyzers=true`, `TreatWarningsAsErrors=false`.
⟵ یعنی در csproj سرور **TargetFramework/LangVersion ننویس** (ارثی است).

**`Directory.Packages.props` — pinهای مرتبطِ موجود:**
`Microsoft.Extensions.Options 10.0.12`, `Microsoft.Extensions.DependencyInjection 10.0.12`,
`Microsoft.Extensions.Logging.Abstractions 10.0.12`, `xunit 2.9.2`, `xunit.runner.visualstudio 2.8.2`,
`Microsoft.NET.Test.Sdk 17.12.0`. (`ManagePackageVersionsCentrally=true` — نسخه را در csproj ننویس، فقط `Include`.)

**`WireApiError` (استفاده‌ی مستقیم — namespace و امضا دقیقاً این‌هاست):**
```csharp
namespace Animora.Contracts.Wire.Errors;   // فایل: src/Animora.Contracts.Wire/Errors/WireApiError.cs
public sealed record WireApiError(string Code, string? Detail, IReadOnlyList<WireFieldError> Fields, string CorrelationId);
public sealed record WireFieldError(string Field, string Code);
```

**`WireContract`:** `namespace Animora.Contracts.Wire;` — `public const int Version = 1;`,
`public const string HeaderName = "X-Animora-Contract-Version";` (در S01 مصرف مستقیم ندارد؛ فقط برای آگاهی).

**`IdGenerator` (ساخت correlation id):** `namespace Animora.Domain.Primitives;` — `public static class IdGenerator`،
متد `public static string NewId()` (ULID 26-کاراکتری). استفاده: `Animora.Domain.Primitives.IdGenerator.NewId()`.

**`ErrorCodes.Unexpected`:** در `src/Animora.Domain/Results/ErrorCodes.cs` ⟵ `public static readonly ErrorCode Unexpected = new("UNEXPECTED", ...)`.
نکته‌ی فاز: **هیچ ErrorCode جدیدی در Domain اضافه نمی‌شود.** کدهای ترنسپورتِ `UNEXPECTED` و `RATE_LIMITED` فقط
به‌صورت **string constant** در `Animora.Server.Hosting.ServerErrorCodes` می‌آیند (کد رشته‌ایِ wire = `"UNEXPECTED"` و `"RATE_LIMITED"`).

**`AssemblyMarker`ها:** `src/Animora.Domain/AssemblyMarker.cs` و `src/Animora.Application/AssemblyMarker.cs` موجودند
(برای بارگذاری assembly در تست‌های layering، اگر لازم شد). `Program` به‌صورت `public partial class Program;` پابلیک می‌شود.

**الگوی csproj یک پروژه‌ی library با ref و InternalsVisibleTo (نمونه از Client.Services):**
```xml
<ItemGroup>
  <InternalsVisibleTo Include="Animora.Application.Tests" />
</ItemGroup>
```
(`InternalsVisibleTo` مستقیماً در `<ItemGroup>` csproj کار می‌کند — در این ریپو همین الگو استفاده شده.)

**`LayeringRulesTests` (فایل `tests/Animora.Domain.Tests/LayeringRulesTests.cs`) — helperهای موجود برای EXTEND:**
```csharp
private static AssemblyName[] ReferencedAssembliesOf(Assembly a) => a.GetReferencedAssemblies();
private static bool ReferencesNameStartingWith(Assembly a, string prefix)
    => ReferencedAssembliesOf(a).Any(r => r.Name is not null && r.Name.StartsWith(prefix, StringComparison.Ordinal));
```
کلاس `public sealed class LayeringRulesTests` در `namespace Animora.Domain.Tests`. usingها: `System`, `System.Linq`,
`System.Reflection`, `Xunit`, `Microsoft.Extensions.DependencyInjection`. فقط **fact اضافه کن، هیچ‌وقت ضعیف نکن.**

**هیچ `appsettings*.json` در ریپو وجود ندارد** (سرور اولین مصرف‌کننده است).

**نکته‌ی ARCH §4 (startup order مرجع):** (1) bind+ValidateOnStart options؛ (2) logging؛ (3) DataProtection؛
(4) DbContext؛ (5) health؛ (6) OpenAPI (Dev + opt-in)؛ (7) pipeline: global-exception → HSTS/HTTPS-redirect →
request-logging (skip `/health/*`) → tenant → authN → authZ → entitlement → routing → endpoints.
در S01: مرحله‌ی (3) و (4) فقط **کامنت placeholder** (`// 3. DataProtection → S02`, `// 4. DbContext → S02`)؛
اسلات‌های tenant/authN/authZ/entitlement هم فقط کامنت (added S04/S05).

**نکته‌ی ARCH §4 (TLS):** TLS توسط OS مذاکره می‌شود؛ **هیچ انتسابِ `SslProtocols` جایی نباشد.** گواهی Kestrel فقط
از مسیر/env پیکربندی می‌آید.

**نکته‌ی STACK §5.1/§11:** ASP.NET Core 10 + Kestrel؛ OpenAPI first-party (`Microsoft.AspNetCore.OpenApi` 10.x)؛
`Npgsql*` فقط در `Animora.Persistence.PostgreSql` (در S01 **نباید** `Npgsql` جایی در سرور ظاهر شود — acceptance #3).

---

## 2) تصمیمات طراحیِ قفل‌شده (نام‌ها دقیقاً همین‌ها — از خودت نساز)

### 2.1 پروژه
- `src/Animora.Server/Animora.Server.csproj`، `<Project Sdk="Microsoft.NET.Sdk.Web">`، `net10.0` ارثی (ننویس).
- `ProjectReference`ها (فقط این سه): `Animora.Domain`, `Animora.Application`, `Animora.Contracts.Wire`.
  (persistence در S02 می‌آید — الان **نه**.)
- `PackageReference Include="Microsoft.AspNetCore.OpenApi"` (بدون نسخه؛ pin مرکزی).
- `<InternalsVisibleTo Include="Animora.Server.Tests" />`.
- در `Program.cs` انتهای فایل: `public partial class Program;` (برای `WebApplicationFactory`).

### 2.2 pinهای جدید در `Directory.Packages.props` (دقیقاً دو تا، خط وسطِ ItemGroup backend)
```xml
<PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="10.0.12" />
<PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.12" />  <!-- فقط تست -->
```
هیچ پکیج دیگری اضافه نشود.

### 2.3 surfaceهای رزرو‌شده — فقط constant (کلاس `ServerRoutes`)
```
Commands   = "/api/commands"
Sync       = "/sync"
Auth       = "/auth"
Operator   = "/operator"
Admin      = "/admin"
HealthLive = "/health/live"
HealthReady= "/health/ready"
```
(فازهای بعدی endpointها را زیر این‌ها map می‌کنند. S01 فقط ثابت‌ها.)

### 2.4 Options
- کلاس `ServerOptions`، section `"Server"`:
  - `DataRootPath` (الزامی، مسیر **مطلق**، باید وجود داشته/writable در readiness)
  - `EnableOpenApi` (bool، پیش‌فرض false)
  - `PublicBaseUrl` (اختیاری، absolute URI)
- `ServerOptionsValidator : IValidateOptions<ServerOptions>` با `ValidateOnStart`. شکست ⟵ پراسس با پیام واضح
  exit می‌کند (misconfiguration در boot). `DataRootPath` باید non-empty و `Path.IsPathRooted` (مطلق) باشد؛
  `PublicBaseUrl` اگر داده شد باید absolute URI باشد.

### 2.5 `SecretGuard` (static، `namespace Animora.Server.Hosting`)
- بر روی `IConfiguration` **فقط provider valueهایی که از فایل‌های JSON می‌آیند** را اسکن می‌کند.
- هر key که نامش با regex `(?i)(password|pwd|secret|connectionstring|apikey|privatekey)` match شود **و**
  مقدارش «ادبیاتی/literal» به نظر برسد (شامل `Password=`/`Pwd=`/`Host=` همراه `;` **یا** برای کلیدهای
  password/secret/apikey/privatekey مقدار non-empty) ⟵ خطا.
- `throw new SecretInConfigurationException(string keyPath)` ⟵ پیام فقط **نام key** را می‌گوید، **هرگز مقدار** را.
- Provider‌های Environment-variable و user-secrets **معاف‌اند**.
- در مرحله‌ی (1) startup صدا زده می‌شود.
- پیاده‌سازی تشخیص «از JSON آمده»: روی `(configuration as IConfigurationRoot).Providers` حلقه بزن و فقط
  `Microsoft.Extensions.Configuration.Json.JsonConfigurationProvider` را بررسی کن؛ از هر provider با
  `TryGet(key, out value)` مقدار را بخوان و key‌ها را از `provider.GetChildKeys`/شمارش فایل‌ها به‌دست آور
  (ساده‌ترین راهِ پایدار: روی `JsonConfigurationProvider` کلیدها را از طریق reflection روی `Data`
  dictionary اگر لازم شد؛ اما ترجیحاً از `IConfigurationRoot` پیمایش کن و برای هر key مشخص کن کدام provider
  مقدار نهایی را داده — اگر JsonConfigurationProvider بود، بررسی کن).
  **نکته‌ی تست‌پذیری:** متد باید روی یک `IConfiguration` ساخته‌شده از `AddJsonStream`/`AddJsonFile` کار کند و
  روی `AddEnvironmentVariables`/`AddInMemoryCollection` خطا ندهد (تستِ `Allows_EnvironmentProvider`).

### 2.6 `GlobalExceptionMiddleware`
- هر exception مدیریت‌نشده ⟵ HTTP 500 با بدنه‌ی
  `WireApiError(Code="UNEXPECTED", Detail = env.IsDevelopment() ? ex.Message : null, Fields=[], CorrelationId)`.
- بیرون از Development هرگز stack لو نرود.
- `OperationCanceledException` وقتی `HttpContext.RequestAborted.IsCancellationRequested` ⟵ هیچ بدنه‌ای ننویس (کلاینت رفته).
- correlation id: اگر header ورودیِ `X-Correlation-Id` معتبر بود (`≤64` کاراکتر، فقط `[A-Za-z0-9-_]`) همان؛
  وگرنه `IdGenerator.NewId()`. و آن را به‌صورت response header `X-Correlation-Id` echo کن.
- منطق correlation را در یک helper استاتیک `CorrelationHeader` بگذار (نام فایل `CorrelationHeader.cs`):
  مثلاً `static string Resolve(HttpContext ctx)` + ثابتِ نام header `"X-Correlation-Id"`.
- `Code="UNEXPECTED"` از `ServerErrorCodes.Unexpected` بیاید.

### 2.7 Health (anonymous، بدون secret)
- `/health/live` ⟵ فقط پراسس، هیچ وابستگی را لمس نمی‌کند، 200 با بدنه `{"status":"live"}`.
- `/health/ready` ⟵ لیست `IReadinessProbe` را اجرا می‌کند. interface در `Animora.Server.Hosting`:
  ```csharp
  public interface IReadinessProbe { string Name { get; } Task<ReadinessResult> CheckAsync(CancellationToken ct); }
  ```
  `ReadinessResult` یک record/استراکت ساده: `Healthy` یا `Unhealthy` (مثلاً `sealed record ReadinessResult(bool IsHealthy)`
  با فکتوری‌های `Healthy()`/`Unhealthy()` — نام‌گذاری داخلی آزاد ولی ساده و sealed).
- S01 فقط `DataRootWritableProbe` را می‌فرستد (چک: `ServerOptions.DataRootPath` وجود دارد و قابل نوشتن است —
  یک فایل temp بساز/پاک کن). `Name` مثلاً `"data-root"`.
- اگر هر probe `Unhealthy` بود ⟵ 503 با بدنه `{"status":"not-ready","failed":["<probe name>"]}` (فقط نام‌ها، هرگز متن exception).
- (S02 بعداً `DatabaseProbe` + `KeyRingProbe` اضافه می‌کند — الان نه.)

### 2.8 Rate limiting (`AddRateLimiter` از `Microsoft.AspNetCore.RateLimiting` داخلی)
- **پنج policy نام‌دار fixed-window:**
  - `auth` ⟵ 10/min per IP
  - `commands` ⟵ 120/min per tenant-or-IP
  - `sync` ⟵ 60/min
  - `files` ⟵ 60/min
  - `operator` ⟵ 30/min
- نام policyها ثابت در کلاس `RateLimitPolicies` (مثلاً `public const string Auth = "auth";` ...).
- پنجره‌ها options-driven: `Server:RateLimits:<policy>:PermitLimit` و `:WindowSeconds`، هر دو باید `> 0` اعتبارسنجی شوند
  (کلاس `RateLimitOptions` + اعتبارسنجی؛ می‌تواند بخشی از `ServerOptions`/validator باشد یا options مجزا — ساده نگه‌دار).
- rejection ⟵ 429 با بدنه `WireApiError(Code="RATE_LIMITED", ...)` + header `Retry-After`.
  (`RATE_LIMITED` از `ServerErrorCodes.RateLimited`.)
- **endpointهای health محدود نمی‌شوند.**
- partitioning: برای `auth` از IP (`ctx.Connection.RemoteIpAddress`)، برای بقیه فعلاً IP (tenant هنوز نیست؛
  کامنت بگذار که tenant-partition در S04/S05 می‌آید).

### 2.9 `ServerErrorCodes` (static، `Animora.Server.Hosting`)
```csharp
public const string Unexpected = "UNEXPECTED";
public const string RateLimited = "RATE_LIMITED";
```
(کدهای ترنسپورتِ سرور؛ domain code نیستند.)

### 2.10 appsettings
- `appsettings.json` ⟵ فقط پیش‌فرض‌های **غیرمحرمانه**: `Server:DataRootPath` **حذف** (باید از env
  `Server__DataRootPath` بیاید)، `Logging`، `Server:RateLimits` (پنج policy با PermitLimit/WindowSeconds).
- `appsettings.Development.json` ⟵ `Server:EnableOpenApi=true`، `Server:DataRootPath="./.data"`.

### 2.11 Layering/Isolation (تست‌ها)
- fact سمت سرور در `tests/Animora.Server.Tests/ServerLayeringTests.cs`:
  `Server_DoesNotReferenceClientProjects` ⟵ assert که `Animora.Server` هیچ‌کدام از این‌ها را ref نمی‌کند:
  `Animora.Client.Services`, `Animora.Persistence.Sqlite`, `Animora.Infrastructure.Local`, `Animora.Desktop`,
  `Animora.Sync.Custom`. (assembly سرور را با `typeof(Program).Assembly` یا `typeof(ServerRoutes).Assembly` بگیر.)
- fact سمت کلاینت در `tests/Animora.Domain.Tests/LayeringRulesTests.cs`:
  `Client_DoesNotReferenceServerProjects` ⟵ assert که Desktop/Client.Services/Infrastructure.Local/Persistence.Sqlite
  هیچ‌کدام `Animora.Server*`, `Animora.Persistence.PostgreSql`, `Animora.Web.Admin` را ref نمی‌کنند
  (با چک رشته‌ایِ `AssemblyName` — **بدون** project ref؛ از helperهای موجود `ReferencesNameStartingWith` استفاده کن).

---

## 3) فایل‌هایی که ساخته/ویرایش می‌شوند (نقشه)

| مسیر | محتوا |
|---|---|
| `Directory.Packages.props` (ویرایش) | +2 pin (OpenApi 10.0.12، Mvc.Testing 10.0.12) |
| `Animora.sln` (ویرایش) | افزودن `Animora.Server` |
| `src/Animora.Server/Animora.Server.csproj` | هاست Sdk.Web، 3 ref، OpenApi، InternalsVisibleTo |
| `src/Animora.Server/Program.cs` | بلوک startup شماره‌دار + `public partial class Program;` |
| `src/Animora.Server/Hosting/ServerOptions.cs` | options |
| `src/Animora.Server/Hosting/ServerOptionsValidator.cs` | validator |
| `src/Animora.Server/Hosting/SecretGuard.cs` | + `SecretInConfigurationException` |
| `src/Animora.Server/Hosting/GlobalExceptionMiddleware.cs` | → `WireApiError` |
| `src/Animora.Server/Hosting/CorrelationHeader.cs` | resolve/echo `X-Correlation-Id` |
| `src/Animora.Server/Hosting/IReadinessProbe.cs` | interface + `ReadinessResult` |
| `src/Animora.Server/Hosting/DataRootWritableProbe.cs` | probe قابل‌نوشتن‌بودنِ DataRoot |
| `src/Animora.Server/Hosting/RateLimitPolicies.cs` | ثابت‌های نام policy |
| `src/Animora.Server/Hosting/RateLimitOptions.cs` | PermitLimit/WindowSeconds |
| `src/Animora.Server/Hosting/ServerRoutes.cs` | ثابت‌های route |
| `src/Animora.Server/Hosting/ServerErrorCodes.cs` | `UNEXPECTED`/`RATE_LIMITED` |
| `src/Animora.Server/appsettings.json` | پیش‌فرض‌های غیرمحرمانه |
| `src/Animora.Server/appsettings.Development.json` | EnableOpenApi + DataRootPath=./.data |
| `tests/Animora.Server.Tests/Animora.Server.Tests.csproj` (ویرایش) | subject ref + Mvc.Testing؛ placeholder-comment حذف |
| `tests/Animora.Server.Tests/HostSmokeTests.cs` | تست |
| `tests/Animora.Server.Tests/SecretGuardTests.cs` | تست |
| `tests/Animora.Server.Tests/GlobalExceptionMiddlewareTests.cs` | تست |
| `tests/Animora.Server.Tests/RateLimitTests.cs` | تست |
| `tests/Animora.Server.Tests/ServerLayeringTests.cs` | تست |
| `tests/Animora.Server.Tests/SourceScanTests.cs` | تست اسکن سورس |
| `tests/Animora.Domain.Tests/LayeringRulesTests.cs` (ویرایش) | fact: client ⊁ server |
| حذف: `tests/Animora.Server.Tests/PlaceholderTests.cs` | — |

---

## 4) تست‌های الزامی (نام‌ها دقیقاً — acceptance #2)

| کلاس | متد | چه چیزی را ثابت می‌کند |
|---|---|---|
| `HostSmokeTests` | `Live_Returns200_WithoutDependencies` | `/health/live` → 200 بدون لمس وابستگی |
| `HostSmokeTests` | `Ready_Returns200_WhenDataRootWritable` | `/health/ready` → 200 وقتی DataRoot قابل‌نوشتن |
| `HostSmokeTests` | `Ready_Returns503_NamesFailedProbeOnly_WhenDataRootMissing` | 503 + فقط نام probe در `failed` |
| `SecretGuardTests` | `Throws_ForLiteralConnectionStringInJson` | connection-string داخل JSON → throw |
| `SecretGuardTests` | `Allows_EnvironmentProvider` | env provider معاف است |
| `GlobalExceptionMiddlewareTests` | `UnhandledException_Returns500WireApiError_NoStack_InProduction` | 500 + بدون stack در Production |
| `GlobalExceptionMiddlewareTests` | `Echoes_CorrelationId` | echo شدن `X-Correlation-Id` |
| `RateLimitTests` | `Auth_Policy_Returns429_WithRetryAfter_AfterLimit` | عبور از حد → 429 + `Retry-After` |
| `ServerLayeringTests` | `Server_DoesNotReferenceClientProjects` | عدم ارجاع سرور به پروژه‌های کلاینت |
| `SourceScanTests` | `No_HardCoded_SslProtocols` | رشته‌ی `SslProtocols.` زیر `src/Animora.Server` نیست |

> **راهنمای پیاده‌سازیِ تست‌های HTTP:** از `Microsoft.AspNetCore.Mvc.Testing` و `WebApplicationFactory<Program>`
> استفاده کن (به همین خاطر `public partial class Program;` لازم است). برای تستِ Production-بودن، محیط را با
> `builder.UseEnvironment("Production")` یا یک factory که `ASPNETCORE_ENVIRONMENT=Production` ست می‌کند بساز.
> برای `Ready_Returns503...` یک `DataRootPath` به مسیری ناموجود/غیرقابل‌نوشتن بده (از طریق
> `ConfigureAppConfiguration`/`WebApplicationFactory.WithWebHostBuilder`).
> برای `SourceScanTests` فقط فایل‌های `.cs` زیر مسیرِ نسبیِ `src/Animora.Server` را بخوان و `Assert.DoesNotContain("SslProtocols.", content)`؛
> مسیر ریشه را از `AppContext.BaseDirectory` به بالا پیدا کن تا به پوشه‌ی حاوی `Animora.sln` برسی.
> برای `SecretGuardTests` مستقیم `SecretGuard` را روی یک `IConfiguration` ساختگی صدا بزن (نیازی به هاست کامل نیست).

---

## 5) معیارهای پذیرش مکانیکی (acceptance — همه باید برقرار باشند)

1. `scripts/sandbox/build.sh full` ⟵ exit 0 با `0 Warning(s)`/`0 Error(s)`؛ `scripts/sandbox/run.sh` ⟵ `SMOKE RUN OK`.
2. `scripts/sandbox/test.sh Server` ⟵ ده تست نام‌بُرده‌ی بالا سبز.
3. `grep -rn "SslProtocols\." src/Animora.Server` ⟵ هیچ؛ `grep -rn "Npgsql" src/Animora.Server` ⟵ هیچ.
4. `git diff --stat -- src/Animora.Domain src/Animora.Application src/Animora.Persistence.Sqlite src/Animora.Desktop` ⟵ خالی (کلاینت دست‌نخورده).
5. `grep -c "AddRateLimiter" src/Animora.Server/Program.cs` ⟵ ≥ 1 و پنج نام `auth|commands|sync|files|operator` در `RateLimitPolicies.cs`.

---

## 6) به‌روزرسانی `PROGRESS.md` (انتهای فاز — طبق recipe فاز)

فایل: `backend-roadmap/PROGRESS.md`
- ردیف `S01` در جدول «Phase status» ⟵ `✅ Done` (+ تاریخ + خلاصه‌ی یک‌خطی).
- «Next phase» ⟵ `S02`.
- یک entry `### S01` به «Repo state log» اضافه کن (چه چیزی اکنون وجود دارد: پروژه‌ها، migration/ordinal،
  error codeهای اضافه‌شده [=هیچ domain code؛ دو string ترنسپورت]، کلاس‌های تست).
- هر انحراف را در «Decision & deviation log» ثبت کن.

---

## تسک‌لیست (هر تسک = یک واحد؛ بعد از هرکدام push اجباری)

- [x] **T0 — آماده‌سازی.** ساخت `Plan/`، خواندن فاز و مراجع، نوشتن همین TODO.
      سپس: `scripts/git/push.sh "chore(plan): add S01 execution TODO"`
- [x] **T1 — pinها.** افزودن دو `PackageVersion` (OpenApi 10.0.12، Mvc.Testing 10.0.12) به `Directory.Packages.props`
      (داخل ItemGroup backend). سپس push: `chore(deps): pin AspNetCore.OpenApi + Mvc.Testing 10.0.12 (S01)`
- [x] **T2 — csproj سرور + sln.** ساخت `src/Animora.Server/Animora.Server.csproj` (Sdk.Web، 3 ref، OpenApi،
      InternalsVisibleTo). افزودن به solution: `source scripts/sandbox/_env.sh && dotnet sln Animora.sln add src/Animora.Server/Animora.Server.csproj`.
      یک `Program.cs` حداقلی موقت بگذار که کامپایل شود (تا sln سالم بماند). push: `feat(server): add Animora.Server project + sln entry (S01)`
- [x] **T3 — کلاس‌های Hosting.** ساخت همه‌ی فایل‌های `src/Animora.Server/Hosting/*.cs` طبق §2.3–§2.9:
      `ServerRoutes`, `ServerErrorCodes`, `ServerOptions`, `ServerOptionsValidator`, `RateLimitOptions`,
      `RateLimitPolicies`, `SecretGuard` (+exception), `CorrelationHeader`, `GlobalExceptionMiddleware`,
      `IReadinessProbe` (+`ReadinessResult`), `DataRootWritableProbe`. push: `feat(server): hosting primitives — options, secret-guard, exception mw, probes, rate-limit (S01)`
- [x] **T4 — Program.cs + appsettings.** نوشتن بلوک startup شماره‌دار (1..7 طبق §2 و ARCH §4)، سیم‌کشیِ
      همه‌ی کلاس‌های T3، map کردن `/health/live` و `/health/ready`، `AddRateLimiter` با پنج policy،
      OpenAPI فقط Dev/opt-in، و `public partial class Program;`. ساخت `appsettings.json` و
      `appsettings.Development.json` (§2.10). push: `feat(server): numbered startup pipeline + health + appsettings (S01)`
- [x] **T5 — سیم‌کشیِ پروژه‌ی تست.** ویرایش `tests/Animora.Server.Tests/Animora.Server.Tests.csproj`:
      افزودن `ProjectReference` به `Animora.Server` + `PackageReference Microsoft.AspNetCore.Mvc.Testing`،
      حذف کامنت «No subject reference»؛ حذف `PlaceholderTests.cs`. push: `test(server): wire Server.Tests to subject + Mvc.Testing (S01)`
- [x] **T6 — تست‌ها.** نوشتن `HostSmokeTests`, `SecretGuardTests`, `GlobalExceptionMiddlewareTests`,
      `RateLimitTests`, `ServerLayeringTests`, `SourceScanTests` (ده متدِ §4). ویرایش
      `tests/Animora.Domain.Tests/LayeringRulesTests.cs` ⟵ افزودن fact `Client_DoesNotReferenceServerProjects`.
      push: `test(server): S01 host/secret-guard/exception/rate-limit/layering/source-scan tests`
- [x] **T7 — build + test + run (verification).** اجرای `scripts/sandbox/build.sh full` (0/0)،
      `scripts/sandbox/test.sh Server` (سبز)، `scripts/sandbox/run.sh` (`SMOKE RUN OK`). اجرای grepهای
      acceptance #3/#4/#5. **اگر فیکس لازم شد و سنگین بود، همان‌جا با پیام واضح push کن.** در پایان،
      اگر چیزی تغییر کرد: push: `fix(server): make S01 build/tests green (0 warnings)` — اگر چیزی تغییر نکرد، این تسک push جدا ندارد.
- [ ] **T8 — PROGRESS.md.** به‌روزرسانی طبق §6. push: `docs(progress): mark S01 done, next S02`
- [ ] **T9 — تسک پایانی: حذف این فایل.** حذف `Plan/PHASE-S01-TODO.md` (فقط همین فایلِ داخل `Plan`؛ به
      فایل فازِ روادمپ `backend-roadmap/phases/PHASE-S01-*.md` و بقیه کاری نداشته باش — **دست نزن**).
      push: `chore(plan): remove completed S01 TODO`

> **یادآوری نهایی:** بعد از هر تسکِ بالا بلافاصله `scripts/git/push.sh "..."` را اجرا کن. اگر حین یک تسک
> کدِ سنگین/فیکس بزرگ نوشتی، همان‌جا push جداگانه با پیام قابل‌فهم بزن. عدم push = شکست.
