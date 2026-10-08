Animora — Deep Architecture Review
تاریخ بازبینی: 2026-10-08
1. نتیجه نهایی

ارزیابی من از سند فعلی:

محور	وضعیت فعلی
انتخاب Modular Monolith	9.5/10
Clean/Onion + DDD-lite	9/10
Multi-tenancy	7.5/10
Offline-first	8/10
Sync correctness	6.5/10
Financial correctness	8/10
Authentication / Authorization	8/10
Licensing	7.5/10
Reliability / Recovery	8/10
Performance strategy	8/10
Web architecture	7.5/10
Operations / Deployment	7/10
SMS / External integrations	5/10
Product-domain completeness	7/10

امتیاز کلی معماری فعلی: حدود 8/10.

نکته مهم این است که این 8/10 به معنی «معماری بد» نیست؛ برعکس، سند فعلی از نظر تفکیک مفاهیم مثل state replication ≠ business intent، authentication ≠ authorization ≠ entitlement و idempotency ≠ ordering بسیار خوب نوشته شده است.

مشکل اینجاست که چند تصمیم بسیار مهم هنوز از نظر semantics کامل نشده‌اند و بعضی تصمیم‌ها حتی با یکدیگر تناقض دارند. مهم‌ترینشان Sync، رفتار چند دستگاه یک کلینیک هنگام قطع ارتباط، SMS، مرزهای Client/Server Domain، ترتیب commit در ChangeSequence و الگوی Web Scale-out است.

Microsoft خودش برای applicationهای non-trivial، معماری monolithic همراه با separation of concerns و Clean Architecture را کاملاً قابل قبول می‌داند و حتی در راهنمای .NET تأکید می‌کند که یک monolith در بسیاری از workloadها از مجموعه‌ای از سرویس‌های جدا ساده‌تر و قابل‌مدیریت‌تر است.

بنابراین:

من Modular Monolith را حفظ می‌کنم.

Microservices در این مقیاس نه تنها ضروری نیست، بلکه احتمالاً اشتباه است.

2. بزرگ‌ترین ایرادهای معماری فعلی
P0 — باید قبل از تثبیت معماری اصلاح شود
P0-1 — تعریف «Offline» هنوز کامل نیست

در سند گفته شده هر Desktop یک replica محلی دارد و هنگام عدم دسترسی به server می‌تواند کار کند.

اما فرض کن یک کلینیک چهار کاربر دارد:

Clinic A
  PC-1   local replica
  PC-2   local replica
  PC-3   local replica
  PC-4   local replica
       X
 Central Server unreachable

در این حالت چهار سیستم دیگر یک source of truth مشترک ندارند.

مثلاً:

Stock = 1

PC1 offline → sells 1
PC2 offline → sells 1

هر دو locally valid هستند.

بعد از reconnect:

1 - 1 - 1 = -1

این موضوع برای inventory با policy قابل‌حل است، اما برای مواردی مثل:

نوبت
صندوق
capacity
اتاق
boarding
موجودی دارو
شماره فاکتور
sequenceهای مالی

موضوع بسیار جدی‌تر می‌شود.

Microsoft نیز offline-first را دقیقاً بر اساس همین مدل می‌سازد: هر device یک local database دارد و تغییرات بعداً sync می‌شوند؛ تغییرات امنیتی و privilege هم تا sync بعدی روی device ممکن است معتبر بمانند.

بنابراین باید یک تصمیم محصولی کاملاً صریح اضافه شود:

مدل A — Offline Device

هر سیستم می‌تواند مستقل کار کند.

در این مدل:

Conflict قابل قبول است.
همه چیز provisional است.
scheduling/stock/cash دارای policy تسویه هستند.
دو سیستم یک کلینیک ممکن است موقتاً واقعیت متفاوتی ببینند.

این مدل ساده‌تر و ارزان‌تر است.

مدل B — Offline Clinic Edge

هر کلینیک یک Local Coordinator دارد:

                Central Server
                     |
                 Internet
                     |
             Clinic Edge Node
            /      |       \
          PC1     PC2      PC3

در این مدل حتی اگر WAN/Internet قطع شود:

PC1 ↔ Edge ↔ PC2

و Edge برای آن clinic source of truth محلی است.

این مدل می‌تواند:

appointment allocation
inventory
cashier
local file access
intra-clinic consistency

را بسیار بهتر تضمین کند.

اما هزینه deployment و support را بالا می‌برد.

توصیه من

برای نسخه اول Clinic Edge نساز مگر اینکه requirement محصول واقعاً این باشد که چهار دستگاه یک clinic در هنگام قطع ارتباط با central server همچنان باید با یکدیگر strongly coordinated باشند.

اما این را در معماری به شکل یک explicit product boundary ثبت کن:

Offline-first does not imply intra-clinic strong consistency while the central authority is unreachable.

این جمله باید یکی از مهم‌ترین Invariantهای سیستم شود.

3. P0 — ایراد جدی در ChangeSequence

این بخش از سند از نظر مفهومی عالی است:

UpdatedAt نباید cursor باشد و cursor باید monotonic باشد.

اما یک خطر مهم هنوز حل نشده است:

صرفاً Database Sequence کافی نیست

مثلاً:

Transaction A
  allocate sequence = 100
  not committed yet

Transaction B
  allocate sequence = 101
  commit

Initial sync:
  sees 101
  cursor = 101

Transaction A:
  commit

اکنون رکورد A دارای ChangeSequence=100 است ولی client قبلاً cursor را روی 101 گذاشته است.

نتیجه:

Change 100 can be permanently skipped

بنابراین شرط معماری باید این باشد:

ChangeSequence باید یک commit-safe monotonic watermark باشد، نه صرفاً یک auto-increment number.

راه‌حل پیشنهادی

برای هر Tenant یک change counter داشته باش:

TenantChangeState
    TenantId
    CurrentVersion

و write transaction در همان transaction:

lock tenant change state
increment version
write business row
write change-log row
commit

به این ترتیب تخصیص version به commit order متصل می‌شود.

برای initial sync:

BEGIN REPEATABLE READ

N = current TenantChangeVersion

read all required tables
read changes <= N

COMMIT

client cursor = N

و بعد:

pull where ChangeSequence > N

این طراحی از نظر correctness بسیار قابل‌دفاع‌تر است.

4. P0 — دو Queue کاملاً مستقل، کمی بیش از حد مستقل شده‌اند

ایده:

Replication Queue
Business Command Queue

در اصل درست است.

اما یک مشکل دارید.

فرض کن:

