نتیجه کلی: اسکلت معماری خوب است و تصمیم Modular Monolith + Offline-first + Server-authoritative + Outbox/Idempotency تصمیم‌های قابل دفاعی هستند؛ اما در وضعیت فعلی من این سند را برای شروع implementation مالی/clinical production-ready نمی‌دانم. چند تناقض و خلأ در آن وجود دارد که اگر همین حالا به code تبدیل شوند، بعداً اصلاح‌شان بسیار پرهزینه خواهد بود.

1. مهم‌ترین نتیجه: 15 ایراد Blocking

این‌ها مواردی هستند که قبل از implementation باید اصلاح شوند.

ID	شدت	موضوع
A-01	بحرانی	Offline authentication/revocation از نظر منطقی کامل نیست
A-02	بحرانی	License anti-cloning + monotonic clock راه‌حل کافی نیست
A-03	بحرانی	Zero-Internet با onboarding/subscription مکانیزم انتقال ندارد
A-04	بحرانی	Single-node عملاً Single Point of Failure کل SaaS است و RTO/RPO تعریف نشده
A-05	بحرانی	قرارداد delta-sync در چند قسمت با خودش تناقض دارد
A-06	بحرانی	retention برای tombstone و offline window تعریف نشده
A-07	بحرانی	initial sync فاقد snapshot/barrier سازگار است
A-08	بحرانی	ادعای "inventory movement ⇐ همیشه convergence صحیح" نادرست است
A-09	بحرانی	optimistic concurrency برای appointment/resource allocation کافی نیست
A-10	بحرانی	Accounting model هنوز یک General Ledger واقعی را تعریف نکرده
A-11	بحرانی	Cross-context financial consistency تعریف نشده
A-12	بحرانی	Data-loss سناریوی client disk loss عملاً queue را recover نمی‌کند
A-13	بحرانی	Server/CA/license private keys با مدل recovery فعلی تناقض دارند
A-14	بحرانی	Browser cookie authentication بدون CSRF contract ناقص است
A-15	بحرانی	Tenant isolation بیش از حد قوی ادعا شده، در حالی که query filters قابل bypass هستند

بعد از این‌ها حدود 20 ایراد High/Medium دیگر هم وجود دارد که پایین‌تر آورده‌ام.

2. §0 — Architecture at a Glance
تصمیم‌های خوب

سه تصمیم اصلی واقعاً قوی هستند:

Modular Monolith برای سروری که عمداً ضعیف است انتخاب مناسبی است. Microsoft هم Clean Architecture را به عنوان ساختاری معرفی می‌کند که business/application core را از infrastructure جدا می‌کند.

تفکیک:

state replication ≠ business intent

هم از نظر معماری درست است و هم با واقعیت Offline-first سازگارتر از یک generic CRUD sync engine است. خود Microsoft در معماری‌های offline-first هم local database + queued writes + incremental synchronization را به کار می‌گیرد.

و:

server = global authority

برای accounting، inventory و scheduling تصمیم درستی است.

ایراد اول: WAF را بیش از حد به خود architecture تبدیل کرده‌ای

Azure Well-Architected Framework پنج pillar دارد و برای traceability بسیار خوب است، اما خودش architecture style یا deployment prescription نیست. Microsoft هم آن را framework برای quality/reliability/security/performance معرفی می‌کند.

پس بهتر است:

Architecture decisions
    ↓
validated against
    ↓
WAF quality attributes

نه:

WAF pillar
    ↓
architecture decision

این فرق در آینده جلوی overengineering را می‌گیرد.

3. §1 — Architecture Drivers

اینجا یک مشکل مهم وجود دارد:

R7 هنوز دقیقاً requirement قابل تست نیست

نوشته‌ای:

fully operational with zero external-Internet connectivity

ولی باید مشخص کنی:

Runtime-plane:     Internet forbidden
Management-plane:  ?
Provisioning-plane: ?
Update-plane:      ?
Backup-plane:      ?
Support-plane:     ?

در وضعیت فعلی این‌ها با هم مخلوط‌اند.

مثلاً:

customer از سایت خرید می‌کند
سایت باید provisioning intent بفرستد
license باید renew شود
subscription باید تغییر کند
server ممکن است کاملاً disconnected باشد

پس باید architecture صریحاً بین این‌ها فرق بگذارد:

DATA PLANE
clinic operation
→ must work with Internet = OFF

CONTROL / MANAGEMENT PLANE
provisioning / renewal / update / support
→ may require controlled connectivity
   OR signed offline bundle

بدون چنین جداسازی، R7 با §13 سازگار نیست.

4. §3–§4 — C4 Context / Container

ساختار کلی خوب است، ولی یک نکته مستندسازی مهم وجود دارد.

C4 و arc42 متعلق به Microsoft نیستند

در §23 نوشته شده:

C4 … arc42 … as documented on Microsoft Learn / Azure Architecture Center

این از نظر provenance درست نیست.

C4 منبع رسمی خودش را دارد و توسط Simon Brown تعریف شده است.

arc42 هم روش مستندسازی مستقل خودش است و حتی ساختار 12 قسمتی مشخص دارد.

بنابراین §23 باید سه دسته source داشته باشد:

Primary methodology
    C4 → c4model.com
    arc42 → arc42.org

Microsoft architecture guidance
    .NET Clean Architecture
    WAF
    Azure Architecture Center
    ASP.NET Core
    SQL Server
    Dynamics 365

Standards
    RFCs
    TLS/OAuth/etc.

این اصلاح مهم است چون AI agent نباید بعداً provenance جعلی برای تصمیم‌ها بسازد.

5. §5 — Clean / Onion Architecture

ساختار:

Presentation
  ↓
Application
  ↓
Domain
  ↑
Infrastructure

مناسب است.

Microsoft هم Clean Architecture را با business logic در مرکز و dependency inversion به سمت core توضیح می‌دهد.

اما این خط مشکل دارد:

every external-facing capability ... is defined as a port owned by Domain/Application

برای ISyncEngine، Domain نباید صاحب abstraction باشد.

Sync یک domain capability نیست.

بهتر:

Client Application Layer
    ↓
ISyncTransport / ISyncCoordinator
    ↓
Infrastructure

نه:

Domain
    ↓
ISyncEngine

به‌خصوص چون خود §6 می‌گوید Synchronization bounded context نیست و business domain نیست.

اصلاح
Domain
  هیچ اطلاعی از Sync ندارد.

Application
  orchestration interface برای Sync دارد.

Infrastructure
  sync implementation را فراهم می‌کند.
6. §6 — Bounded Contexts

اینجا یکی از بزرگ‌ترین structural risks وجود دارد.

Microsoft در DDD guidance روی استقلال bounded context و ownership persistence تأکید می‌کند؛ bounded context ideally persistence خودش را هم مالک می‌شود.

اما architecture تو می‌گوید:

14 bounded contexts
        ↓
1 shared database

این ذاتاً غلط نیست، ولی باید قواعد بسیار سختی اضافه کنی.

