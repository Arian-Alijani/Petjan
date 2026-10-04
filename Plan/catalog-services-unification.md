# Plan — یکپارچه‌سازی کالا و خدمات + تب «نرخ خدمات» در فروش و صندوق

> **Push rule (locked):** after EVERY task below: `scripts/git/push.sh "type(scope): msg"` (exit 3 → call `setup_github_environment`, re-run). Heavy code mid-task → push too.
> **No full test / long build.** Compile only: `scripts/sandbox/build.sh` (Desktop+refs, 0 warn/0 err). Use `build.sh full` only if tests/Domain-only projects were touched. dotnet installed via `scripts/sandbox/install-prereqs.sh` (done once per sandbox). dotnet-ef at `.sandbox/tools` (if missing: `dotnet tool install dotnet-ef --version 10.0.12 --tool-path .sandbox/tools`).
> Status legend: ⬜ todo · ✅ done

## 0. Facts already discovered (do NOT re-investigate)

Data flow: View → VM → `Animora.Contracts` iface → `Animora.Client.Services` → dispatcher → `Animora.Application` handler → port in `Application/Ports` → impl `Animora.Persistence.Sqlite/Queries|Repositories`. Register queries/commands in `Application/UseCases/<Area>/<Area>Registration.cs`; ports in `Persistence.Sqlite/Context/LocalStoreServiceCollectionExtensions.cs` (~L86-121); Desktop services in `Desktop/App.axaml.cs` (~L342-363, all `AddSingleton`).