Op1 = Create Patient
Op2 = Book Appointment(PatientId)

اگر Op1 از pipeline replication رفته باشد و Op2 از business-command pipeline، اکنون dependency بین دو transport وجود دارد.

در معماری فعلی dependency عملاً برای command → command تعریف شده ولی برای:

Replication operation → Business command

کامل تعریف نشده است.

معماری بهتر

در client یک:

Local Operation Journal

داشته باش.

داخل آن operationها typed هستند:

StateMutation
BusinessCommand
FileOperation

ولی transport handler آنها متفاوت است.

مثلاً:

Local Operation Journal
        |
        +---- Entity Mutation Handler
        |
        +---- Business Command Handler
        |
        +---- File Transfer Handler

هر operation:

OperationId
OperationType
CreatedAt
LocalSequence
DependsOn[]
CausationId
AggregateId
PayloadHash
Status

دارد.

در این حالت queueها هنوز از نظر semantics جدا هستند، ولی lifecycle و causal graph یکپارچه است.

این به نظر من از جمله مهم‌ترین اصلاحات architectural است.

5. P0 — Generic State Replication نباید به CRUD Sync تبدیل شود

این خطر در سند فعلی کمی پنهان است.

نباید نتیجه این شود:

Entity changed locally
       ↓
send row
       ↓
server updates row

برای بعضی داده‌ها مشکلی ندارد:

UI preference
Display setting
Non-critical metadata

ولی برای:

Appointment
Payment
Inventory
Invoice
Cash session
Medical history
Patient ownership

باید mutation semantics مشخص داشته باشد.

بنابراین به جای دو کلاس صرف:

State
Intent

یک classification چهارگانه بهتر است:

نوع	رفتار
Immutable Fact	فقط append
Replicated State	versioned sync
Business Command	server settlement
Derived Projection	قابل بازسازی

مثلاً:

Patient.Name
    → Replicated State

Appointment
    → Business Commands
       BookAppointment
       RescheduleAppointment
       CancelAppointment

Payment
    → Immutable Fact / Command

Ledger
    → Immutable Fact

Dashboard
    → Derived Projection

این مرز از نظر معماری بسیار دقیق‌تر است.

6. P0 — SMS با R7 فعلی تناقض دارد

در سند می‌گویید:

هیچ external runtime dependency وجود ندارد.

اما SMS یک capability عملیاتی محصول است.

SMS provider:

Animora → external SMS gateway

وابستگی خارجی است.

بنابراین باید R7 اصلاح شود.

نسخه درست:

Core clinic operation must not depend synchronously on external Internet services.

نه اینکه:

هیچ external service در runtime وجود ندارد.

معماری SMS
Business action
      ↓
CommunicationOutbox
      ↓
SMS Worker
      ↓
Provider Adapter
      ↓
SMS Gateway

و وضعیت:

Queued
ProviderUnavailable
Submitting
Submitted
Delivered
Failed
Expired
DeadLetter

اگر اینترنت قطع بود:

appointment reminder
       ↓
Queued
       ↓
clinic continues normally

نه:

SMS call failed → transaction failed

Microsoft برای workloadهایی که dependency خارجی ممکن است workload را block کند، queue-based load leveling را به عنوان راهکار decoupling و smoothing بار توصیه می‌کند؛ این دقیقاً برای SMS، report generation و برخی file jobs مناسب است.

7. P0 — یک Bounded Context مهم کم است

Contextهای فعلی:

Identity
Tenant
Patient
Clinical
Scheduling
Sales
Inventory
Finance
Reporting
Files
Subscription
Licensing
Audit
Synchronization

خوب‌اند.

اما با نیازهایی که خودت گفتی، حداقل این context باید اضافه شود:

Communication / Customer Engagement

مثلاً:

Communication
    Message
    Template
    Provider
    DeliveryAttempt
    DeliveryReceipt
    Campaign

Customer Engagement / Loyalty
    CustomerProfile
    Segment
    LoyaltyAccount
    LoyaltyTransaction
    Reward
    CampaignEnrollment
    Consent
چرا Loyalty باید ledger باشد؟

نباید:

Points = 350

را دائماً overwrite کنی.

بلکه:

+100 Purchase
+50 Campaign
-20 Reward
-10 Expired

و:

Balance = SUM(transactions)

یا یک projection معتبر از آن.

این همان تفکر append-only است که برای Finance انتخاب کرده‌ای و باید برای Loyalty هم استفاده شود.

8. P1 — Sharing کامل Domain/Application بین Server و Desktop را توصیه نمی‌کنم

این بخش:

Shared Domain
Shared Application
Shared Contracts

بیش‌ازحد aggressive است.

مشکل

Server domain شما شامل چیزهایی خواهد شد مثل:

EF/database assumptions
server authorization context
database repositories
server-only invariants
transaction boundaries
background jobs
tenant context

اینها نباید وارد Desktop شوند.

معماری بهتر
                    Shared
              ┌─────────────────┐
              │ Contracts        │
              │ DTOs             │
              │ Error Codes      │
              │ IDs              │
              │ deterministic    │
              │ calculation libs │
              └─────────────────┘
                   /       \
                  /         \
        Desktop Core       Server Core
        ----------         -----------
        Local model        Domain model
        Offline workflows  Application
        Local DB           DB
        Sync               Infrastructure

یعنی:

Contract sharing: yes

Pure deterministic business-calculation sharing: yes

Server Application layer sharing: no

Server persistence/domain model sharing: generally no

Microsoft's Clean Architecture guidance also puts business logic in the application core and infrastructure behind abstractions; it does not require that same server core to become the client runtime.

9. P1 — Multi-tenancy: Global Query Filter را Security Boundary نکن

این قسمت در سند شما نسبتاً خوب است اما wording باید اصلاح شود.

EF Core خودش می‌گوید Global Query Filters برای multitenancy مفیدند، ولی:

IgnoreQueryFilters()

می‌تواند آنها را bypass کند؛ همچنین required navigationها می‌توانند رفتار غیرمنتظره ایجاد کنند.

بنابراین معماری صحیح:

Authorization
      +
Tenant-scoped Application Service
      +
Tenant-aware Repository / Query
      +
Database RLS

است.

و نه:

Global Query Filter = Security
پیشنهاد مهم

اگر DB شما PostgreSQL است، من RLS را از «optional hardening» به recommended production defense-in-depth تبدیل می‌کنم.

PostgreSQL RLS می‌تواند visibility و write access را در سطح خود database enforce کند و در صورت نبود policy default-deny دارد. SQL Server هم RLS رسمی دارد و Microsoft آن را برای multitenant row isolation صراحتاً مطرح می‌کند.