در حال حاضر مشخص نیست آیا این موارد ممنوع‌اند یا مجاز:

Finance → SELECT InventoryTables
Finance → FK → SalesTable
Reporting → JOIN FinanceTables + InventoryTables
Sales → JOIN PatientTables

اگر این‌ها مجاز شوند، bounded contextها عملاً به database partitions تبدیل می‌شوند، نه context واقعی.

پیشنهاد قطعی

حتی با shared DB:

dbo.Identity.*
dbo.Tenant.*
dbo.Patient.*
dbo.Finance.*
dbo.Inventory.*
...

و:

هیچ context نباید مستقیماً table context دیگر را query کند.

ارتباط فقط:

Public contract
Command
Query interface
Integration event

و ترجیحاً FK بین bounded contexts نداشته باش؛ reference ID داشته باش.

این کار بعداً Deployment Stamp و database separation را بسیار آسان‌تر می‌کند.

7. §6 — یک مشکل جدی‌تر: Cross-context Finance

این قسمت:

SaleCompleted ← create draft invoice

از نظر eventual event architecture قابل قبول است، ولی برای accounting کافی نیست.

مثلاً فروش:

Sale
Payment
Inventory decrease
COGS
Cash/AR
Tax
Invoice

ممکن است همه بخشی از یک business transaction باشند.

اگر:

Sales commits
   ↓
Outbox
   ↓
Finance consumes later

برای مدتی این وضعیت ممکن است رخ دهد:

Sale = complete
Inventory = decreased
Payment = settled
Finance = not posted

اگر این حالت مجاز است، باید صریحاً بگویی:

financially incomplete / pending posting

و اگر مجاز نیست، باید یک application-level transactional workflow داشته باشی که در یک DB transaction چند bounded context را هماهنگ کند.

این Distributed Transaction نیست، چون همه در یک process/database هستند.

یعنی:

Application Workflow
    ↓
Sales aggregate
Inventory aggregate
Finance posting
    ↓
ONE database transaction

اما domain ownership همچنان حفظ شود.

8. §7 — Shared Client Kernel

ایده خوب است، اما این بخش خطرناک است:

Shared ... Authorization policies

نباید authorization client/server را به عنوان یک implementation مشترک تلقی کنی.

کلاینت قابل compromise است.

بنابراین:

Client authorization = UX optimization / local gating

Server authorization = security authority

حتی اگر همان policy code را share کنی، نباید هیچ security invariant به اجرای client وابسته باشد.

ASP.NET Core هم authorization را مستقل از authentication می‌داند و برای resource-based authorization بر ارزیابی server-side resource تأکید می‌کند.

9. §8 — Sync Architecture

این مهم‌ترین بخش سند است و چند مشکل جدی دارد.

A-05 — UpdatedAt هنوز اشتباه مدل شده

ابتدا گفته‌ای:

UpdatedAt
— ONLY basis for delta cursors

بعد پایین‌تر می‌گویی:

strictly increasing server sequence
must back the delta cursor

این دو با هم سازگار نیستند.

باید تبدیل شود به:
ChangeCursor
    opaque
    server-issued
    strictly ordered within a sync scope

UpdatedAt
    informational/business/display timestamp

مثلاً:

ChangeSequence = 8471932
UpdatedAt      = 2026-10-08T08:31:44Z
Version        = opaque row concurrency token

Client فقط ChangeSequence را cursor می‌کند.

10. A-06 — Tombstone retention تعریف نشده

این یکی بحرانی است.

فرض کن:

Device offline = 120 days
Server deletes Patient X
Server deletes tombstone after 30 days
Device reconnects on day 120

Client هیچ وقت deletion را نمی‌بیند و ممکن است Patient X را دوباره sync کند.

بنابراین:

tombstone retention ≥ maximum supported offline duration + safety margin

باید invariant باشد.

و این برای:

deletes
revoked memberships
removed permissions
removed products
cancelled appointments
license/device revocation
changed references

هم مهم است.

11. A-07 — Initial sync consistency

این هم در architecture نیست.

فرض کن:

Patient
Owner
Appointment
Invoice
Payment

در هنگام initial sync تغییر می‌کنند.

اگر هر table جداگانه pull شود:

Patient snapshot = T1
Appointment snapshot = T2
Invoice snapshot = T3
Payment snapshot = T4

ممکن است client state از هیچ لحظه واقعی سیستم نباشد.

باید initial sync داشته باشی:

BEGIN SNAPSHOT
     ↓
SnapshotWatermark = N
     ↓
download all required state <= N
     ↓
apply local snapshot
     ↓
advance to N
     ↓
pull changes > N
     ↓
END

یا معادل آن با یک sync token چندscope‌ای.

Microsoft offline-first خودش incremental sync و local-first operation را تأیید می‌کند، اما architecture تو باید consistency mechanism مخصوص رابطه‌های چندموجودیتی را خودش تعریف کند.

12. Sync conflict model یک مفهوم دیگر کم دارد: resurrection

تو deletion را soft-delete کرده‌ای، ولی این scenario وجود دارد:

Device A:
  edits Patient X offline

Server:
  Patient X deleted

Device A reconnect:
  sends update to X

باید مشخص شود:

Reject?
Restore?
Create replacement?
Human resolution?

این صرفاً Version conflict نیست.

پیشنهاد:

DeletedAt
DeletionVersion
DeletionReason
DeletedBy

و update deleted aggregate یک business conflict مستقل باشد.

13. A-08 — Inventory convergence

این جمله غلط است:

two offline devices recording different movements always converge to a correct total

فقط وقتی درست است که movementها independent باشند.

مثلاً:

Server stock = 1

Device A offline:
  sell 1

Device B offline:
  sell 1

دو movement معتبر هستند، ولی آیا نتیجه:

-1 stock

مجاز است؟

یا باید یکی reject شود؟

این به policy بستگی دارد.

Microsoft Dynamics هم inventory را صرفاً یک عدد ساده نمی‌بیند؛ inventory transactionها physical/financial states، dimensions، batch/serial و settlement دارند.

بنابراین Inventory باید این موارد را explicitly تعریف کند:

Negative inventory allowed?
Reservation model?
Batch?
Expiry?
Serial?
Lot?
Warehouse/location?
Cost layer?
Concurrent reservation?
Transfer?
Return?
Stock adjustment?

برای دامپزشکی مخصوصاً:

expiry date
batch/lot
controlled drugs
serial numbers
stock location
cold-storage inventory

می‌تواند مهم شود.

14. A-09 — Appointment conflict

این:

version mismatch
→ conflict

برای scheduling کافی نیست.

فرض:

Room 1
09:00–10:00

Client A:
  books 09:30–10:30

Client B:
  books 09:30–10:30

ممکن است هر دو روی versionهای مختلف بخوانند و سپس write کنند.

حتی اگر version token داشته باشند، business invariant باید server-side در transaction enforce شود.

برای چنین invariants باید از یکی از این‌ها استفاده شود:

database uniqueness strategy
serializable transaction
application lock
resource partition lock
slot allocation table

SQL Server برای uniqueness constraint و application locks primitiveهای مشخص دارد.

پس Scheduling باید یک:

Resource Allocation Model

داشته باشد، نه فقط optimistic concurrency.

15. A-10 — Accounting model ناقص است

«Append-only ledger» به تنهایی Accounting Architecture نیست.

Microsoft General Ledger هم اساس را بر debit/credit entries و chart of accounts می‌گذارد.

حداقل باید architectural invariants این‌ها باشند:

Every posted journal:
    total debit = total credit

No posted journal mutation

Posting period must be open

Posting date != arbitrary client timestamp

Currency defined

Exchange rate versioned

Rounding policy defined

Tax calculation snapshot retained

Account dimensions immutable for posted entry

Source document reference retained

Reversal references original posting

Correction requires compensating entry

و:

PeriodClose

باید واقعاً یک state machine باشد:

Open
→ SoftClose
→ Review
→ Closed
→ Reopen by authorized correction

Dynamics 365 هم period closing را صرفاً یک boolean ساده نمی‌داند و قبل از close مراحل مختلف، posting controls و reconciliation دارد.

16. یک مشکل بسیار مهم: Offline + Period Close

این scenario اصلاً در architecture نیست:

Device offline on Sep 30

Accounting period:
Sep 30 → CLOSED

Device reconnects on Oct 3

Pending:
Invoice dated Sep 30
Payment dated Sep 30
Stock movement dated Sep 30

چه می‌شود؟

گزینه‌ها:

Reject
Post into Oct 3
Allow authorized back-posting
Hold for accountant
Create adjustment

این باید architecture-level policy باشد.

وگرنه "offline accounting" با period close در conflict است.

17. A-11 — Cross-context financial atomicity

این‌ها باید مشخص شوند:

Sale + Payment
Sale + Inventory reduction
Purchase receipt + payable
Payment + cash session
Refund + inventory return
Period close + pending commands

برای هرکدام باید بگویی:

Atomic
Eventually consistent
Compensating
Human approval

در حال حاضر همه زیر event-driven eventual consistency پنهان شده‌اند.

برای accounting این کافی نیست.

18. Command Queue

مدل کلی خوب است، ولی یک semantic اشتباه دارد:

Draft
→ LocallyPosted
→ PendingSettlement
→ Settled

برای accounting واژه LocallyPosted خطرناک است.

Posted باید server/accounting meaning داشته باشد.

بهتر:

Draft
→ LocallyCommitted
→ PendingSettlement
→ Settled

و:

SettlementStatus

را از:

BusinessLifecycleStatus

جدا کن.

همچنین:

Reversed

بهتر است state اصلی command نباشد؛ reversal خودش یک business command جدید است که به command قبلی reference دارد.

19. Dependency metadata اشتباه ترکیب شده

نوشته‌ای:

CausationId / DependsOnOperationId

این‌ها باید دو مفهوم کاملاً مستقل باشند:

CausationId
    چرا این command/event به وجود آمد؟

DependsOnOperationId
    قبل از این command کدام operation باید settle شود؟

و باید invariant اضافه کنی:

dependency graph = DAG

یعنی:

Op A → B → C

مجاز.

ولی:

A → B
B → C
C → A

باید قبل از اجرا detect شود.

20. OperationId خوب است، ولی UserId را نباید از payload قبول کنی

در command آمده:

TenantId
UserId
DeviceId

همان طور که TenantId را از client نباید قبول کرد، UserId و DeviceId هم نباید trusted باشند.

بهتر:

Client sends:
    OperationId
    CommandType
    AggregateId
    Payload

Server derives:
    TenantId
    UserId
    DeviceId
    AuthorizationContext

DeviceId هم باید به credential/device registration cryptographically bind باشد.

21. Optimistic concurrency کافی نیست

EF Core هم optimistic concurrency را برای detecting stale writes معرفی می‌کند، ولی خودش توضیح می‌دهد که isolation levels بالاتر بعضی guarantees اضافی دارند.

بنابراین:

VersionToken

برای:

Patient edit

مناسب است.

اما برای:

booking
stock reservation
cash close
period close
seat allocation
license/device registration

ممکن است کافی نباشد.

این‌ها business invariants هستند و باید transactionally enforce شوند.

22. §9 — Multi-tenancy

Shared DB/shared schema از نظر architecture ممکن است، و Microsoft هم isolation را spectrum می‌داند.

اما یک مشکل wording داری:

Cross-tenant access is architecturally impossible even if one layer is disabled

این ادعا با مدل فعلی قابل اثبات نیست.

مثلاً اگر:

Tenant filter accidentally disabled

و query application اشتباه باشد، auth layer لزوماً مانع leakage نمی‌شود.

یا:

IgnoreQueryFilters()
raw SQL
bulk update
stored procedure
report query
maintenance job
file query
cache

می‌توانند global filter را bypass کنند.

Microsoft خودش هم global query filters را convenience mechanism برای multi-tenancy معرفی می‌کند و حتی caveatهای required navigation و disabling filter را مستند کرده است.

پس معماری بهتر:
Layer 1:
  authenticated tenant context

Layer 2:
  tenant-scoped repository/query abstraction

Layer 3:
  DB RLS / equivalent hardening

Layer 4:
  adversarial isolation tests

و مشخصاً:

no raw database access outside approved tenant-aware infrastructure.

23. Tenant context باید immutable باشد

این:

ambient request context

خطر دارد اگر mutable باشد.

بهتر:

ResolvedTenantContext
    immutable
    created once at request boundary

و هیچ پایین‌دستی نباید بتواند:

TenantId = X → Y

را تغییر دهد.

این مخصوصاً در:

async continuations
background jobs
worker claims
outbox handlers
scheduled jobs

مهم است.

24. §10 — Authentication

معماری cookie برای browser و token برای desktop قابل دفاع است.

ولی:

bearer + refresh token

برای native application باید با flow استاندارد درست پیاده شود.

Microsoft برای desktop/native app استفاده از Authorization Code + PKCE را توصیه می‌کند.

RFC 8252 هم BCP صریحاً می‌گوید native app باید authorization request را با external user-agent، معمولاً browser، انجام دهد و public native client باید PKCE داشته باشد.

و RFC 9700 جدیدتر برای public clients refresh-token rotation یا sender-constraining را الزام/توصیه می‌کند.

بنابراین در STACK/architecture باید این contract باشد:

Desktop:
    Authorization Code
    + PKCE
    + OS secure storage
    + refresh-token rotation
    + device-bound key where appropriate

Web:
    server-side login
    + secure cookie
    + CSRF protection
25. A-14 — Cookie authentication بدون CSRF کامل نیست

این یکی صریحاً در architecture جا افتاده.

هر browser request که cookie را خودکار می‌فرستد می‌تواند در برابر CSRF آسیب‌پذیر باشد.

