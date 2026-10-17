# عیب‌یابی Build روی ویندوز (Visual Studio / `dotnet`)

> دو خطای رایج هنگام build روی ماشین توسعه ویندوز، ریشه‌یابی‌شده و راه‌حل **استاندارد** (بدون خاموش‌کردن قواعد امنیتی پروژه).

---

## ۱) خطای timeout دانلود دیتای آسیب‌پذیری NuGet

```
Error occurred while getting package vulnerability data:
The download of 'https://api.nuget.org/v3-vulnerabilities/.../vulnerability.base.json'
timed out because no data was received for 60000ms.
```

### ریشه
این پروژه طبق `STACK.md §11` / `R7` / `T-RULE 015` با `NuGetAuditMode=all`
کار می‌کند و این قاعده **اجباری** است (release manifest اگر خاموش باشد fail
می‌شود). با روشن‌بودن audit، هر `dotnet restore` دیتابیس آسیب‌پذیری را دانلود
می‌کند. وقتی هیچ منبع audit جداگانه‌ای تعریف نشده باشد، NuGet از فید **کامل**
`api.nuget.org/v3/index.json` استفاده می‌کند که سنگین است و روی شبکهٔ کند/پشت
پراکسی/فیلترشده timeout می‌دهد. این یک مشکل **محیطی/شبکه** است، نه نقص کد.

### راه‌حل (اعمال‌شده در مخزن)
فایل `nuget.config` در ریشهٔ مخزن اضافه شده که audit را به endpoint سبکِ
**«فقط دیتای آسیب‌پذیری»** یعنی `https://data.nuget.org/v3/index.json` هدایت
می‌کند (توصیهٔ رسمی مایکروسافت). audit **روشن می‌ماند**؛ فقط منبعِ داده‌اش
سبک‌تر و سریع‌تر می‌شود، پس timeout رفع می‌شود.
مرجع: <https://learn.microsoft.com/nuget/concepts/auditing-packages>

> نکته: این خطا به‌صورت `NU1900` (اختلال ارتباط با منبع audit) ظاهر می‌شود و
> **هشدار** است نه خطای build؛ اما روی شبکهٔ کند، تکرارش restore را کند/ناپایدار
> می‌کند. فایل `nuget.config` همین را برطرف می‌کند.

اگر ماشین شما **کاملاً آفلاین** است و حتی به `data.nuget.org` هم دسترسی ندارد،
restore را فقط برای همان نشست به‌صورت آفلاین اجرا کنید (قواعد مخزن را تغییر ندهید):

```powershell
dotnet restore --disable-parallel
# یا موقتاً فقط برای همین ترمینال، بدون commit:
$env:NuGetAudit = "false"   # صرفاً لوکال و موقت؛ هرگز در فایل‌های مخزن ننویسید
```

---

## ۲) خطای قفل‌شدن فایل `obj/.../*.editorconfig`

```
The process cannot access the file
'...\obj\Debug\net10.0\Animora.Contracts.GeneratedMSBuildEditorConfig.editorconfig'
because it is being used by another process.
```

### ریشه
این فایل را MSBuild هنگام build تولید می‌کند. این خطا یعنی **دو فرآیند build
هم‌زمان** روی یک پوشهٔ `obj` می‌نویسند. معمول‌ترین علت‌ها:

1. هم‌زمان build در Visual Studio **و** `dotnet build` از ترمینال.
2. سرویس زبانِ VS Code / Rider (OmniSharp/Roslyn) که در پس‌زمینه در حال
   build/restore است، در حالی‌که شما build دیگری اجرا می‌کنید.
3. آنتی‌ویروس یا ابزار همگام‌سازی ابری (OneDrive/Dropbox) که پوشهٔ پروژه را
   اسکن/قفل می‌کند — اینجا مسیر `Desktop\Projects\...` زیر OneDrive است.

### راه‌حل استاندارد
- فقط **یک** build را هم‌زمان اجرا کنید؛ قبل از `dotnet build` در ترمینال،
  build در Visual Studio را متوقف/تمام کنید (و برعکس).
- اگر گیر کرد، فرآیندهای معلق MSBuild/VBCSCompiler را ببندید و پاک‌سازی کنید:
  ```powershell
  dotnet build-server shutdown
  taskkill /IM MSBuild.exe /F  2>$null
  taskkill /IM VBCSCompiler.exe /F  2>$null
  # سپس obj/bin را پاک کنید (در .gitignore هستند، امن است):
  Get-ChildItem -Recurse -Directory -Include obj,bin | Remove-Item -Recurse -Force
  ```
- پروژه را **بیرون از پوشهٔ OneDrive/Dropbox** نگه دارید، یا پوشهٔ `obj`/`bin`
  را از همگام‌سازی ابری و اسکن بلادرنگ آنتی‌ویروس مستثنا کنید.

> `obj/` و `bin/` از قبل در `.gitignore` هستند، پس این‌ها هیچ‌وقت commit
> نمی‌شوند و پاک‌کردنشان بی‌خطر است.