ساختار:

Application authorization
          ↓
Tenant Context
          ↓
Tenant-aware query
          ↓
Database RLS

دو لایه اول برای correctness و authorization، RLS برای defense-in-depth.

10. P1 — «Tenant isolation حتی اگر App filter خاموش باشد» هدف درستی نیست

در سند آمده که چند لایه باید «مستقلاً کافی» باشند.

این تعریف بیش از حد تئوریک است.

مثلاً:

Application authorizes user as Tenant A

به تنهایی جلوی این query را نمی‌گیرد:

SELECT * FROM Patients WHERE Id = @id;

پس:

Application authorization alone is not a data-isolation mechanism.

بهترین طراحی:

tenant context
   ↓
every repository call requires tenant scope
   ↓
database policy also checks tenant

و test:

Tenant A
   └── request object belonging to Tenant B

باید در:

API
Application
Repository
Database
Cache
File storage
Reports
Background Jobs

رد شود.

OWASP نیز در راهنمای فعلی multitenancy تأکید می‌کند که tenant isolation باید در application، database، cache، storage و سایر لایه‌ها بررسی شود.

11. P1 — Web architecture شما یک فرض اشتباه درباره Scale-out دارد

در سند آمده:

دو instance وب → session affinity + backplane

این همیشه درست نیست.

اگر Web شما:

Stateless HTTP
+ signed/authenticated cookie
+ persisted Data Protection keys
+ no server-side session

باشد، نیازی به sticky session برای request/response معمولی ندارید.

مشکل اصلی زمانی است که از:

Interactive Server / SignalR

استفاده کنید.

Microsoft توضیح می‌دهد که Interactive Server برای هر interactive session یک server-side circuit و state ایجاد می‌کند.

برای server ضعیف پیشنهاد من:
Static SSR
   +
HTTP API
   +
Client-side interaction only where needed

و:

Interactive Server

فقط برای بخش‌هایی که واقعاً نیاز دارند.

برای 500–600 user، مخصوصاً اگر server ضعیف باشد، نباید stateful browser circuit را baseline قرار داد.

12. P1 — Data Protection Key design درست است اما deployment semantics باید دقیق‌تر شود

این تصمیم فعلی صحیح است:

key ring نباید ephemeral باشد.

Microsoft نیز تأکید می‌کند که Data Protection keys باید persistent باشند و حذف key می‌تواند payloadهای قبلی را برای همیشه غیرقابل‌بازگشت کند.

اما باید اضافه شود:

Primary key ring
+
Backup copy
+
Restore test
+
Rotation policy
+
Compromise/revocation procedure

و برای scale-out:

all app instances use same key ring

بدون این، cookie/antiforgery در instance دیگر قابل validate نیست.

13. P1 — Desktop authentication architecture خوب است ولی Offline Auth باید دقیق‌تر شود

انتخاب:

Authorization Code + PKCE
External Browser

کاملاً استاندارد است.

RFC 8252 برای native apps استفاده از external user-agent و PKCE را best practice می‌داند و Microsoft Windows نیز APIهای OAuth desktop مبتنی بر Authorization Code + PKCE و system browser دارد.

پس این بخش را نگه می‌دارم.

اما این را اضافه می‌کنم:

Online identity
      ≠
Offline capability

Offline capability باید:

device-bound
signed
time-bounded
capability-scoped
non-transferable

باشد.

در Windows، TPM/CNG برای hardware-backed key storage گزینه مناسبی است؛ Microsoft Platform Crypto Provider برای استفاده از TPM طراحی شده و می‌تواند private key را سخت‌تر قابل استخراج کند.

نکته مهم

این را هرگز به عنوان:

ضدکپی مطلق

معرفی نکن.

بلکه:

hardware binding raises the cost of cloning and credential extraction.

14. P1 — Offline capability باید Matrix داشته باشد

الان architecture می‌گوید «per capability»، ولی specification هنوز آن matrix را کامل نکرده.

من این مدل را پیشنهاد می‌کنم:

Capability	Offline
View patients	Yes
View clinical history	Yes
Add notes	Yes
Draft invoice	Yes
Create appointment draft	Yes
Book appointment	Yes, policy-controlled
Sell item	Yes, policy-controlled
Register payment	Yes, policy-controlled
Inventory adjustment	محدود
Refund	No / very short window
Cash closing	No
Period close	No
User/role management	No
Subscription management	No
Device revocation	No
License issuance	No

این جدول باید جزو ADRهای رسمی شود.

Power Apps نیز در offline-first دقیقاً رویکردی مشابه دارد: local work ادامه دارد، اما privilege changes و برخی online-only operations تا sync معتبرند و offline profile تعیین می‌کند چه داده‌ای روی device وجود داشته باشد.

15. P1 — Server باید «Source of Truth» باشد، ولی برخی چیزها نباید به Eventual Consistency رانده شوند

این بخش:

Sale + stock reduction + payment = atomic
Sale → finance draft = eventual

باید دوباره بررسی شود.

چون اگر accounting برای شما جزء core product است، ممکن است کاربر بگوید:

Sale successful

ولی:

General Ledger

هنوز آن را نداشته باشد.

برای خیلی از سیستم‌ها این مطلوب نیست.

از آنجا که شما:

same process
same database

دارید، باید تا جایی که business rule نیاز دارد عملیات مالی را یک transaction واحد کنید.

مثلاً:

Checkout
 ├── Sale
 ├── Payment
 ├── Inventory movement
 ├── Tax snapshot
 ├── COGS
 └── Journal entries

در یک transaction.

بعد:

Outbox

برای:

notifications
analytics
reports
customer club
SMS

استفاده شود.

Microsoft's Transactional Outbox guidance نیز دقیقاً state change و event را در یک transaction قرار می‌دهد تا dual-write اتفاق نیفتد.

16. P1 — Ledger را بهتر است Event Sourcing ننامیم

این عبارت:

event-sourcing-lite

برای ledger ممکن است از نظر terminology گمراه‌کننده باشد.

بهتر:

Append-only Accounting Journal / Immutable Financial Facts

چون:

Event Sourcing

و:

Accounting Ledger

یکی نیستند.

Event Sourcing یعنی state از events بازسازی شود؛ accounting ledger الزاماً چنین معماری‌ای ندارد.

17. P1 — Idempotency retention یک hole دارد

در سند:

(TenantId, OperationId)

خیلی خوب است.

اما مدت نگهداری رکورد idempotency تعریف نشده.