Microsoft هم anti-forgery را برای cookie-based authentication و mutationهای unsafe مستند کرده است.

باید architecture-level invariant اضافه شود:

All browser state-changing requests require anti-forgery protection.

همراه با:

Secure
HttpOnly
SameSite
Origin/Fetch metadata checks where appropriate
strict CORS

CORS خودش جای CSRF protection نیست. Microsoft صراحتاً این را توضیح داده است.

26. A-01 — Offline Session

اینجا تضاد جدی وجود دارد.

نوشته‌ای:

security-stamp/password-change invalidates cached offline sessions

اگر دستگاه واقعاً offline باشد، server نمی‌تواند session snapshot موجود را invalidate کند.

واقعیت:

Server changes role
       ↓
Offline device
       ↓
cannot know

پس چیزی که واقعاً می‌توانی بگویی:

Revocation takes effect:
    immediately online
    at next trusted contact offline

و اگر بخواهی قبل از آن invalid شود، باید mechanism دیگری وجود داشته باشد.

بنابراین عبارت invalidates باید تبدیل شود به:

invalidation is enforced at next synchronization/contact; no offline revocation is possible without an externally delivered revocation artifact.

این limitation ذاتی architecture است، bug نیست؛ ولی الان درست بیان نشده.

27. Long offline window و security policy

یک trade-off بنیادی داری:

Long offline window
        ↕
Fast revocation

هرچه offline window بزرگ‌تر:

stolen device
stolen credential
role revoked
employee terminated
subscription cancelled

مدت بیشتری ممکن است offline capability داشته باشد.

بنابراین maximum offline lifetime نباید فقط technical number باشد؛ باید برای هر capability risk classification داشته باشد.

این بخش خوب شروع شده، ولی باید به جدول واقعی تبدیل شود:

Capability	Offline?	Max age	Requires fresh auth?
View patient	Yes	X	No
Add clinical note	Yes	Y	Maybe
Sale	Yes	Y	Maybe
Refund	No/short	Z	Yes
Period close	No	0	Yes
Change roles	No	0	Yes
28. A-02 — License anti-cloning

این جمله:

installation-bound identity

به خودی خود کافی نیست.

اگر identity با software-only secret ذخیره شود:

copy disk
copy secret
copy license

ممکن است clone ساخته شود.

Microsoft برای Windows hardware-backed keys و TPM توضیح می‌دهد که private key می‌تواند non-migratable و hardware-bound باشد.

در macOS هم Secure Enclave می‌تواند private key را طوری نگه دارد که plaintext key اصلاً در application در دسترس نباشد و key به همان device bind شود.

پس بهتر:

Installation Identity
    ↓
generated on device
    ↓
private key non-exportable where supported
    ↓
public key registered with server
    ↓
license/capability bound to public key

نه:

machine fingerprint
+
encrypted local secret

که anti-cloning ضعیف‌تری دارد.

29. A-02 — Monotonic clock برای license

این قسمت:

verify against a MONOTONIC high-water clock

به شکلی که نوشته شده کافی نیست.

Windows performance/uptime counters زمان سپری‌شده از system start را می‌دهند؛ یعنی یک monotonic uptime counter، تقویم دائمی بین rebootها نیست. GetTickCount نیز صریحاً elapsed time since system startup است. QueryPerformanceFrequency هم counter frequency را در زمان boot مشخص می‌کند.

پس:

MonotonicClock

برای:

time elapsed while process/device remains running

مناسب است،

اما برای:

license expires on 2027-01-01

به تنهایی کافی نیست.

بهتر:
Server-issued trusted time
+
persisted high-water wall time
+
monotonic elapsed-time evidence
+
rollback detection
+
secure device state

و رفتار anomaly:

clock rollback detected
    → sensitive offline operations disabled
    → local evidence preserved
    → recovery requires server contact
30. A-13 — PKI architecture یک تناقض دارد

§10.5 می‌گوید:

CA private key lives ONLY on server/deployment authority

§17.3 می‌گوید:

full server backup includes cryptographic keys

این دو با هم قابل جمع نیستند.

اگر CA root key در full server backup باشد:

root CA private key
→ attacker who obtains server backup
→ can create trusted certificates
→ MITM all clients
معماری صحیح‌تر:
Offline Root CA
    ↓
Issuing CA
    ↓
Server certificate

Root private key:

NOT on production server
NOT in normal backup

Microsoft هم برای AD CS از offline root CA به عنوان الگوی افزایش حفاظت نام می‌برد.

31. Certificate revocation

R7 می‌گوید اینترنت نباید لازم باشد.

ولی X.509 chain validation می‌تواند برای revocation به network دسترسی داشته باشد؛ .NET هم Online و Offline revocation modes را جدا می‌کند و صراحتاً می‌گوید Online ممکن است live request انجام دهد.

بنابراین باید architecture مشخص کند:

Production runtime:
    RevocationMode = Offline

Local CRL:
    served internally

OR

Short-lived certificates + controlled renewal

و certificate validation نباید accidentally به CRL/OCSP اینترنتی وابسته شود.

32. §11 — Data Architecture
Accounting خوب شروع شده ولی ناقص است

Append-only صحیح است.

اما برای accounting باید علاوه بر immutability داشته باشی:

Journal
JournalLine
PostingBatch
Account
FiscalPeriod
AccountingDate
DocumentDate
Currency
ExchangeRate
Tax
Dimensions
ReversalReference
SourceDocument
PostingUser
PostingTimestamp

و invariant:

sum(debits) == sum(credits)
33. Inventory

Movement-based مدل خوبی است؛ Microsoft Dynamics هم inventory را بر اساس transactionها، physical/financial state، settlement و dimensionها مدیریت می‌کند.

اما inventory movement به معنای event sourcing کامل نیست.

حداقل باید مشخص شود:

quantity
uom
base uom
conversion
lot
expiry
serial
location
cost
valuation method
reservation
availability
negative inventory

به‌خصوص cost layer مهم است. Microsoft حتی برای FIFO settlement بین issue و receipt مدل صریح دارد.

34. §12 — Outbox

این تصمیم درست است.

Transactional Outbox واقعاً برای atomically storing business state + event مناسب است. Microsoft همین مدل را توضیح می‌دهد: transaction state و outbox event را با هم commit و سپس worker آنها را relay می‌کند.

اما state machine تو ناقص است.

فعلاً:

Pending
→ Claimed
→ Processing
→ Succeeded / Retry

لازم داری:

Pending
Claimed
Processing
RetryScheduled
Succeeded
DeadLetter
Cancelled
Poison

همراه:

LeaseExpiry
AttemptCount
LastException
NextAttemptAt
FirstFailedAt
LastFailedAt
DeadLetterReason
35. Competing Consumers + Ordering

Microsoft خودش می‌گوید Competing Consumers برای independent tasks مناسب است و اگر tasks باید sequence خاصی داشته باشند، مناسب نیست مگر سازوکار ordering داشته باشی.

پس این invariant باید اضافه شود:

WorkType
    → concurrency class
    → ordering key
    → retry policy
    → dead-letter policy

مثلاً:

TenantId + AggregateId

می‌تواند ordering partition باشد.

36. Circuit Breaker در Outbox launch تقریباً بی‌معناست

در §12.3 نوشته‌ای:

circuit breaker on chronically failing downstream effect

ولی R7 می‌گوید:

No external runtime dependency

و launch:

single process
single database
no broker

پس بسیاری از outbox effects در واقع local هستند.

Microsoft هم توضیح می‌دهد Circuit Breaker عمدتاً برای remote dependency مناسب است و برای message-driven یا local resource می‌تواند مناسب نباشد.

برای launch:

Retry
Backoff
Dead-letter
Operator remediation

از Circuit Breaker مهم‌تر است.

37. Retry storm

این قسمت خوب است که jitter گذاشته‌ای.

این کاملاً با guidance Microsoft سازگار است؛ retryهای مکرر می‌توانند outage را بدتر کنند و retry storm/thundering herd بسازند.

ولی باید مشخص شود:

retry budget
max attempts
max elapsed time
jitter distribution
per-tenant reconnect limit
global reconnect limit

صرفاً «exponential backoff» کافی نیست.

38. §12.4 — Bulkhead در یک process

اینجا یک تناقض معماری داری.

نوشته‌ای:

worker threads isolated from request handling

اما اگر همه داخل:

one process
one runtime
one memory space
one OS scheduler

باشند، thread-count isolation به معنای واقعی Bulkhead نیست.

مثلاً:

500 file uploads
+
200 reports
+
100 sync workers

می‌توانند:

CPU
memory
DB connections
disk I/O
bandwidth

را تمام کنند.

Microsoft Bulkhead را به عنوان isolation into pools تعریف می‌کند، ولی تو باید pool resource واقعی تعریف کنی.

برای single node پیشنهاد:

Interactive request budget
Sync budget
File-transfer budget
Report budget
Maintenance budget

و برای هرکدام:

max concurrency
queue depth
CPU budget
DB concurrency
rate limit
39. §9 + §16 — Noisy Neighbor

این یکی بسیار مهم است و الان کم‌رنگ است.

Shared infrastructure یعنی:

Clinic A
  massive report
  huge file import
  reconnect storm
       ↓
Server saturated
       ↓
Clinic B degraded

Microsoft این را دقیقاً Noisy Neighbor antipattern می‌نامد.

Deployment Stamp راه‌حل بعد از وقوع مشکل است.

برای launch هم باید حداقل داشته باشی:

Per-tenant request rate
Per-tenant background concurrency
Per-tenant storage quota
Per-tenant sync bandwidth
Per-tenant report concurrency
Per-tenant file upload limits

Throttling باید resource-cost-aware باشد، نه فقط requests/second. Microsoft هم همین نکته را صراحتاً مطرح می‌کند.

40. §11.5 — Reporting isolation ناقص

گفتن:

Heavy reporting has its own budget

کافی نیست.

اگر report همچنان:

same database
same CPU
same disk
same buffer cache
same connection pool

را مصرف کند، budget نرم است.

باید حداقل:

Report query timeout
Max report concurrency
Max rows/scans
Cancellation
Dedicated connection pool
Read-only transaction
Snapshot isolation strategy

داشته باشی.

و بعداً:

read replica / reporting DB

به عنوان escalation.

41. §14 — Files

مدل دو consistency domain درست است.

اما چهار مورد مهم نیست:

1. File hash نباید File identity باشد

بهتر:

FileId = random opaque identifier

ContentHash = SHA-256(...)

نه:

File path = content hash

چون deduplication جهانی می‌تواند cross-tenant side channel درست کند.

2. Quarantine باید security gate باشد

الان:

Uploaded
→ HashVerified
→ Committed
→ Available

برای فایل خارجی بهتر:

Staged
→ Uploaded
→ HashVerified
→ MalwareScanned
→ ContentValidated
→ Committed
→ Available
3. Backup consistency

DB metadata و binary backup باید یک recovery point سازگار داشته باشند.

این الان مشخص نشده.

4. Storage exhaustion

روی single node:

disk full

می‌تواند باعث شود:

file store
DB
logs
outbox
backups

همزمان fail شوند.

باید storage quota و low-water/high-water alarms داشته باشی.

42. Valet Key

Valet Key از نظر اصول درست است و Microsoft آن را برای direct limited access به resource تعریف می‌کند.

اما در معماری فعلی file store روی همان node است.

پس باید تصمیم بگیری:

Valet key for separate storage

یا:

application-mediated streaming

اگر File Store واقعاً filesystem محلی است، Valet Key پیچیدگی بیشتری ایجاد می‌کند؛ البته همچنان می‌توان آن را با signed access token روی file endpoint پیاده کرد.

43. §15 — Observability

جهت‌گیری خوب است.

اما یک ایراد امنیتی:

Diagnostics bundle
    = logs + config snapshot + sync state

اگر config snapshot شامل:

DB credentials
certificate private key
license signing key
connection strings
JWT signing secret
DataProtection key

شود، diagnostics bundle خودش تبدیل به credential dump می‌شود.

Microsoft صریحاً تأکید می‌کند secrets و sensitive data نباید در logs قرار بگیرند.

پس:

Diagnostics Bundle
    secrets = NEVER
    credentials = NEVER
    private keys = NEVER

و باید redaction policy معماری باشد.

44. Audit Log

این:

append-only audit log

به معنی tamper-proof نیست.

اگر:

database admin
vendor root operator
malware

دسترسی کامل DB داشته باشند، append-only application table را می‌توان تغییر داد.

برای audit قوی‌تر:

Audit Record
    ↓
hash chain / signed batches
    ↓
separate audit key
    ↓
restricted writer
    ↓
periodic immutable export

یا حداقل:

Audit DB/schema separate security boundary

باشد.

45. Server timestamp

Microsoft WAF برای security audit هم توصیه می‌کند eventها server-side timestamp شوند.

بنابراین در audit:

ClientCreatedAt

می‌تواند نگه داشته شود، ولی canonical timestamp باید:

ServerObservedAt

باشد.

46. §13 — Subscription / Licensing

از نظر conceptual separation خوب است:

Subscription != Licensing

اما یک سؤال بنیادی هنوز جواب ندارد:

"installation" دقیقاً چیست؟

آیا license به:

server deployment
desktop installation
device
user
tenant

bind شده؟

در SaaS با یک server مرکزی، این پنج مفهوم متفاوت‌اند.

من پیشنهاد می‌کنم:

Subscription
    tenant-level commercial entitlement

DeviceRegistration
    one physical client installation

OfflineCapabilityGrant
    short-lived signed capability for one device

ServerLicense
    vendor deployment entitlement, if needed

این چهار مفهوم را جدا کن.

47. License signing key نباید داخل runtime server باشد

اگر:

server
+
license signing private key

با هم باشند و server compromise شود:

attacker
→ steals signing key
→ issues valid licenses

پس:

Runtime Server:
    public verification keys only

License Issuer:
    protected offline signing key

این یک security architecture مهم است.

48. Subscription website

بخش:

website → onboarding intent

خوب است ولی security contract کم است.

باید داشته باشی:

signed webhook/message
issuer identity
audience
contract version
idempotency key
timestamp
expiration
replay protection
signature verification
payment reference
correlation ID

و lifecycle:

Paid
→ Provisioned
→ Active
→ RenewalPending
→ Renewed
→ Grace
→ Suspended
→ Cancelled

همچنین:

refund
chargeback
payment dispute

باید اثر entitlement را مشخص کند.

49. A-03 — مشکل واقعی Zero-Internet و Website

این از نظر معماری یکی از بزرگ‌ترین gapهاست.

فرض:

Internet OFF

در همان لحظه:

customer buys
website creates onboarding

چطور به central server برسد؟

در متن نوشته‌ای:

async, out-of-band onboarding contract

ولی out-of-band transport تعریف نشده.

باید یکی از این‌ها وجود داشته باشد:

مدل پیشنهادی
Public Website
     ↓
Signed Provisioning Package
     ↓
Operator/Admin workstation
     ↓ USB / controlled transfer
     ↓
Animora Server

یا:

Website
 ↓
Vendor Provisioning Queue
 ↓
Controlled management connection
 ↓
Animora

که management plane است و باید صریحاً خارج از runtime plane تعریف شود.

50. §17 — Deployment

اینجا معماری single-node است، ولی از نظر Reliability یک واقعیت باید صریح نوشته شود:

single node
=
single point of failure

Microsoft WAF مستقیماً redundancy و حذف SPoF را یکی از اصول reliability می‌داند.

این به معنی آن نیست که single-node برای launch غلط است؛ ممکن است تصمیم هزینه/محصول باشد.

اما باید صریح باشد:

RTO during total node failure = X
RPO = Y

بدون این اعداد نمی‌توانیم بگوییم architecture reliable است.

51. Backup فقط «داشتن backup» نیست

باید architecture این را تعریف کند:

Production
   ↓
Backup copy #1
   ↓
Backup copy #2

و حداقل یکی:

off-host
isolated
immutable

باشد.

Microsoft برای ransomware-resilient architecture هم روی immutable/independent recovery data و تست restore تأکید می‌کند.

و:

Backup exists

بدون:

Restore succeeded

ارزش محدودی دارد.

52. A-04 — RTO/RPO باید در Architecture Drivers بیاید

الان داری:

performance
offline
security

ولی برای یک SaaS accounting باید حتماً:

Availability SLO
RTO
RPO
Maximum data loss
Maximum offline command age
Maximum outage before client lock

هم تعریف شود.

Microsoft WAF هم توصیه می‌کند SLO/RTO/RPO مشخص شوند و recovery بر اساس آن‌ها طراحی و آزمایش شود.

53. Client disk loss — یک تناقض مهم

§17.3 می‌گوید:

Client disk loss ← server-driven resync + pending-command-recovery policy

ولی اگر command هنوز به server نرفته باشد:

command exists only on lost disk

پس server اصلاً آن را ندارد.

بنابراین resync نمی‌تواند آن command را recover کند.

باید صریحاً تعیین شود:

Unsent command
    = recoverable?

اگر بله:

encrypted local backup

لازم است.

اگر نه:

user-facing warning:
"not synchronized / unrecoverable after local disk loss"

لازم است.

54. Device stolen

متن می‌گوید:

stolen device ← revocation ← next contact

اما attacker می‌تواند:

steal device
disconnect network
continue offline

تا زمانی که offline capability expire شود.

پس عبارتش باید بشود:

Stolen device:
    server revocation takes effect at next contact;
    meanwhile cached offline capability remains a risk bounded by
    its expiry and local device protection.

این trade-off باید به customer risk model گفته شود.

55. §18 — Versioning

ایده mixed fleet خوب است، ولی:

N/N-1/N-2 bidirectional

بیش از حد سخت است.

من توصیه می‌کنم:

Server N
supports Client N, N-1, N-2

Client N
requires Server >= minimum compatible server

و نه الزاماً:

New client must work with old server

چون این هزینه API/schema compatibility را چند برابر می‌کند.

برای rollback server باید migration strategy داشته باشی:

Expand
→ Deploy compatible code
→ Migrate
→ Switch
→ Contract

و نه:

deploy schema
→ if failed restore old executable
56. Sync versioning باید Capability Negotiation هم داشته باشد

صرفاً:

ContractVersion = 4

کافی نیست.

باید server/client بتوانند اعلام کنند:

Capabilities:
    appointment-v2
    inventory-lots
    offline-refund
    tax-v3

تا mixed fleet feature activation قابل کنترل باشد.

57. §19 — Fitness Gates

این قسمت از بهترین قسمت‌های سند است.

اما چند gate ضروری کم است.

پیشنهاد می‌کنم اضافه شوند:

W — Period Close Race
Device offline
+
period closes
+
old command reconnects
X — Client Disk Loss
unsent commands
+
disk loss
Y — Delete Resurrection
offline update
+
server delete
+
reconnect
Z — Initial Sync Consistency
multi-table concurrent writes
+
initial snapshot
AA — Operator Cross-Tenant
operator role
+
tenant impersonation
+
cross-tenant file
+
cache
+
report
AB — Backup Restore Consistency
DB backup
+
file backup
+
key restore
+
server restart
AC — License Key Compromise
server compromise
→ license signing key remains unavailable
AD — Poison Outbox
permanent event failure
→ bounded retry
→ dead-letter
→ operator repair
AE — Resource Starvation
one tenant
→ max reports
→ max uploads
→ max sync
→ other tenants remain within SLO
58. §20 — ADRها

تعداد ADRها خوب است، ولی تصمیم‌های زیر هنوز ADR جدا می‌خواهند:

ADR-16
Offline authentication threat model

ADR-17
Tombstone retention

ADR-18
Sync snapshot consistency

ADR-19
Inventory concurrency semantics

ADR-20
Scheduling allocation semantics

ADR-21
Cross-context financial transactions

ADR-22
PKI hierarchy and root-key custody

ADR-23
License-signing key custody

ADR-24
Offline subscription provisioning

ADR-25
Backup topology / RPO / RTO

ADR-26
Audit tamper evidence

ADR-27
Client disk loss semantics

این‌ها از جنس «implementation detail» نیستند؛ architecture decisions واقعی‌اند.

59. §21 — AI Agent Rules

Rules بسیار خوب هستند، ولی دو rule باید اصلاح شوند.

A-RULE 009

فعلاً:

UpdatedAt is delta-cursor basis only

باید:

Opaque server-issued ChangeCursor is the only synchronization cursor; UpdatedAt is informational.

A-RULE 030

فعلاً:

installation-binding mandatory

بهتر:

Installation binding MUST be based on a protected device identity; software-only machine fingerprints are not accepted as anti-cloning proof.

