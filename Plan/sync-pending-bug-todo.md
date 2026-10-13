# Plan — Sync Button "Pending Changes" Bug

> وضعیت کلی: در حال انجام
> این فایل برای ادامه‌ی کار در یک سشن کاملاً جدید نوشته شده. تمام ریشه‌یابی انجام شده و نتایج خالص اینجاست؛ مدل بعدی نیازی به بررسی دوباره‌ی کد ندارد و مستقیم می‌تواند پیاده‌سازی/ادامه را انجام دهد.

---

## قانون قفل‌شده (CRITICAL)
بعد از تکمیل هر تسک از این لیست، حتماً اجرا شود:
```bash
scripts/git/push.sh "type(scope): message"
```
نپوش کردن = شکست. هر تسک = یک commit + push مستقل.

---

## پروژه (context خالص)
- **Animora/Petjan** — اپ دسکتاپ Avalonia + .NET، معماری offline-first با command journal و sync engine.
- Backend (ASP.NET Kestrel) روی `http://188.212.99.234:5000`.
- UI دکمه‌ی همگام‌سازی در `TopbarView` است و از `ISyncStatusService` داده می‌گیرد.

### فایل‌های کلیدی (مسیر دقیق)
| نقش | فایل |
|---|---|
| ViewModel دکمه sync | `src/Animora.Desktop/Shell/TopbarViewModel.cs` |
| سرویس وضعیت sync (مغز) | `src/Animora.Client.Services/Sync/LocalSyncStatusService.cs` |
| قرارداد سرویس | `src/Animora.Contracts/Sync/ISyncStatusService.cs` |
| مدل خلاصه‌ی صف فرمان‌ها | `src/Animora.Application/UseCases/Sync/PendingCommandSummaryQuery.cs` |
| ژورنال فرمان (SQLite) | `src/Animora.Persistence.Sqlite/Commands/CommandJournal.cs` |
| SQL claim صف | `src/Animora.Persistence.Sqlite/Commands/PipelineSql.cs` |
| پردازشگر pass | `src/Animora.Application/Commands/LocalCommandProcessor.cs` |
| enum وضعیت settlement | `src/Animora.Domain/Primitives/SettlementState.cs` |

---

## ریشه‌یابی نهایی (تمام شد — نتیجه‌ی خالص)

**علامت:** دکمه همگام‌سازی همیشه «N تغییر ارسال‌نشده» نشان می‌دهد؛ بعد از کلیک، عدد کم نمی‌شود / تغییرات اعمال نشده به نظر می‌رسد.

**زنجیره‌ی علت (قطعی، از روی کد + لاگ):**
1. در لاگ، همه‌ی فرمان‌ها واقعاً به `POST /api/commands` رفته و `200 OK` گرفته‌اند. اما بسیاری `outcome:1` با کدهایی مثل `INVOICE_NOT_FOUND`, `PET_NOT_FOUND`, `ROOM_UNAVAILABLE`, `STAY_NOT_FOUND` برگردانده‌اند → یعنی سرور آن‌ها را **Rejected** کرده (رد تجاری، نه خطای شبکه).
2. در `CommandJournal.ApplySettlementAsync` → `TargetState(Rejected) = SettlementState.Rejected`. این یک حالت **ترمینال** است.
3. در `PipelineSql.ClaimPendingCommandsAsync` فقط ردیف‌های `Status IN (LocallyPosted, PendingSettlement)` دوباره claim می‌شوند. پس ردیف‌های `Rejected`/`Conflict`/`NeedsAttention` **هرگز دوباره ارسال نمی‌شوند**.
4. اما در `CommandJournal.GetSummaryAsync`، تابع `IsUnsettled` حالت‌های `Rejected`/`Conflict`/`NeedsAttention` را هم «unsettled» می‌شمارد و در `Total` می‌آورد.
5. `LocalSyncStatusService`:
   - `MapState`: اگر `summary.Total > 0` → `SyncState.Pending`.
   - `PendingChangeCount = summary.Total`.
6. `TopbarViewModel` حالت `Pending` با `pending>0` را «N تغییر ارسال‌نشده» و رنگ کهربایی نمایش می‌دهد.