فرض کن:

Client offline = 90 days

و:

Server idempotency record retention = 30 days

بعد client یک operation قدیمی را retry کرد.

اگر original effect واقعاً قبلاً اجرا شده باشد و record idempotency حذف شده باشد:

duplicate effect

ممکن است رخ دهد.

بنابراین:

Financial commands

OperationId باید عملاً بخشی از immutable financial history باشد.

Non-financial commands

Retention:

max offline command lifetime
+
replay safety margin

باشد.

18. P1 — Audit باید دو سطح داشته باشد

الان:

Audit = async sink

به نظرم کافی نیست.

برای:

payment
refund
invoice posting
role change
device registration/revocation
subscription change
period close

Audit fact باید با business fact اتمی باشد.

مثلاً:

Transaction
 ├── JournalEntry
 ├── Payment
 ├── AuditEvent
 └── OutboxEvent

همه commit شوند.

برای telemetry معمولی:

async

کاملاً مناسب است.

بنابراین:

Compliance/Security Audit = synchronous durable fact
Operational Logging = async telemetry

این دو را از هم جدا کن.

19. P1 — Logging فعلی باید Privacy-aware شود

در یک clinic system احتمالاً اینها وارد سیستم می‌شوند:

owner name
phone
address
animal medical history
documents
payment information

نباید اینها بی‌دلیل وارد log شوند.

OWASP صراحتاً توصیه می‌کند access token، password، encryption key، connection string و sensitive PII/health-like data در log ثبت نشوند و در صورت نیاز mask/hash شوند.

پس:

TenantId
UserId
DeviceId
CorrelationId
OperationId
RequestId

خوب است.

ولی:

full patient record
access token
refresh token
password
medical note
file contents

نباید log شوند.

20. P1 — File architecture درست است، اما Content Hash باید tenant-scoped باشد

این:

File Store
content addressed by hash

از نظر deduplication خوب است.

ولی بهتر است کلید storage چیزی مثل:

Tenant/{TenantId}/File/{FileId}/{ContentHash}

باشد، نه فقط:

SHA256(file)

چون global content addressing می‌تواند اطلاعات جانبی درباره existence فایل مشترک بین tenantها ایجاد کند.

همچنین برای فایل‌های حساس:

DB metadata
+
binary
+
hash
+
tenant
+
classification
+
encryption status
+
retention
+
quarantine state

لازم است.

OWASP نیز نگهداری فایل خارج از webroot یا روی storage جدا از application را در صورت امکان توصیه می‌کند و روی اعتبارسنجی نوع/محتوا و کنترل دسترسی تأکید دارد.

21. P1 — File Valet Key برای self-hosted local storage کمی over-engineered است

Valet Key ذاتاً برای زمانی بسیار مفید است که storage جداست و می‌خواهید application از مسیر انتقال file حذف شود؛ Microsoft نیز همین مزیت را تصریح می‌کند.

اما شما در launch احتمالاً دارید:

Application
Database
File Store

را روی یک node اجرا می‌کنید.

برای فایل‌های کوچک:

Authorized API endpoint

کاملاً مناسب است.

برای فایل‌های بزرگ‌تر:

Authorization
   ↓
internal redirect / direct local file serving

بهتر است تا application process مجبور نباشد bytes را حمل کند.

پس Valet Key را capability pattern نگه می‌دارم، ولی آن را الزام تمام file transferها نمی‌کنم.

22. P1 — Background Worker بهتر است process جدا داشته باشد، نه فقط BackgroundService

Modular Monolith الزاماً به معنای:

one process

نیست.

بهتر است:

                Same Codebase
                     |
          ┌──────────┴──────────┐
          |                     |
     Animora API           Animora Worker
          |                     |
       HTTP/UI              Jobs/Outbox
          |                     |
          └──────────┬──────────┘
                     |
                  Database

یعنی:

one deployment unit
one repository
one architecture
one database

ولی:

API process
Worker process

جدا.

فایده:

report سنگین API را نمی‌کشد
SMS backlog CPU را نمی‌گیرد
sync processing request threads را اشغال نمی‌کند
file processing قابل محدودسازی است
crash worker کل API را نمی‌خواباند

این دقیقاً با هدف weak-server شما هماهنگ‌تر است.

23. Worker Queue

در launch نیازی به RabbitMQ/Kafka/Redis ندارید.

یک relational database-backed queue کافی است:

WorkItem
----------
Id
Type
TenantId
Priority
Status
AvailableAt
LeaseUntil
Attempts
LastError
CreatedAt

claim:

Pending
  ↓
atomic claim
  ↓
Processing
  ↓
Succeeded

یا:

Retry
DeadLetter

برای PostgreSQL می‌توان از قابلیت‌های locking مناسب برای claim کردن کارها استفاده کرد؛ در scale فعلی لازم نیست message broker اضافه کنید.

Microsoft در الگوی Competing Consumers نیز روی idempotency، ordering و Dead Letter تأکید دارد.

24. P1 — Competing Consumers را بیش‌ازحد استفاده نکن

برای:

Financial command settlement
Appointment allocation
Cash session
Ledger posting

worker concurrency بالا خطرناک است.

برای هر work type تعریف کن:

ConcurrencyClass
OrderingKey
RetryPolicy
DeadLetterPolicy

مثلاً:

SMS
    concurrency = 10
    ordering = none

Reports
    concurrency = 1-2
    ordering = none

Tenant financial settlement
    concurrency = controlled
    ordering = Tenant/Aggregate

Microsoft نیز تأکید می‌کند Competing Consumers ذاتاً ordering را تضمین نمی‌کند؛ وقتی order مهم است باید explicit ordering key یا مکانیزم مخصوص داشته باشید.

25. P1 — Retry باید بر اساس operation semantics باشد

برای Sync:

retry = yes

برای command:

retry same OperationId = yes
retry with new OperationId = NEVER

این بخش در سند فعلی صحیح است.

ولی اضافه کن:

max elapsed retry duration
max attempts
jitter
per-device backoff
per-tenant reconnect cap
global reconnect cap

Microsoft درباره Retry Storm دقیقاً هشدار می‌دهد که retryهای همزمان می‌توانند recovery سرور را بدتر کنند.

26. P1 — Reconnect Storm باید برای 500–600 user واقعاً شبیه‌سازی شود

این یکی از مهم‌ترین benchmarkهای شماست.

سناریو:

600 clients
server unavailable = 30 min

server comes back

600 clients reconnect simultaneously

اگر هر client:

login
pull metadata
pull records
push commands
upload files
refresh license

را همزمان انجام دهد، server می‌تواند از ترافیک عادی چندین برابر فشار ببیند.

راه‌حل:

random startup jitter
exponential backoff
priority command settlement
progressive synchronization
per-device sync budget
server throttling

Microsoft's throttling guidance نیز توصیه می‌کند throttling را قبل از saturation انجام دهید و علاوه بر request rate، concurrency، queue depth و resource saturation را هم کنترل کنید.

27. Capacity — 500 تا 600 User دقیقاً چه معنی دارد؟

این عدد به‌تنهایی قابل sizing نیست.

مثلاً:

حالت A

600 user × 5 request/min:

3000 requests/min
≈ 50 requests/sec
حالت B

600 user × 10 request/min:

6000 requests/min
≈ 100 requests/sec
حالت C

600 user × 20 request/min:

12000 requests/min
≈ 200 requests/sec

این فقط baseline است.

Sync storm می‌تواند عدد را خیلی بالاتر ببرد.

بنابراین من target benchmark را این‌طور تعریف می‌کنم:

600 concurrently authenticated users
+
at least 150–200 actively interacting users
+
100 RPS sustained business workload
+
200–300 RPS peak burst
+
mass reconnect test
+
background workload
+
database reporting workload

اینها اهداف benchmark پیشنهادی هستند، نه ظرفیت تضمین‌شده.

Microsoft نیز صراحتاً توصیه می‌کند reliability/performance targets از business workload تعریف شوند و بعد اندازه‌گیری شوند؛ عدد concurrent user از روی hardware به‌تنهایی ظرفیت را ثابت نمی‌کند.

28. برای 500–600 User، Modular Monolith کاملاً کافی است

من در launch این کارها را نمی‌کنم:

Kubernetes
Microservices
Kafka
Service Mesh
Distributed Cache
CQRS database split
Event Store
Multi-region
Global API Gateway

معماری launch:

Internet / National Network
          |
       Reverse Proxy
          |
     API/Web Process
          |
   Modular Application
          |
     Relational DB
          |
      File Store

      Worker Process
          |
       DB Queue
          |
   SMS / Reports / Files

این بسیار استانداردتر و عملی‌تر است.

Microsoft هم monolithic deployment را برای applicationهای غیرtrivial معتبر می‌داند و Deployment Stamps را زمانی مناسب می‌داند که واقعاً نیاز به scale/isolation بیشتری وجود داشته باشد.

29. Scale-out Escape Hatch را تغییر بده

نسخه فعلی:

Web app → 2+ instances
requires sticky session + backplane

بهتر است:

Stage 0
Single node

Stage 1
Separate worker process

Stage 2
Separate DB/file host

Stage 3
Stateless API x2

Stage 4
Dedicated read/reporting node

Stage 5
Deployment stamps

Stage 6
High availability DB

نه:

single node → microservices

Microsoft Deployment Stamps را دقیقاً برای تقسیم tenantها بین scale units مستقل و کاهش blast radius معرفی می‌کند.

30. یکی از مهم‌ترین توصیه‌های من: یک «Control Plane» واقعی تعریف کن

در معماری شما عملاً control plane وجود دارد ولی نام‌گذاری نشده.

من آن را صریح می‌کنم:

CONTROL PLANE
------------
Identity provisioning
Tenant provisioning
Subscription
Billing state
License issuance
Device registration
Plan management
Software release
Support
Backup management

DATA PLANE
----------
Clinic login
Patient
Clinical
Scheduling
Sales
Inventory
Finance
Sync
Files
Reports

اصل:

Data Plane must not synchronously depend on Control Plane.

این دقیقاً باعث می‌شود:

Website unavailable

ولی:

Clinic continues

باشد.

و:

Billing system unavailable

ولی:

Clinic continues

باشد.

31. Website نباید Identity Database جداگانه داشته باشد

در حال حاضر خطر implicit وجود دارد:

Website Account
Animora Account

نباید دو identity مستقل باشند.

باید:

Product Identity
       |
       +--- Website
       |
       +--- Desktop
       |
       +--- Web

باشد.

Website:

register
purchase
provision

را انجام می‌دهد ولی identity authority یکی است.

Website نباید:

User password
User roles
Tenant membership

را duplicate کند.

32. Subscription و Seat Model را دقیق‌تر کن

اینها را از هم جدا کن:

User
Membership
Device
Session
Seat
Subscription
License

مثلاً:

Subscription
   └── seat limit = 4

Tenant
   ├── User A
   ├── User B
   ├── User C
   └── User D

Devices
   ├── Device 1
   ├── Device 2
   └── Device 3

و باید مشخص شود:

seat یعنی user یا device یا concurrent session؟

من پیشنهاد می‌کنم:

Licensed user seats
+
device registrations
+
concurrent session limit

سه مفهوم جدا باشند.

برای crash:

session lease
heartbeat
expiry
reclaim

لازم است.

33. API Contract Architecture

این بخش در سند باید اضافه شود.

برای هر API/Sync contract:

ContractVersion
SchemaVersion
RequestId
CorrelationId
OperationId
TenantScope
DeviceId
FeatureFlags

و compatibility policy:

Server N
supports:
N
N-1
N-2

ولی الزام:

new client ↔ old server

نباید کورکورانه وجود داشته باشد.

بهتر:

New client asks server capability/version

If unsupported:
    controlled "upgrade required"

Otherwise:
    negotiate compatible contract

این بسیار مهم‌تر از ادعای:

N/N-1 bidirectional compatibility

است.

34. Initial Sync را به «Offline Profile» تبدیل کن

این بخش از Power Apps بسیار قابل اقتباس است.

برای هر device:

Offline Profile

تعریف کن.

مثلاً Doctor:

patients
clinical
appointments
vaccinations

Cashier:

patients
sales
payments
products
cash session

Inventory:

products
stock
suppliers
purchases

نه اینکه کل Tenant روی هر device دانلود شود.

Power Apps نیز offline profile را برای تعیین جدول‌ها، روابط و subset داده‌ای که روی device قرار می‌گیرد به کار می‌برد و صراحتاً توصیه می‌کند فقط داده موردنیاز offline روی device download شود.

این هم:

performance

را بهتر می‌کند و هم:

security

را.

35. Client Local Database

اگر برای Desktop از SQLite استفاده می‌کنی، انتخاب بسیار منطقی‌ای است.

ولی سه قانون مهم:

Local SSD only
WAL mode
Never network share