60. یک Rule جدید بسیار مهم

اضافه کن:

A-RULE 036

Client-originated TenantId, UserId, DeviceId, Role, Entitlement,
AccountingDate, AuthorizationDecision, or EffectivePostingState
must never be treated as authoritative server state.

این rule خیلی از attackهای آینده را جلوگیری می‌کند.

61. یک Rule دیگر
A-RULE 037

Every supported offline duration must have corresponding tombstone,
authorization-revocation, command-retention, and licensing-retention
durations that are at least as long as the supported offline window,
plus an explicit safety margin.

این یکی برای Offline-first فوق‌العاده مهم است.

62. یک Rule دیگر برای Accounting
A-RULE 038

A client-local financial state may be provisional, but no UI state,
event, report, or local label may represent it as globally posted,
settled, closed, or authoritative until server settlement succeeds.
63. §23 — Pattern Catalog

لیست patternها در کل خوب است، ولی یک مشکل دارند: بعضی را بیش از حد به کار برده‌ای.

واقعاً مناسب
Transactional Outbox       ✓
Idempotent Consumer        ✓
Competing Consumers        ✓ with ordering caveat
Retry                      ✓
Throttling                 ✓
Deployment Stamps          ✓ future
Cache-Aside                ✓ if cache exists
Valet Key                  ✓ depending on file architecture
نیاز به بازنگری
Circuit Breaker

برای local launch dependencies احتمالاً unnecessary است. Microsoft خودش محدودیت‌های استفاده از آن را توضیح می‌دهد.

همچنین:

"applied as-is"

را حذف کن.

بهتر:

adapted to Animora's deployment and reliability constraints

چون Azure Architecture Center patternها context-specific هستند.

64. چیزی که معماری هنوز به اندازه کافی روی آن تأکید نکرده: Data Classification

این سیستم فقط accounting نیست؛ داده دارد:

owner identity
contact information
animal medical history
clinical documents
financial transactions
payment records
audit
credentials
license/device identity

Microsoft WAF توصیه می‌کند data classification پیش از تصمیم امنیتی انجام شود و classification روی retention، storage و protection اثر بگذارد.

بنابراین باید taxonomy داشته باشی:

Public
Internal
Tenant Confidential
Clinical Sensitive
Financial Sensitive
Security Secret
Cryptographic Key

و هرکدام:

encryption
retention
logging
backup
access
deletion/pseudonymization

خاص خودشان را داشته باشند.

65. Server-side encryption اصلاً کافی مشخص نشده

می‌گویی:

client local store encrypted

ولی برای server:

DB encryption at rest?
file encryption?
backup encryption?
key encryption?
log encryption?

مشخص نیست.

Microsoft security guidance روی encryption مطابق data classification تأکید دارد.

برای single-node vendor-hosted server، من حداقل این را معماری می‌دانم:

Disk encryption
+
DB encryption where available
+
encrypted backups
+
separate backup encryption key
+
key rotation/recovery procedure
66. Key management باید خودش یک معماری مستقل باشد

الان چند key مختلف داری ولی همه به صورت پراکنده آمده‌اند:

TLS CA keys
Server certificate
Data Protection keys
License signing key
Offline capability signing key
File access grant signing key
Device identity keys
Database encryption keys
Backup encryption keys

این‌ها نباید یک «Crypto section» مبهم باشند.

حداقل باید:

Key
Owner
Purpose
Where generated
Where stored
Who can use
Can export?
Rotation
Revocation
Backup
Recovery
Compromise procedure

داشته باشند.

Microsoft هم برای secrets روی least privilege، rotation، auditing و جداسازی نقش user/admin/auditor تأکید می‌کند.

67. Single Server یعنی Key Custody خطرناک‌تر است

چون:

App
DB
File store
CA
Licensing
Audit

همه روی یک ماشین‌اند.

اگر root credential آن ماشین compromise شود، blast radius بسیار بزرگ است.

بنابراین حتی اگر compute single-node باشد، لازم نیست trust domain هم single-node باشد.

بهتر:

Server
  ├── app
  ├── db
  ├── files
  └── public verification keys

Outside runtime:
  ├── Root CA key
  ├── license signing key
  └── backup recovery keys
68. §15 Health Checks

Liveness/readiness تفکیک خوب است و Microsoft هم همین distinction را مستند می‌کند.

اما endpointهای health نباید روی public/clinic network اطلاعات حساس بدهند.

بهتر:

/health/live
    minimal

/health/ready
    management network only

/health/deep
    authenticated operator only
69. Rate limiting

در architecture فقط reconnect throttling آمده است.

باید این‌ها را هم داشته باشی:

Authentication attempts
Password reset
Device registration
Command submission
Sync
File upload
Report generation
Search/typeahead
Operator APIs
Provisioning API

ASP.NET Core rate limiting middleware خودش partitioning, metrics و load testing را پشتیبانی می‌کند، و Microsoft تأکید دارد rate limits باید با workload واقعی stress-test شوند.

70. یک مشکل با "static-first web rendering"

برای server ضعیف خوب است، ولی اگر web UI شامل interactive server components باشد، باید در capacity model حساب شود:

connected sessions
session memory
reconnect frequency
circuit/state
websocket/HTTP connection
server-side timers

نباید فرض کرد:

browser = تقریباً zero server cost

اگر rendering واقعاً stateless server-rendered باشد، عالی است.

اگر server-side interactive باشد، کاملاً متفاوت است.

71. Scale-out escape hatch ناقص است

نوشته:

2+ instances
→ session affinity + backplane

برای scale-out واقعی باید حداقل این‌ها هم بررسی شوند:

Data Protection shared key ring
Session state
Cache
SignalR / realtime
File access
Background worker leadership
Outbox leases
Database concurrency
Health routing
Connection draining
Deployment coordination

Microsoft هم برای web farm روی shared Data Protection key ring و shared caching تأکید می‌کند.

پس "add a backplane" به تنهایی escape hatch کامل نیست.

72. §19 — Gate برای Cache

Gate L خوب است، ولی فقط key composition را تست نکن.

باید این‌ها تست شوند:

permission change
role change
tenant switch
subscription change
cache expiry
stale authorization
logout/login as different user

چون:

Tenant + User + Resource + Version

اگر permission بعداً تغییر کند ممکن است کافی نباشد.

73. مسئله Cache Invalidation

Cache-Aside pattern خودش consistency problem دارد؛ Microsoft هم صریحاً می‌گوید cached data همیشه با source data همگام نیست و باید stale detection/refresh strategy وجود داشته باشد.

برای financial output بهتر است:

financial authoritative views
    no long-lived cache

و برای:

reference data
catalog
UI settings

cache allowed.

74. Backup + File Store

معماری باید یک Recovery Point Manifest داشته باشد:

BackupSetId
DatabaseBackupVersion
FileStoreSnapshotVersion
KeySetVersion
LicenseKeyVersion
ServerBuildVersion
SchemaVersion