### Three separate catalog queries (the root of the inconsistency)
| Surface | Query chain | Current behaviour / BUG |
|---|---|---|
| Sales/صندوق picker (`Pages/Sales/CatalogModalViewModel` ctor #1) | `SalesQueryService.GetCatalogAsync` → `SalesCatalogQuery` → `Persistence/Queries/SalesQueries.GetCatalogAsync` (L138) | INNER JOIN `ProductCategory` (soft-deleted/missing category ⇒ product vanishes); one row per active **variant** (id = VariantId); `Take(500)`; + `BoardingServiceDef` rows (Category "Boarding"). Mapping `SalesMapping.ToContract(SalesCatalogReadModel)` L202 builds chips all/svc/item + per-category. Cashier page `CashierService.GetCatalogItemsAsync` reuses same query. |
| Visit «افزودن خدمات/کالا» (`Pages/Visits/VisitEncounterViewModel` L217, L250-310 → shared CatalogModalViewModel ctor #2) | `VisitEncounterService.GetCatalogAsync` (L59) → `RxCatalogQuery` → `Persistence/Queries/RxCatalogQueries.cs` | id = **Product.Id**, price = `DefaultSalePrice`; `Take(200)` by name ⇒ items past 200 hidden. **Client cache `TenantScopedCache` key `tenant|user|rxcatalog|v1` never invalidated ⇒ a product added in انبار never appears in visits until app restart** (main reported bug). |
| Boarding add-item (`Pages/Boarding/BoardingCatalogModalViewModel` → `GetSelectableItemsAsync`) | `BoardingSelectableItemsQuery` → `Persistence/Queries/BoardingQueries.GetSelectableAsync` L57 + `FindSelectableAsync` L68 (used by `AddStayItemCommandHandler`, `Application/UseCases/Boarding/BoardingCommands.cs` L121) | **Filters Kind ∈ {Service, Consumable, Medicine} only.** Inventory «کالای جدید» always creates `ProductKind.PetShop` (`InventoryQueryService.CreateProductAsync` L147) ⇒ every user-created product is invisible in پانسیون (main reported bug). Food/Vaccine/Equipment also hidden. |

Line resolution when billing: `Persistence/Repositories/SalesDraftData.FindCatalogEntryAsync` matches `v.VariantId == id || p.Id == id` then `BoardingServiceDef.Id` ⇒ all 3 id styles are billable; keep this.

### Services today
- No real "service" catalogue exists: `CreateProductCommand` rejects `ProductKind.Service` (`ServiceNoStock`, "use the catalogue editor" — editor never built). Seed has **0** Service products ⇒ visits' «خدمات» filter is empty (reported "can't see services").
- `Domain/Boarding/BoardingServiceDef` (Code, Title, UnitPrice Money, Billing PerStay/PerDay/PerUse, IsActive; `Create/Update/Deactivate/Activate`) — seeded with ENGLISH titles in `Seed/LocalSeeder.cs` L91-98 (WALK/DIET/BATH/GROOM/PLAY/CARE); only read (`BoardingServicesQuery`); NO create/update command, NO repository. Boarding settings tab only lists them (`BoardingPageViewModel.ServiceRows` L279).
- `Product.Create(... trackStock:false ...)` allows Service; variants optional when `trackStock=false`, but Sales catalog needs ≥1 variant ⇒ create services WITH one DEFAULT variant (`ProductVariant.DefaultAttributeName="DEFAULT"`, unit "PCS"). `Product.SetPrices`, `Describe`, `Deactivate/Activate`, `UpdateVariant` exist. Category for services: seeded code `SRV` (DisplayName «خدمات»), id resolve via `db.Set<ProductCategory>().Where(c=>c.Code=="SRV")`.
- Permissions (`Domain/Identity/PermissionCodes.cs`): use `SettingsWrite` (Manager only by default) for rate editing; read with `SalesRead`. CommandTypes in `Contracts.Wire/Commands/CommandTypes.cs` — new constants MUST be added to `All` list too. Audit attr: `[Auditable(AuditCategory.Configuration, CommandTypes.X)]`, commands implement `ICommand<T>, IJournaledCommand` (see `CategoryCommands.cs` pattern).
- Desktop change bus: `IDataChangeNotifier.Notify(DataDomain.Inventory/Sales/Boarding/Visits…)` (`Desktop/Services/IDataChangeNotifier.cs`); Inventory page notifies `Inventory, Dashboard` after writes (L587).

### Sales page tabs
`Pages/Sales/SalePageViewModel.cs`: `_workspace` "cashier"/"history", `SetWorkspace(key)` L189, `IsCashierTab/IsHistoryTab` L103-105, `History` child VM lazily `EnsureLoaded()`. View `SalePageView.axaml` L345-376: `Grid ColumnDefinitions="*,*"` with two `Border.wt-item > Button.wt-tap` (icons IconCart / IconHistory); history surface at L1117 `<sales:SaleHistoryView DataContext="{Binding History}" IsVisible=...IsHistoryTab>`. Converters in `SalePageView.axaml.cs` take bool isHistory (corner/margin) — for a 3rd tab bind them to a new `IsFlushTab` (history||rates) property. Icons available: IconTag, IconPercent, IconClipboard, IconEdit, IconTrash, IconPlus. Modals: `_dialogs.ShowModal(vm, maxWidth)`; every modal VM↔View pair must be registered in `Desktop/Shell/ViewFactory.cs` (`Register<VM, View>(factories);`). Error toasts: `PatientsPresentation.CommandFailureMessage(ex, fallback)`; Persian digits: `SalePresentation.Fa/Money`.

## Tasks

### T1 ✅ Fix catalog visibility (backend-only, small)
1. `BoardingQueries.GetSelectableAsync` + `FindSelectableAsync`: drop the Kind filter → every active product (`Service` → "ClinicService", else "ClinicItem"). Take(500) keep.
2. `RxCatalogQueries.GetAsync`: Take(200) → 1000.
3. `VisitEncounterService.GetCatalogAsync`: remove the never-invalidated cache (delete `_catalog` field + `TenantScopedCache` using) — always query (bounded, cheap). Also include active `BoardingServiceDef`? NO (visits = clinic only).
4. `SalesQueries.GetCatalogAsync`: LEFT JOIN category (`join c ... into cs from c in cs.DefaultIfEmpty()`, label `c == null ? "سایر" : c.DisplayName`), Take(500)→1000.
5. `build.sh` → push `fix(catalog): show every active product in boarding/visit/sales pickers`.

### T2 ✅ Backend: service-rate catalogue (Application + Persistence)
- New file `Application/UseCases/Sales/ServiceRateCommands.cs`:
  - `ServiceRateReadModel(string Id, string Source /*"Clinic"|"Boarding"*/, string Title, long PriceMinor, bool IsActive, string? Billing)`.
  - `ServiceRatesQuery : IQuery<IReadOnlyList<ServiceRateReadModel>>` `[RequiresPermission(SalesRead)]` → port `IServiceRateQueries.GetAsync` (new file `Application/Ports/IServiceRateQueries.cs`; impl `Persistence/Queries/ServiceRateQueries.cs`: Products Kind==Service (incl. inactive, not Deleted) + all BoardingServiceDefs, ordered Source,Title).
  - `CreateServiceRateCommand(string Title, long PriceMinor) : ICommand<string>` → handler: resolve SRV category id via port method `FindServiceCategoryIdAsync` (fallback first active category); `Product.Create(tenant, sku:"SRV-"+8hex, name, catId, ProductKind.Service, "PCS", Money.Irt(0), Money.Irt(price), [new ProductVariant(IdGenerator.NewId(now), sku+"-1", DEFAULT, sku, Irt(0), Irt(price), new Quantity(0,"PCS"), null, true)], trackStock:false)`; `IProductRepository.Add`.
  - `UpdateServiceRateCommand(string Id, string Source, string Title, long PriceMinor, bool IsActive)`: Clinic → product via `IProductRepository.FindAsync`, `Describe(title, brand, notes)`, `SetPrices(cost, Irt(price))`, update each variant `with { SalePrice = Irt(price) }` via `UpdateVariant`, Activate/Deactivate. Boarding → new `IBoardingServiceRepository` (FindAsync tracked; impl in Persistence/Repositories, register AddScoped) → `Update(title, Irt(price), existing.Billing)` + Activate/Deactivate.
  - `CreateBoardingServiceRateCommand(Title, PriceMinor, BoardingServiceBilling)` → `BoardingServiceDef.Create(tenant, "SVC-"+6hex, ...)`, repository Add.
  - All write commands `[RequiresPermission(PermissionCodes.SettingsWrite)]`, `[Auditable(AuditCategory.Configuration, CommandTypes.SalesServiceRateCreate/Update)]`; validators (Required Title, MaxLength 256, Price ≥ 0). Add constants `SalesServiceRateCreate="sales.service-rate.create"`, `SalesServiceRateUpdate="sales.service-rate.update"` to CommandTypes + `All`.
  - Register in `SalesRegistration.cs` (AddQuery/AddCommand/AddValidator), ports in LocalStoreServiceCollectionExtensions.
- Seed: in `LocalSeeder` add new marker block `"sales.services.default"` creating clinic service products: «ویزیت عمومی» 500_000, «ویزیت تخصصی» 900_000, «تزریق» 150_000, «پانسمان» 250_000, «سونوگرافی» 1_200_000, «جراحی سرپایی» 3_000_000 (only for existing installs too — marker guarded, runs on every start). Also add data migration like `Migrations/20261004120000__027_PersianCategoryNames.cs` (attribute-only, no Designer) renaming BoardingServiceDefs Titles WALK→«پیاده‌روی اضافه», DIET→«رژیم غذایی ویژه», BATH→«حمام», GROOM→«آرایش و نظافت», PLAY→«بازی», CARE→«مراقبت ویژه» (WHERE Title=English) and change seed titles to Persian.
- Push `feat(sales): service-rate catalogue commands, queries and default services`.

### T3 ✅ Contracts + Client service
- `Contracts/Sales/IServiceRateService.cs`: `Task<IReadOnlyList<ServiceRate>> GetAsync(); Task CreateAsync(NewServiceRate r); Task UpdateAsync(ServiceRate r);` records `ServiceRate(string Id, ServiceRateSource Source, string Title, long Price, bool IsActive)`, `enum ServiceRateSource { Clinic, Boarding }`, `NewServiceRate(ServiceRateSource Source, string Title, long Price)`.
- `Client.Services/Sales/ServiceRateService.cs` (copy QueryAsync/SendAsync pattern of `SalesQueryService` L324-340; throw `InvalidOperationException(error.Code)` on failure as other services do / `ServiceException`). Register `services.AddSingleton<IServiceRateService, ServiceRateService>();` in App.axaml.cs near L356.
- Push.

### T4 ✅ Desktop: «نرخ خدمات» tab
- `SalePageViewModel`: workspace "rates", `IsRatesTab`, `IsFlushTab => IsHistoryTab || IsRatesTab`, child `Rates = new ServiceRatesViewModel(IServiceRateService, toast, dialogs, changes)` (add ctor param; DI resolves), `Rates.EnsureLoaded()` on activation; raise property changes in SetWorkspace.
- View: tab grid → `"*,*,*"`, 3rd button `CommandParameter="rates"`, icon IconTag, text «نرخ خدمات»; switch converters binding to `IsFlushTab`; add `<sales:ServiceRatesView DataContext="{Binding Rates}" IsVisible="{...IsRatesTab}"/>` after history view.
- New `Pages/Sales/ServiceRatesViewModel.cs` + `ServiceRatesView.axaml(.cs)`: toolbar (search box, filter chips همه/خدمات کلینیک/خدمات پانسیون, button «خدمت جدید»), list rows (title, source badge, price `SalePresentation.Money`, active toggle, edit button). Inline edit modal `ServiceRateEditModalViewModel/View` (Title, Price, Source select on create only, IsActive) → save → `Notify(DataDomain.Sales, DataDomain.Boarding, DataDomain.Visits)`, reload. Register modal in ViewFactory. Style: reuse classes from `SaleHistoryView.axaml` toolbar (`sw-panel`, etc.).
- `build.sh` → push `feat(sales): «نرخ خدمات» tab to add/edit clinic & boarding service rates`.

### T5 ✅ Integration check (read-only, short)
- Visit picker: services now listed (seeded) with filter "svc"; new product appears without restart (T1). Boarding picker shows PetShop items + clinic services. Sales picker shows services (DEFAULT variant). Deactivated service hidden everywhere (all queries filter IsActive). Quick `build.sh` only. Push if anything changed.

### T6 ⬜ Delete this plan file
`rm Plan/catalog-services-unification.md` → push `chore(plan): remove completed catalog-services plan`. Touch nothing else in Plan/ or roadmaps.