SQLite در WAL mode concurrency خواندن/نوشتن بهتری دارد، اما همچنان فقط یک writer همزمان دارد و WAL برای network filesystem مناسب نیست.

برای یک Desktop که local database دارد:

SQLite

عالی است.

برای:

\\server\clinic\animora.db

اصلاً مناسب نیست.

36. Local data encryption باید واقعی باشد

برای Windows:

Local DB
   ↓
encrypted
   ↓
key protected by Windows/TPM/user context

نه:

machineId
+
AES key stored next to DB

Windows DPAPI/CNG برای حفاظت از secrets روی همان دستگاه mechanisms رسمی دارد و TPM/CNG می‌تواند برای device-bound keys استفاده شود.

37. Backup architecture را از نظر فیزیکی کمی قوی‌تر کن

برای SaaS با clinic data:

Primary Server
     |
     +--- Local backup
     |
     +--- Off-host backup
     |
     +--- Immutable backup

حداقل.

اگر DB شما PostgreSQL باشد، WAL archiving و PITR به شما اجازه می‌دهد از base backup + WAL برای point-in-time recovery استفاده کنید؛ این بسیار بهتر از فقط nightly dump است.

برای full restore نیز:

Database
+
Files
+
Runtime Key Ring
+
Configuration
+
Build/Schema version

باید به یک Recovery Point وصل باشند.

38. Single Node را به عنوان HA معرفی نکن

این جمله فعلی درست است:

single node = SPoF

اما برای Production من توصیه می‌کنم:

Single-node compute

اگر هزینه مهم است.

ولی:

Off-host backup

حتماً.

و بعداً:

DB standby

اولین گام HA باشد، نه microservices.

Microsoft WAF نیز availability و recovery را باید با SLO/RTO/RPO تعریف کند و failure domain را بخشی از طراحی reliability می‌داند.

39. PKI داخلی فعلی احتمالاً بیش‌ازحد پیچیده است

این قسمت را ساده‌تر می‌کنم.

اگر مشتریان به:

https://app.animora.ir

وصل می‌شوند، برای runtime endpoint از certificate عمومی استاندارد استفاده کن.

نیازی نیست:

Animora Root CA

را به همه PCها install کنی.

این بیشتر برای:

private LAN endpoint
internal admin endpoint
mTLS

مناسب است.

برای central SaaS endpoint:

Public trusted TLS

عملی‌تر است.

در نتیجه:

Public TLS

برای:

Desktop
Web
Website
API

و:

Private CA

فقط برای private infrastructure در صورت نیاز.

این deployment را بسیار ساده‌تر می‌کند.

40. Domain decomposition پیشنهادی من

نسخه بهتر Context Map:

Identity
Tenant / Clinic
Membership
Patient / Owner
Clinical
Scheduling
Sales / POS
Inventory
Finance
Files
Reporting
Subscription
Licensing
Communication
Customer Engagement / Loyalty
Audit

و زیر سیستم‌های infrastructure:

Synchronization
Job Processing
File Transfer
Caching
Observability
External Integrations

یعنی:

Synchronization را واقعاً از Domain جدا نگه دار.

این بخش فعلی شما یکی از بهترین تصمیم‌هاست و باید حفظ شود.

41. Customer Engagement معماری پیشنهادی

مثلاً:

Patient
  |
Owner
  |
Customer Profile
  |
Segment
  |
Campaign
  |
Communication
  |
SMS Provider

و برای loyalty:

LoyaltyAccount
     |
LoyaltyTransaction[]
     |
Balance Projection

هر transaction:

OperationId
Source
Reason
Points
Timestamp
Reference

داشته باشد.

42. Scheduling نیازمند مدل Resource Allocation است

این قسمت architecture فعلی درست متوجه شده ولی باید اجرایی‌تر شود.

مثلاً:

Veterinarian
Room
Boarding Cage
Surgery Room
Equipment

همگی resource هستند.

Appointment فقط record نیست.

بلکه:

AppointmentRequest
    ↓
Allocation Transaction
    ↓
ResourceAssignment

و constraint:

same resource
same time interval
cannot overlap

باید در DB/business transaction enforce شود.

اگر DB شما PostgreSQL باشد، unique/exclusion constraints و transaction isolation ابزارهای بسیار خوبی برای این نوع invariantها هستند؛ SERIALIZABLE نیز برای بعضی allocationها می‌تواند مناسب باشد، هرچند serialization failure باید retry شود.

43. Inventory

مدل فعلی بسیار خوب است.

ولی اینها را حتماً به ADR تبدیل کن:

Negative Stock
Reservation
Lot
Batch
Expiry
Serial
Warehouse
Location
Cost Layer
COGS
Return
Damaged
Adjustment

و یک اصل:

QuantityOnHand هیچ‌وقت مستقیماً توسط client overwrite نمی‌شود.

44. Finance

Finance باید شامل این invariantها باشد:

Double entry
Immutable posted fact
Period state
Currency
Tax
Rounding
Exchange rate snapshot
Source document
Reversal
Audit
Idempotency

و:

Draft
Posted
Reversed

با:

Command Settlement Status

دو status system جدا باشند.

این تفکیک فعلی شما بسیار خوب است و باید حفظ شود.

45. Reporting

روی weak server:

OLTP query
+
heavy report

نباید یکی باشند.

نسخه launch:

OLTP DB
    |
    +--- normal queries
    |
    +--- precomputed summary tables
    |
    +--- scheduled reports

و بعداً:

Reporting DB

فقط وقتی benchmark نشان داد نیاز است.

46. Cache

در launch Redis لازم نیست.

اول:

in-memory bounded cache

برای چیزهایی مثل:

plans
permissions metadata
reference data
configuration

کافی است.

ولی key باید شامل:

Tenant
User / permission scope
Resource
Query version

باشد.

Microsoft Cache-Aside را برای read optimization مناسب می‌داند، ولی تأکید می‌کند که cache freshness و invalidation باید بخشی از طراحی باشد.

47. Rate limiting را از ابتدا داشته باش

نه فقط برای حمله.

برای noisy tenant:

per IP
per user
per device
per tenant
per endpoint

مثلاً:

/login
/sync
/command
/files/upload
/reports
/sms

هرکدام policy مختلف.

Microsoft throttling را دقیقاً به عنوان ابزار کنترل resource consumption و noisy-neighbor معرفی می‌کند.

OWASP API Security نیز unrestricted resource consumption را یکی از ریسک‌های اصلی API می‌داند.

48. Authentication security

برای password:

Argon2id

یا fallback مناسب.