**نتیجه:** ردیف‌های رد/تعارض/نیازمند-رسیدگی که `SyncNowAsync` هرگز نمی‌تواند پاکشان کند، برای همیشه به‌عنوان «تغییر ارسال‌نشده» شمرده می‌شوند. عدد هرگز صفر نمی‌شود و دکمه انگار بی‌اثر است = دقیقاً گزارش کاربر.

**فیکس استاندارد (کمینه، بدون تغییر signature):**
- «کار ارسالی قابل‌تکرار» را از «ردیف‌های ترمینالِ نیازمند رسیدگی» جدا کن.
- مقدار قابل‌تکرار = `Total - Rejected - Conflict - NeedsAttention` (= Draft + LocallyPosted + PendingSettlement) — همان چیزی که `ClaimBatchAsync` واقعاً claim می‌کند.
- در `LocalSyncStatusService`:
  - `PendingChangeCount` و تصمیم `SyncState.Pending` را بر اساس **retriable** بگذار، نه `Total`.
  - اگر retriable=0 ولی attention(Rejected+Conflict+NeedsAttention)>0 → وضعیت `Synced` (چون کار ارسالی‌ای نمانده)، نه Pending. این باعث می‌شود بعد از sync عدد به صفر برسد و پیام «همه‌چیز به‌روز است» نشان داده شود.
- این تغییر فقط در لایه‌ی سرویس است؛ هیچ migration یا تغییر DB لازم نیست. `PendingCommandSummary` دست‌نخورده می‌ماند (سازگار با همه‌ی callerها و تست‌ها).

---

## تسک‌ها

- [x] **T0 — ساخت پوشه Plan + این فایل todo.** (این فایل) — پوش شود.
- [x] **T1 — فیکس شمارش pending در `LocalSyncStatusService`.** انجام شد.
  - helper `RetriableCount(summary) = max(0, Total - Rejected - Conflict - NeedsAttention)` اضافه شد.
  - `PendingChangeCount` snapshot حالا از `RetriableCount` پر می‌شود.
  - `MapState` حالا `SyncState.Pending` را فقط وقتی `RetriableCount>0` برمی‌گرداند.
  - کامنت XML چرایی فیکس اضافه شد. — پوش شد.
- [x] **T2 — بازتاب در متن UI (`TopbarViewModel`).** بررسی شد؛ تغییر کد لازم نبود.
  - چون `PendingChangeCount` حالا retriable و `State` وقتی retriable=0 برابر `Synced` است، پیام‌های موجود خودکار درست شدند:
    - بعد از sync موفق با pendingBefore>0 → «N تغییر ارسال و همگام‌سازی شد».
    - بعد از sync موفق با pendingBefore=0 → «همه‌چیز به‌روز است».
    - حالت idle با retriable>0 → «N تغییر ارسال‌نشده» (کهربایی).
  - بررسی شد که هیچ UI دیگری شمارش جداگانه‌ی NeedsAttention/Rejected از ژورنال نمی‌خواند؛ پس فیکس خودکفاست و wiring اضافه لازم نیست. — پوش شد.
- [x] **T3 — تأیید صحت تغییر (بدون full test/build طولانی).** انجام شد.
  - در این sandbox دات‌نت نصب نیست (`which dotnet` → خالی)؛ طبق قانون «بیلد/تست طولانی ممنوع» و وجود CI گیت‌هاب‌اکشن، بیلد به CI سپرده شد.
  - بازبینی دستی: `ImplicitUsings` فعال است → `System.Math` در دسترس؛ `PendingCommandSummary` از `Animora.Application.UseCases.Sync` قبلاً import شده (خط ۳)؛ فیلدهای `Total/Rejected/Conflict/NeedsAttention` روی record موجودند؛ `summary.Value!` مثل قبل dereference می‌شود. تغییر syntactically/semantically درست است.
  - پوش شد (CI اجرا می‌شود).
- [ ] **T4 — تسک پایانی: حذف همین فایل todo از پوشه Plan.** (فقط همین فایل؛ به بقیه‌ی فایل‌ها و roadmap کاری نداشته باش.) — پوش شود.

---

## یادداشت‌های اجرا
- `scripts/git/push.sh` اگر exit code 3 داد → ابزار `setup_github_environment` یک بار صدا زده شود، بعد دوباره همان دستور.
- شاخه‌ی کاری فعلی `main` است (ریموت `Arian-Alijani/Petjan`).