تا معلوم باشد یک restore point کامل دقیقاً چه چیزی است.

75. مهم‌ترین اصلاحی که برای Sync پیشنهاد می‌کنم

اگر بخواهم فقط یک بخش architecture را بازنویسی کنم، §8 را این‌طور می‌کنم:

LOCAL STATE
    ↓
Local transaction
    ↓
Pending command / local mutation
    ↓
SYNC SESSION
    ├── upload commands
    ├── upload replicated changes
    ├── establish server snapshot watermark
    ├── download snapshot pages
    ├── download post-snapshot changes
    └── reconcile provisional state

و server:

Server Change Log
    ChangeSequence
    TenantId
    EntityType
    EntityId
    Operation
    Version
    CommitPosition

نه UpdatedAt به عنوان cursor.

76. معماری پیشنهادی اصلاح‌شده برای Sync
                    ┌────────────────────┐
                    │ Server Change Log  │
                    │ monotonic cursor   │
                    └─────────┬──────────┘
                              │
                       Pull changes
                              │
Desktop ──────────────────────┼─────────────────
  │                           │
  │ Local DB                  │
  │                           │
  ├── Replicated State        │
  │                           │
  ├── Pending Commands ───────┼──► Business API
  │                           │
  └── Provisional State       │
                              │
                        Server Transaction
                              │
                 ┌────────────┴─────────────┐
                 │ authorization             │
                 │ preconditions             │
                 │ domain invariants         │
                 │ idempotency               │
                 │ posting                   │
                 │ outbox                    │
                 └───────────────────────────┘
77. Architecture scorecard فعلی
حوزه	ارزیابی
Modular Monolith	عالی
Clean Architecture	خوب
C4 documentation	خوب
DDD decomposition	خوب ولی shared DB خطرناک
Offline-first direction	خوب
State vs intent split	بسیار خوب
Command idempotency	بسیار خوب
Financial immutability	خوب ولی accounting model ناقص
Inventory	خوب ولی convergence claim غلط
Scheduling	ناقص
Multi-tenancy	خوب ولی security claims بیش‌ازحد
Authentication	نیازمند اصلاح جدی
Offline auth	ناقص/بحرانی
Licensing	ناقص/بحرانی
PKI	نیازمند redesign
Outbox	خوب
Worker reliability	نیازمند DLQ/lease/resource model
File lifecycle	خوب ولی security/backup gaps
Observability	خوب
Reporting	ناقص از نظر resource isolation
Single-node deployment	قابل قبول برای launch، نه بدون RTO/RPO
Disaster recovery	ناقص
Versioning	بیش از حد ambitious و ناقص
Fitness functions	یکی از قوی‌ترین بخش‌ها
78. ترتیب اصلاح قبل از coding

من این پروژه را در این ترتیب جلو می‌بردم:

Phase 0 — معماری‌های حل‌نشده

اول:

1. Offline security threat model
2. RTO/RPO/SLO
3. Sync cursor + snapshot protocol
4. Tombstone retention
5. Inventory consistency model
6. Scheduling allocation model
7. Accounting posting model
8. Cross-context financial transaction model
9. PKI/key custody
10. Offline provisioning

تا این‌ها حل نشوند، coding sync/accounting اشتباه است.

Phase 1 — Kernel invariants
Tenant isolation
Auth
Device identity
OperationId
Idempotency
Versioning
Transaction boundaries
Ledger invariants
Phase 2 — Offline substrate
Local DB
encrypted storage
device key
replicated-state protocol
command queue
reconnect
conflict handling
Phase 3 — Finance/Inventory/Scheduling

به همین ترتیب و نه برعکس.

Phase 4 — Scale / reporting / file optimization

بعداً.

79. یک نسخه اصلاح‌شده از «Spine» معماری

به‌نظر من spine واقعی Animora باید این باشد:

                    ANIMORA
                       │
           ┌───────────┴────────────┐
           │                        │
     Replicated State          Business Intent
           │                        │
     Change Log/Cursor          Command Contract
           │                        │
       Client DB              Idempotency
           │                        │
      Conflict Policy        Preconditions
           │                        │
       Reconciliation       Domain Transaction
           │                        │
           └──────────┬─────────────┘
                      │
               Server Authority
                      │
          ┌───────────┼────────────┐
          │           │            │
       Finance    Inventory    Scheduling
          │           │            │
          └────── business invariants ──────┘
                      │
                  Outbox
                      │
              Integration Events
                      │
          Reporting / Audit / Projections

و امنیت:

Identity
   +
Device Identity
   +
Authorization
   +
Entitlement
   +
Tenant Context

جدا از business synchronization بماند.

80. نتیجه نهایی من

این معماری از پایه بد نیست؛ برعکس، spine آن از بسیاری از معماری‌های SaaS که دیده می‌شود بالغ‌تر است.

خصوصاً این تصمیم‌ها را نگه می‌دارم:

Modular Monolith
Clean Architecture
DDD-lite
Server authority
State replication ≠ business intent
Command idempotency
Append-only financial facts
Movement-based inventory
Transactional Outbox
Local observability
Gated scale-out

اما در همین شکل implementation را شروع نمی‌کنم.

چهار نقطه‌ای که بیشترین احتمال دارد بعداً پروژه را به بن‌بست برسانند عبارت‌اند از:

1. Offline security / licensing
2. Sync correctness
3. Cross-context accounting consistency
4. Single-node disaster recovery

و بعد از آنها:

5. Inventory/scheduling concurrency
6. Tenant isolation enforcement
7. Key/PKI custody
8. Resource/noisy-neighbor isolation

از نظر Microsoft هم جهت کلی architecture قابل دفاع است: Clean Architecture و multitenancy model انتخاب‌شده قابل استفاده‌اند، Offline-first با local store و queued synchronization الگوی شناخته‌شده‌ای است، Outbox/Idempotent Consumer مناسب‌اند، و WAF تأکید می‌کند که reliability باید با redundancy، RTO/RPO، recovery و testing اثبات شود—not merely documented.

اما 15 مورد Blocking بالا باید قبل از code freeze به تصمیم معماری تبدیل شوند.

یک نکته مهم هم باقی می‌ماند: چون STACK.md را در این پیام ندارم، عمداً وارد حکم نسخه/کتابخانه/DB/desktop framework نشده‌ام؛ این بررسی architecture-level است. برای مثال، اینکه آیا انتخاب واقعی DB شما می‌تواند snapshot/change cursor، RLS، indexing، locking و restore model موردنیاز را تحمل کند، وابسته به STACK است.

منابع اصلی مورد استفاده

برای provenance معماری از منابع رسمی C4 و arc42 استفاده کردم؛ برای بخش Microsoft از Microsoft Learn شامل WAF، multitenancy، ASP.NET Core، EF Core، SQL Server، Azure Architecture Center و Dynamics 365؛ و برای OAuth/crypto از RFC Editor و مستندات رسمی Apple/Microsoft استفاده کردم.