OWASP در نسخه فعلی Password Storage guidance استفاده از Argon2id را توصیه می‌کند و work factor را باید با benchmark روی hardware واقعی تعیین کرد.

برای desktop:

Authorization Code
+
PKCE S256
+
External Browser
+
Refresh Token Rotation
+
Secure OS Storage

برای web:

Secure
HttpOnly
SameSite
Cookie
+
Anti-forgery

Microsoft صراحتاً تأکید می‌کند cookie-based auth برای state-changing browser requests بدون antiforgery کافی نیست.

49. Public API Security

تمام endpoints باید:

Authentication
Authorization
Tenant scope
Resource authorization
Input validation
Rate limit
Payload size limit
Timeout
Audit where sensitive

داشته باشند.

خصوصاً:

GET /patients/{id}

نباید صرفاً بگوید:

user authenticated

بلکه:

Does this user have access to this patient?

را بررسی کند.

OWASP API Security در API1 Broken Object Level Authorization دقیقاً روی همین مسئله تأکید می‌کند.

50. Revised Target Architecture

معماری نهایی پیشنهادی من:

                        ┌──────────────────────┐
                        │ Public Website       │
                        │ Register / Billing   │
                        │ Download             │
                        └──────────┬───────────┘
                                   │
                            Control Plane
                                   │
                  ┌────────────────┴─────────────────┐
                  │                                  │
             Identity                         Subscription
             Provisioning                       Licensing
                  │                                  │
                  └────────────────┬─────────────────┘
                                   │
                       signed / authenticated
                         management contract
                                   │
                                   ▼
┌────────────────────────────────────────────────────────────┐
│                    ANIMORA CENTRAL NODE                    │
│                                                            │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ Reverse Proxy / TLS / Rate Limit                    │  │
│  └───────────────────────┬──────────────────────────────┘  │
│                          ▼                                 │
│  ┌──────────────────────────────────────────────────────┐  │
│  │                 Animora API/Web                     │  │
│  │                                                      │  │
│  │ Business API       Sync API        File API         │  │
│  └───────────────────────┬──────────────────────────────┘  │
│                          ▼                                 │
│  ┌──────────────────────────────────────────────────────┐  │
│  │              Modular Application Core                │  │
│  │                                                      │  │
│  │ Identity     Tenant        Patient      Clinical    │  │
│  │ Scheduling   Sales         Inventory    Finance     │  │
│  │ Reporting    Files         Subscription Licensing  │  │
│  │ Communication              Loyalty                 │  │
│  └───────────────────────┬──────────────────────────────┘  │
│                          │                                 │
│              ┌───────────┴───────────┐                     │
│              ▼                       ▼                     │
│       Relational Database       File Storage               │
│              │                       │                     │
│              └───────────┬───────────┘                     │
│                          ▼                                 │
│              ┌──────────────────────┐                      │
│              │ Animora Worker       │                      │
│              │                      │                      │
│              │ Outbox               │                      │
│              │ SMS                  │                      │
│              │ Reports              │                      │
│              │ File reconciliation  │                      │
│              │ Cleanup              │                      │
│              │ Notifications        │                      │
│              └──────────┬───────────┘                      │
│                         │                                  │
└─────────────────────────┼──────────────────────────────────┘
                          │
                    external adapters
                          │
              ┌───────────┴────────────┐
              │                        │
           SMS Provider           Other Providers
51. Desktop Target Architecture
Desktop
│
├── Presentation
│
├── Offline Application Core
│
├── Local Authorization UX
│
├── Local Store
│
├── Local Operation Journal
│
├── Sync Coordinator
│
├── Command Settlement Coordinator
│
├── File Transfer Manager
│
├── Device Identity
│
├── Offline Capability
│
└── Secure Key Storage

و local DB:

encrypted local database
+
local operation journal
+
sync cursor
+
settlement states
+
device metadata
52. Sync Protocol پیشنهادی
Down Sync
Client
  |
  | lastChangeSequence
  v
Server
  |
  | page of changes
  v
Client
  |
  | apply transaction
  |
  | advance cursor

Cursor فقط بعد از:

local transaction committed

advance شود.

هر page:

Changes
+
NextCursor
+
HasMore

دارد.

53. Full Resync

اگر:

client cursor < server retention floor

یا:

local DB corrupted

باشد:

FULL RESYNC

نه تلاش برای repair ناقص.

54. Sync Conflict State Machine

بهتر است:

Pending
↓
Applying
↓
Accepted
│
├── Rejected
├── Conflict
├── NeedsUserAction
└── Superseded

برای هر conflict:

what happened
why
server state
client state
available action
financial impact

در UI.

55. Client UI باید Sync state را first-class نشان دهد

مثلاً:

● Connected
● Offline
↻ Syncing
! 3 Pending Changes
! 1 Conflict
✓ All Changes Synced

این الگو در محصولات Microsoft offline-first نیز به‌صورت explicit sync status نمایش داده می‌شود.

56. Architecture Fitness Tests که باید به لیست فعلی اضافه شوند

علاوه بر Gateهای فعلی:

G-A

ChangeSequence Commit Ordering

دو transaction concurrent → هیچ change skip نشود.

G-B

Cross Pipeline Causality

Replication Op → Business Command dependency درست settlement شود.

G-C

Financial Idempotency Retention

بعد از restart، restore و long offline replay، financial duplicate رخ ندهد.

G-D

Four Devices One Clinic Offline

چهار desktop یک clinic برای N روز بدون central server کار کنند و conflictها deterministic باشند.

G-E

SMS Provider Down

SMS provider برای 24 ساعت down شود و clinic core function همچنان کار کند.

G-F

Worker Starvation

100,000 report/SMS/file jobs وجود داشته باشد ولی interactive API SLO از بین نرود.

G-G

Tenant Cache Poisoning

Tenant A هرگز result tenant B را نگیرد.

G-H

Audit Atomicity

Business transaction success ⇒ required audit fact exists.

Business transaction rollback ⇒ corresponding audit business fact نیز rollback شود.

G-I

Client Lost Disk

exact policy برای unsent commands ثابت شود.

G-J

Server Restore

Database + files + Data Protection keys از یک recovery point restore شوند.

G-K

Mixed Version

Server N + client N/N-1/N-2.

و:

unsupported combination

باید controlled failure داشته باشد.

57. Production deployment پیشنهادی

برای launch:

4 vCPU
8–16 GB RAM
NVMe SSD
Linux

را به عنوان benchmark baseline می‌گذارم، نه requirement قطعی.

اگر منظور از weak server چیزی مانند:

1 vCPU
2 GB RAM
slow HDD

است، من برای 500–600 concurrent users بدون benchmark موفقیت را تضمین نمی‌کنم.

ترتیب منابع:

Fast SSD
RAM
CPU

برای workload شما بسیار مهم است، چون:

DB
sync
file store
reports
worker jobs

همه روی یک node هستند.

58. اولویت اصلاحات
Must Fix Before Coding
تعریف دقیق Offline semantics
Clinic Edge یا عدم وجود آن به عنوان explicit product decision
Commit-safe ChangeSequence
unified Local Operation Journal
entity sync classification
SMS/Communication architecture
exact Offline Capability Matrix
financial atomicity policy
identity ownership between Website and Animora
tenant enforcement model
client/server shared-code boundary
idempotency retention
audit atomicity
API contract/versioning rules
قبل از Production
worker process isolation
rate limiting
reconnect storm
backup/PITR
file reconciliation
privacy-aware logging
RLS
local encryption
disaster restore drill
mixed-version compatibility
security testing
بعداً
Redis
Message Broker
separate reporting DB
deployment stamps
read replicas
dedicated tenant DB
active-active HA

اینها را الان نساز.

59. معماری‌ای که من برای Animora نهایی می‌کنم

اگر خودم مسئول Architecture Sign-off این پروژه بودم، تصمیم‌ها را این‌طور ثبت می‌کردم:

AD-01
Modular Monolith

AD-02
Relational Database

AD-03
Shared database + tenant discriminator

AD-04
Database-level RLS as defense-in-depth

AD-05
Server is global authority

AD-06
Offline desktop is local-first

AD-07
No web offline at launch

AD-08
Local Operation Journal

AD-09
Typed mutation model:
State / Command / Immutable Fact / Projection

AD-10
Commit-safe tenant-local change sequence

AD-11
Snapshot-consistent initial sync

AD-12
Business command idempotency

AD-13
Financial facts immutable

AD-14
Financially coupled operations atomic

AD-15
Outbox for asynchronous effects

AD-16
DB-backed durable worker queue

AD-17
Separate API and worker processes

AD-18
SMS is asynchronous external integration

AD-19
Offline capability is capability-scoped

AD-20
Authorization Code + PKCE for native client

AD-21
Device-bound offline capability

AD-22
Website is control-plane client

AD-23
Single product identity authority

AD-24
Subscription ≠ License ≠ Authorization

AD-25
Public TLS for public runtime endpoint

AD-26
Private CA only where private infrastructure actually needs it

AD-27
Tenant-safe cache and file access

AD-28
Audit business facts are transactionally durable

AD-29
RTO/RPO explicitly tested

AD-30
Capacity certified by load/chaos testing

AD-31
Deployment Stamps deferred

AD-32
Microservices deferred
60. جمع‌بندی نهایی
چیزهایی که در معماری فعلی واقعاً درست هستند

اینها را من حفظ می‌کنم:

Modular Monolith
Clean Architecture
DDD bounded contexts
Server authoritative
Offline-first desktop
Transactional Outbox
Idempotent Consumer
Append-only financial facts
Movement-based inventory
Tenant-aware design
Capability-scoped offline security
Explicit RTO/RPO
Local observability
Contract versioning
No microservices at launch

این قسمت‌ها از نظر معماری mature هستند. Microsoft guidance نیز با انتخاب monolith، separation of concerns، transactional outbox، multitenancy، throttling و deployment stamps در نقاط مربوط هم‌جهت است.

چیزهایی که حتماً تغییر می‌دهم
Two totally independent client queues
        ↓
Unified Local Operation Journal + typed handlers

Global/per-scope sequence without commit semantics
        ↓
Commit-safe tenant-local change version

Generic row sync for too many entities
        ↓
Typed Sync Semantics

Shared full Domain/Application between server/client
        ↓
Shared Contracts + pure deterministic libraries only

Zero external runtime dependency
        ↓
Core operations independent of external providers

SMS direct integration
        ↓
Durable Communication Outbox + Worker

Audit async-only
        ↓
Transactional business audit + async operational telemetry

Interactive server web as potential baseline
        ↓
Stateless HTTP/SSR baseline

Global EF filter as isolation hardening
        ↓
Tenant-scoped application boundary + DB RLS

Single worker inside main process
        ↓
Separate worker process, same modular monolith

Single-node backups
        ↓
Off-host immutable backup + tested restore
61. حکم نهایی من

Animora نباید Microservice شود.

برای هدف:

500–600 concurrent users
125–200 clinics تقریباً
3–4 concurrent users / clinic
weak Linux server
offline-first desktop
future web
multi-tenant SaaS
finance
inventory
SMS
loyalty

بهترین architecture به نظر من:

Modular Monolith + Relational Database + Offline-first Local Replica + Unified Local Operation Journal + Server-authoritative Business Settlement + Transactional Outbox + Durable DB-backed Workers + Strict Tenant Isolation + Explicit Offline Capability Model

است.

این معماری هم ساده‌تر از microservices است، هم روی hardware ضعیف‌تر شانس بسیار بهتری دارد، هم مسیر scale-out واقعی دارد.

اما من قبل از نوشتن featureهای اصلی، Sync Protocol، Local Operation Journal، Offline Capability Matrix، Financial Transaction boundaries و Tenant isolation را freeze می‌کردم. اگر این پنج قسمت اشتباه طراحی شوند، بعداً تغییر دادنشان بسیار پرهزینه خواهد بود.

و یک نکته مهم درباره مستندات: Microsoft خودش WAF و Architecture Center را به‌عنوان ابزار تصمیم‌گیری و ارزیابی ارائه می‌کند، نه نسخه آماده برای همه workloadها؛ بنابراین اینکه سند Animora پر از نام pattern است به‌تنهایی مزیت نیست. معیار نهایی باید invariant، failure mode، measurable SLO و fitness test باشد.

مهم‌ترین منابع اصلی بررسی‌شده

Microsoft .NET — Architectural Principles

Microsoft .NET — Common Web Application Architectures / Clean Architecture

Microsoft Azure — Multitenant Architecture Guidance

Microsoft Azure — Deployment Stamps

Microsoft Azure — Transactional Outbox

Microsoft EF Core — Global Query Filters

Microsoft Power Apps — Offline-first architecture

IETF RFC 8252 — OAuth 2.0 for Native Apps

OWASP — Multi-Tenant Application Security

PostgreSQL — Row-Level Security

PostgreSQL — Transaction Isolation

Microsoft — ASP.NET Core Data Protection Key Management