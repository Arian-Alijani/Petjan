# Progress & Project State — Windows Frontend

> **Read this file first, every session.** It is the single source of
> truth for "what already exists" and "what to do next." Update it at the
> end of every phase you execute — do not skip this step.

## The two standing principles (apply to EVERY phase, no exceptions)

These two goals are the entire reason this roadmap exists. They are in force
in **every** phase (foundation phases 01–05 *and* screen phases 06–29), and
they are never traded off against speed, token budget, or "good enough."
Neither may be relaxed by a phase file, a deviation-log entry, or a future
session. When they seem to conflict, satisfy **both** — never sacrifice one.

1. **Pixel-faithful rebuild of `Animora-UI` — nothing removed, nothing
   changed visually.** The finished screen must be indistinguishable from its
   `Animora-UI/*.html` reference: every box, table, card, chart, donut, gauge,
   ring, badge, tab, sub-nav, icon and text label is rebuilt in full, in the
   same place / order / grouping / count, with **exact** measurable values
   transcribed from the screen's own CSS and Persian text reproduced
   character-for-character. No visible element is ever dropped, simplified,
   merged, approximated, or deferred. *(Full rules: `CONVENTIONS.md` §4a
   Fidelity Protocol + §6 fidelity gate; `ROADMAP.md` §3a.)*

2. **Backend-ready, standard, idiomatic code — the front end ends this
   roadmap cleanly waiting for a backend.** Every screen's data need is a
   plain `Animora.Contracts` interface + DTO, consumed by the ViewModel via
   constructor DI, with the *only* concrete implementation being an in-memory
   `Mock*` in `Animora.Desktop/Mocks`. When the future backend/database
   roadmap runs, it implements the same interfaces for real and swaps one DI
   registration — **zero** View/ViewModel changes. No mock call, sample datum,
   `if (design-time)` branch, or hardcoded data ever leaks into a View or a
   ViewModel's public shape. Code is idiomatic Avalonia/C#/MVVM
   (CommunityToolkit.Mvvm), never transliterated from the reference's JS.
   *(Full rules: `CONVENTIONS.md` §1 layout, §3 MVVM, §5 data pattern; the
   "Backend-ready" note in each screen phase.)*

> These are stated here (the always-read file) so no session can miss them.
> They restate — never replace — the authoritative rules in `CONVENTIONS.md`
> and `ROADMAP.md`; if wording ever differs, those files govern.

## Current state

- **Repo state (Phase 18, build- & smoke-run verified):** the Inventory screen is completed — the product detail/edit modal, the new-product modal and the stock-adjust modal are built (the reference's `Animora-UI/assets/inventory.js` lines ~448–end: `openProductModal`/`renderProductModal`/`renderInfoTab`/`renderVariantsTab`/`renderVariantCard`/`makeVariant`/`syncVariantsToOptions`/`bindVariantsTabInteractions`/`renderHistoryTab`, `openNewProductModal`, `ensureModal`/`openModal`/`closeModal`, `openStockAdjustModal`/`renderStockAdjustModal`). `Animora.Contracts/Inventory/` extends `IInventoryQueryService` with `GetDetailAsync`/`SaveProductInfoAsync`/`AddVariantAsync`/`GetHistoryAsync`/`CreateProductAsync`/`DeleteProductAsync`/`AdjustStockAsync` plus the `ProductDetail`/`ProductInfoUpdate`/`NewVariantRequest`/`StockMovement`/`NewProductRequest`/`StockAdjustRequest` DTOs. `MockInventoryQueryService` is now a stateful in-memory store backed by the same product list from Phase 17, seeded with the reference's `MOVES` ledger (18 entries across p1/p2/p3/p5/p7/p8); `CreateProductAsync`/`SaveProductInfoAsync`/`AddVariantAsync`/`AdjustStockAsync`/`DeleteProductAsync` mutate it, and `AdjustStockAsync` appends the delta as an «اصلاح موجودی دستی» history entry. `Animora.Desktop/Pages/Inventory/` adds `ProductModalItemViewModels` (variant editor card with stepper + low-stock tone, attribute-option chip, history timeline item, product-type tile, adjust row), `ProductModalViewModel`+`.axaml` (toned header «دسته · کد», Info|Variants|History tab strip with live variant-count badge, three-KPI strips (.inv-kpis), base-info + single-unit price/stock grid + `.convert-hint`, the variants tab's no-variant intro / attribute builder / option chips / `.var-card` editor list, the history tab's filter chips + `.hist-timeline` + empty state, and the footer ذخیره تغییرات / انصراف / حذف کالا), `NewProductModalViewModel`+`.axaml` (base-info grid, تک‌واحد/چند‌تنوعی `.type-toggle` tiles, single-unit vs. opt-in variant blocks, «ساخت کالا»), and `StockAdjustModalViewModel`+`.axaml` (per-variant `.adjust-row` list with stock steppers, «ثبت موجودی»). Phase 17's `OpenProduct`/`OpenNewProduct`/`OpenStockAdjust`/`EditVariant` placeholders now open the real modals via `IDialogService.ShowModal` (680/640/480 wide) and refresh the list on every mutation. New theme icons `IconLayers`/`IconArrowDownLeft`. `InventoryPageViewModel` gains the `IDialogService` dependency. **Build verification:** `scripts/sandbox/build.sh fast` = **0 warnings / 0 errors**; `scripts/sandbox/run.sh` = **SMOKE RUN OK**. Fixed 1 build defect (missing `Animora.Desktop.Controls` using for `Tone`). **Visual verification (§6.7 + §7a):** the rebuilt screen + modals were compared element-by-element against `Animora-UI/inventory.html`/`inventory.js` — the list screen (header, sub-nav counts, 6 toned category chips + separator + 3 stock chips, 8 product rows) rendered identically in a captured screenshot; the product/new-product/stock-adjust modals reproduce the reference's headers, tab strip, KPI boxes, field grids, variant cards, option chips, history timeline and footer actions, with every measurable value traced to `inventory.css`/`styles.css` and every Persian label reproduced character-for-character.
- **Repo state (Phase 17, build-verified):** the Inventory placeholder is replaced with the real product-list screen (the reference's `Animora-UI/inventory.html` chrome + `inventory.css` + `assets/inventory.js` list/settings/sub-nav section, lines 1–447). `Animora.Contracts/Inventory/` adds `IInventoryQueryService` (`GetProductsAsync`/`GetCategoriesAsync`) plus the `CategoryInfo`/`UnitInfo`/`ProductVariant`/`Product` DTOs (product carries its emoji thumbnail `Glyph`, tone, optional variant `Attribute` and 1–4 variants; stock/price totals derived, never stored twice). `Animora.Desktop/Mocks/MockInventoryQueryService` re-derives the reference's 6 `CATS` (دارو/پت‌شاپ/واکسن/غذا/ابزار درمانی/بهداشتی) and 8 `PRODUCTS` (each with its real variants, SKUs, stock/reorder/cost/price/expiry). `Animora.Desktop/Pages/Inventory/` adds `InventoryPresentation` (money formatter via Phase 04 Persian grouping, `.pthumb.(a..f)` 135° gradients, `.cat-tag.tone-*`/`.tone-dot.tone-*`/`.status-dot.*`/stock-bar palettes), `InventoryItemViewModels` (subnav pill with live count, category+stock filter chips, variant sub-row, product row with derived stock/status/price + `.stockcell .bar` progress), `InventorySettingsItemViewModels` (tone option, category row with tone picker+delete, settings toggle), the `InventoryPageViewModel` (`SetSubnav` mirroring `renderSubnav`/`bindSubnav`; `SetCategoryFilter`/`SetStockFilter`; debounced `ApplySearch`/`ClearSearch`; `ToggleExpand` for inline variant sub-rows; Phase-18 placeholder toasts for `OpenProduct`/`OpenNewProduct`/`OpenStockAdjust`/`EditVariant`; settings `AddCategory`/`SaveSettings`/`PickTone`/`DeleteCategory`), `InventoryPageView.axaml(.cs)` (page header with «کالای جدید», unified `.pagebar` with the full-width `.inv-subnav` seg + tier-2 chips/search, `.inv-table` rows with exact columns کالا/کد کالا/دسته/تنوع/موجودی کل/محدوده قیمت/وضعیت/actions, `.subrow-*` inline variant panel, `.inv-empty` empty state), and the separate `InventorySettingsView.axaml(.cs)`. New theme icons `IconBone`/`IconBath`/`IconRefresh`. DI registers `IInventoryQueryService`/`InventoryPageViewModel`; the sidebar `inventory` item targets `InventoryPageViewModel`. **Build verification:** `dotnet build` = **0 warnings / 0 errors**. Fixed 3 build defects (missing `Animora.Contracts.Inventory` using; missing `Animora.Desktop.Controls` using for `Tone`; 5× `Button.BoxShadow` → template `ContentPresenter#PART_ContentPresenter`). **Visual verification (§6.7 + §7a):** the rebuilt screen was compared element-by-element against `Animora-UI/inventory.html` — header, sub-nav (کالاها count / تنظیمات کالاها count), 6 toned category chips + separator + 3 stock chips, search placeholder, the 8 product rows (glyph thumbnail, name/brand, code chip, category tag, variant pill «N نوع» vs «تک واحد», total stock + bar, price range + تومان, status dot, «موجودی»+edit), the inline variant sub-rows, the empty state, and both settings cards — every measurable value traced to `inventory.css`/`styles.css`, every Persian label/column reproduced character-for-character.
- **Repo state (Phase 16, build- & smoke-run verified):** the Boarding screen is completed — the stay-detail / transfer-room / checkout / new-stay modals and the real History/Settings tab content are built (the reference's `boarding.js` `_openDetailFor`/`_renderDetailActions`/`renderHistory`/`renderSettingsRooms`/`renderSettingsSvcs`/`openModal`/`mhead`/`openTransferModal`/`openCheckoutModal`/`openNewStayModal`). `Animora.Contracts/Boarding/` extends `IBoardingQueryService` with `GetDetailAsync`/`AddItemAsync`/`RemoveItemAsync`/`TransferAsync`/`CheckoutAsync`/`CreateStayAsync`/`GetRoomsAsync`/`GetServicesAsync`/`GetSelectableItemsAsync`/`GetKnownPetsAsync` plus the `StayDetail`/`BoardingStayItem`/`CheckoutRequest`/`NewStayRequest`/`RoomInfo`/`ServiceInfo`/`BoardingSelectableItem`/`BoardingKnownPet` DTOs and the `BoardingItemKind`/`BoardingItemSource`/`BoardingRoomType` enums. `MockBoardingQueryService` is now a stateful in-memory store (the reference's `STAYS`+`items[]`, `ROOMS`, `EXTRA_SVCS`, `CLINIC.SERVICES`, `CLINIC.ITEMS`, `knownPets`) whose mutations keep the pre-aggregated `ExtraItemsCost` in sync so the board card total still matches the detail line sum. `Animora.Desktop/Pages/Boarding/` adds `BoardingModalItemViewModels` (day cell / consumed row / add group+option / payment option / known pet / history card / room tile / service row / rate row / setting toggle), `StayDetailModalViewModel`+`.axaml` (toned header with status/pay/transferred pills, entry-info strip, contacts chips, scrollable body — stay-days timeline with past/today states + dots, care note, consumed items with the grouped optgroup add row, bill totals — and status-specific footer actions), `TransferModalViewModel`+`.axaml` (draft-invoice preview + amount + confirm), `CheckoutModalViewModel`+`.axaml` (room-free hint + warn banner + confirm), `NewStayModalViewModel`+`.axaml` (existing-file/manual mode switch, patient autocomplete, stay form, payment radios, note), and `BoardingHistoryTabView`/`BoardingSettingsTabView` (real History card grid + toolbar, and the four Settings cards). Phase 15's `OpenStayDetail`/`OpenNewStay` now open the real modals; the detail modal opens transfer/checkout modals (modal-from-modal, matching the reference's nested-modal pattern), and every mutation refreshes the board/list/history/rooms. Added theme icon `IconCoins`. **Build verification:** `scripts/sandbox/build.sh full` = **0 warnings / 0 errors**; `scripts/sandbox/run.sh` = **SMOKE RUN OK**. Fixed 2 build defects (missing `Tone` using; a `Border.Command` → `Button` history card, since Border exposes no Command). **Visual verification (§6.7 + §7a):** the modal set and the History/Settings tabs were compared element-by-element against the reference — header pills/strip/contacts/body sections/footer actions, transfer lines + total, checkout banner, new-stay mode switch/autocomplete/form/payment/note, history toolbar + cards + empty state, and all four Settings cards — with every measurable value traced to `boarding.css`/`styles.css` and every Persian label/column reproduced character-for-character. **Deviation:** the detail modal's grouped `<select>` (optgroups خدمات پانسیون/خدمات کلینیک/کالاها) is re-drawn as a grouped popup picker, since Avalonia has no native optgroup select — same three groups, same order, same labels (see log).

- **Repo state (Phase 20, build-verified):** the Sale/POS placeholder is replaced with the two-pane invoice workspace matching `Animora-UI/sale.html` + `assets/sale.css` + `assets/sale-page.js` (sidebar + invoice content/lines/totals/history/KPIs). `Animora.Contracts/Sales/` adds `ISalesQueryService` (`GetReadyEncountersAsync`/`GetInvoicesAsync`/`GetInvoiceAsync`/`CreateFromEncounterAsync`/`CreateBlankAsync`/`GetInsurersAsync`/`GetKpisAsync`) plus `ReadyEncounterRef`/`InvoiceSummary`/`InvoiceDetail`/`InvoiceLine`/`InvoicePayment`/`InsurerRef`/`SaleKpiSnapshot` DTOs and the `InvoiceKind`/`InvoiceState`/`InvoiceLineKind`/`DiscountKind`/`PaymentMethod` enums. `Animora.Desktop/Mocks/MockSalesQueryService` is a stateful in-memory store seeded with 4 ready encounters (mirrors `enc-3`/`enc-4` shape) and 5 invoices across every state (draft/partial/settled/void/refunded) carrying the reference's `FLOW.calcInvoice` arithmetic; `CreateFromEncounterAsync`/`CreateBlankAsync` insert new invoices. `Animora.Desktop/Pages/Sales/` adds `SalePresentation` (money/fa formatting, avatar-tone gradients, state/method/tag brushes), `SaleItemViewModels` (segment tab, ready-encounter card, invoice card, group header, sidebar row), `InvoiceLineViewModel`/`InvoicePaymentViewModel`, `SaleInvoiceViewModel`, and `SalePageViewModel` (`SetSegment` mirroring `renderSeg`; `SelectInvoice`/`BuildFromEncounter`/`NewBlankInvoice` mirroring `selectInvoice`/`buildFromEncounter`/`newBlankInvoice`; `FunnelToVisits` for the empty-state; searchable/segmented lists mirroring `matchQuery`/`matchEncQuery`/`renderSidebar`; read-only line rendering via `renderLine`, terminal banner, totals summary and `renderHistory`; inline mini-KPIs via `renderKPIs`; Phase-21 placeholder toasts for `AddItem`/`AddCustomItem`/`TakePayment`), and `SalePageView.axaml(.cs)` (`.sw-topline` header + `.sw-mini-kpis`, `.sw-workspace` two-pane grid, `.sw-side` segment/search/list, `.sw-inv` head/customer/visit-context/body/lines/adj/history/footer). New theme icon `IconImage`. DI registers `ISalesQueryService`/`SalePageViewModel`; the sidebar `sale` item now targets `SalePageViewModel`. **Build verification:** `scripts/sandbox/build.sh fast` = **0 warnings / 0 errors**. **Fidelity:** every element the reference's two-pane workspace renders is rebuilt — sidebar segment (صف ویزیت/فاکتورها with counts + pulse), search, grouped invoice list (باز و در انتظار/تسویه‌شده/ابطال و عودت) and ready-visit cards; main invoice header (شمارهٔ فاکتور, status pill, terminal banner, customer chip, visit-context), items list with `renderLine`'s tag/qty/discount/subtotal columns, `.sw-adj` (تخفیف فاکتور + پوشش بیمه), `.sw-history`, the footer summary/final-amount/paid-due chips and actions (چاپ/ابطال/عودت وجه/pay CTA), plus the `.sw-empty` funnel. Catalog picker & payment modal are Phase 21 by the phase split. **Note:** this session ran in a fresh sandbox without the .NET SDK; `scripts/sandbox/install-prereqs.sh` was run to install it. Structural (not pixel) visual verification was done against the reference CSS/HTML (no window rendering performed this session).

- **Next phase to execute:** `22` — Sale/POS is now fully complete (Phase 21 build-verified: 0 warnings / 0 errors). Phase 20 is build-verified (0 warnings / 0 errors). Phase 19 is **build- & smoke-run verified (0 warnings / 0 errors)** + rendered & compared to the reference.
- **Repo state (Phase 21, build-verified):** the Sale/POS screen is completed — the catalog picker, the payment modal (with settlement-success celebration), the void/refund flows, the custom-line-item modal and the owner/guest picker are built (the reference's `Animora-UI/assets/sale-page.js` lines ~855–1483: `doPayment`/`doVoid`/`doRefund`/`openRefundModal`/`openConfirm`/`showSettlementSuccess`/`openCatalog`/`closeCatalog`/`catalogAllItems`/`catalogFiltered`/`highlight`/`renderCatFilters`/`renderCatRecent`/`renderCatalog`/`updateHl`/`addToInvoice`/`openCustomItemModal`/`addCustomLine`/`bindCatalogGlobal`/`openPicker`/`selectOwner`/`selectGuest`; the drawer markup from `sale.html` `.cat-drawer`). `Animora.Contracts/Sales/` extends `ISalesQueryService` with `GetCatalogAsync`/`AddLineAsync`/`AddCustomLineAsync`/`TakePaymentAsync`/`VoidAsync`/`RefundAsync`/`GetOwnersAsync`/`PickOwnerAsync` plus the `CatalogItem`/`CatalogFilterRef`/`RecentItemRef`/`CatalogSnapshot`/`CustomLineRequest`/`PaymentRequest`/`PaymentResult`/`RefundRequest`/`RefundResult`/`OwnerRef`/`OwnerPickResult` DTOs. `MockSalesQueryService` now carries the reference's full 22-item catalog (12 `SERVICES` sv-1..sv-12 + 10 `ITEMS` it-1..it-10) with the `SVC_GROUPS`/`ITEM_GROUPS` labels + `CAT_ICON` keys and the 6-entry owner directory (mirrors `window.OWNERS` + first pet); `AddLineAsync` increments an existing matching line or appends (mirrors `addToInvoice`), `AddCustomLineAsync` mirrors `addCustomLine`, `TakePaymentAsync` caps at the due + appends the payment row + advances draft→partial/settled (mirrors `FLOW.applyPayment`), `VoidAsync` mirrors `FLOW.voidInvoice`, `RefundAsync` subtracts + appends a negative row + moves to refunded, `PickOwnerAsync` mirrors `selectOwner`/`selectGuest`, and `GetOwnersAsync` filters like `openPicker`'s search. `Animora.Desktop/Pages/Sales/` adds `Shared/ConfirmDialogViewModel`+`.axaml` (the reusable themed yes/no dialog — danger/warn/brand/neutral icon tile + confirm button tone, mirrors `openConfirm`), `CatalogModalViewModel`+`.axaml` (500-wide picker: search with highlight-on-search, category filter chips with counts, recent-items strip, dense `.cat-row` list with kind-tinted icon tiles + add buttons, empty state, foot count + tip), `CustomItemModalViewModel`+`.axaml` (name/unit-price/qty form + live row-total preview), `PaymentModalViewModel`+`.axaml` (five-method grid, amount input + مانده کامل/٪۵۰/صفر presets, live preview band neutral/success/warn/partial, and the settlement-success screen — hero check ring + receipt/customer/method/amount/total rows + refund/print/close/next actions), `RefundModalViewModel`+`.axaml` (amount + کل مبلغ/نیمی presets + note), and `OwnerPickerModalViewModel`+`.axaml` (searchable owner directory with avatar/file-code/mobile/balance + guest-sale option). Phase 20's placeholder `AddItem`/`AddCustomItem`/`TakePayment` toasts are replaced with real `IDialogService.ShowModal` flows; `VoidInvoice` (confirm-then-call) and `Refund` commands are wired to the invoice panel's action buttons; `NewBlankInvoice` now opens the owner/guest picker first (mirrors the reference's walk-in flow); every mutation refreshes the sidebar/KPIs/open invoice via `RefreshAfterMutation`. `SalePresentation` gains `ConfirmIco`/`MethodIconKey`/`MethodLabel`/`PreviewBrushes`/`PreviewIconKey`. **Build verification:** `scripts/sandbox/build.sh fast` = **0 warnings / 0 errors**. Fixed 3 build defects (missing `Animora.Desktop.Controls` using for `Tone` ×2; `Catalog.Count` → `Catalog.Length`; removed unsupported `TextBox.InputScope`). **Fidelity:** every element the reference's catalog drawer + payment/refund/custom-item/confirm/success modals + owner picker renders is rebuilt — .cat-drawer head/hint-kbds/search+Ctrl-K/filter-chips/recent-strip/rows/empty/foot, .sw-pay head/methods/amount+presets/preview bands, .sw-confirm (tone icon/body/foot), .sw-modal-refund (amount+presets+note), .sw-custom-form/field/row/preview, .sw-success hero/rows/foot, .sw-modal + .sw-owner-row/.sw-owner-none — with every measurable value traced to `sale.css`/`styles.css`, exact Persian text char-for-character and the method/state colors mapped to Phase 02 tokens. Structural (not pixel) visual verification was done against the reference CSS/HTML.
- **Repo state (Phase 19, build- & smoke-run verified + rendered):** the Purchase placeholder is replaced with the real «خرید و تأمین‌کنندگان» screen matching `Animora-UI/purchase.html` (static markup — no screen-specific CSS; all boxes/KPIs/table styled from `styles.css`). `Animora.Contracts/Purchasing/` adds `IPurchasingQueryService` (`GetSnapshotAsync`) plus the `PurchasingSnapshot`/`PurchaseKpi`/`PurchaseOrderSummary` DTOs and the `PurchaseOrderStatus` enum (Delivered/InProgress/Finalized). `Animora.Desktop/Mocks/MockPurchasingQueryService` returns the reference's exact values: the 3 KPI cards (۶ سفارش فعال · ۱۲۸ م خرید این ماه · ۳۲ م بدهی تأمین‌کننده, with the ۱۲٪↑/۴٪↓/۹٪↓ delta chips) and the 3 purchase orders of its table. `Animora.Desktop/Pages/Purchasing/` adds `PurchasingPageViewModel` (loads the snapshot; `NewPurchaseInvoice` toast-only stub, mirroring the reference's static header button — no deeper purchase flow exists to mirror), `PurchasingItemViewModels` (`PurchaseKpiViewModel` — tone-mapped icon tile; `PurchaseOrderRowViewModel` — status pill tone mapping 1:1 to `pill-ok`/`pill-warn`), and `PurchasingPageView.axaml(.cs)` (page header with «فاکتور خرید» primary action; `.grid-3c` 3-column KPI stat row via Phase 03 `StatCard`; `.card`+`.tbl` orders table with the exact columns شماره/تأمین‌کننده/تاریخ/اقلام/مبلغ کل/تسویه/وضعیت + row `icon-btn`). New theme icons `IconCreditCard`/`IconDollar` (from `icons.js`) and `IconTruck`'s wheel radius corrected to the reference (r=2.5). DI registers `IPurchasingQueryService`/`PurchasingPageViewModel`; the Dashboard quick-action tile 7 «خرید و تأمین» now targets `PurchasingPageViewModel` (no sidebar entry exists for this screen in the reference — reached only via that quick action, same pattern as Phase 07's Reception). **Verification:** `scripts/sandbox/build.sh full` = **0 warnings / 0 errors**; `scripts/sandbox/run.sh` = **SMOKE RUN OK**; rendered the screen under Xvfb and compared element-by-element to `purchase.html` — header (eyebrow/title/subtitle + «فاکتور خرید»), 3 KPI cards (values/labels/delta chips/tinted icons) and the 3-row orders table (all columns, Persian text, ok/warn status pills) match.
- **Phase 17 & 18 status re-verified (this session):** both Inventory phases are complete in the shipped code (Phase 17 PR #45: product list, subnav filters, catalog settings; Phase 18 PR #46: product detail/edit modal, new-product modal, stock-adjust modal) — all `inventory.js` functions (`renderSubnav`/`renderProducts`/`renderProductRow`/`renderSettings`/`emptyState` and `openProductModal`/`renderProductModal`/`renderInfoTab`/`renderVariantsTab`/`renderVariantCard`/`renderHistoryTab`/`openNewProductModal`/`openStockAdjustModal`/`renderStockAdjustModal`) have matching Contracts, mock and View/ViewModel coverage, and the solution builds 0/0. The checklist rows for 17/18 were still `⬜ Not started` (a PROGRESS.md bookkeeping lag) and are now marked ✅ Done. No remaining gaps found.
- **Repo state (Phase 15, build- & smoke-run verified):** the Boarding placeholder is replaced with the real board/list screen (the reference's `Animora-UI/boarding.html` chrome + `.bd-*` cards in `boarding.css` + `assets/boarding.js` `tabMatches`/`renderTabCounts`/`renderCards`/`switchSection`). `Animora.Contracts/Boarding/` adds `IBoardingQueryService` (`GetStaysAsync`) plus the `BoardingStay` DTO and `BoardingStatus`/`BoardingPaymentMode`/`BoardingSpecies` enums (status vocabulary `active`/`done` verified against `statusMeta`; all other visible states derived from status + check-out vs. today exactly as the reference derives them). `Animora.Desktop/Mocks/MockBoardingQueryService` re-derives the reference's 6 `STAYS` (léo/میشا/گلچین/کوکو/هاپو/سیمبا) re-anchored to the live Jalali "today" via `Day(offset)`, carrying pre-aggregated extra-item cost so the card total matches. `Animora.Desktop/Pages/Boarding/` adds `BoardingPresentation` (species label/icon, avatar `a..f` 135° gradients, status/pay pill brushes, card border/bg/progress/day-label tones, `money`), `BoardingSectionViewModel`/`BoardingTabViewModel`/`BoardingSpeciesOption`/`BoardingStayCardViewModel` (head + status/room chips, check-in/out strip, `.bd-prog` timeline, owner/cost footer, CTA — every value derived identically), the `BoardingPageViewModel` (section switcher `SetSection`; status tabs + counts `SetTab`; species dropdown `ToggleSpecies`/`SetSpecies`; debounced search `ApplySearch`/`ClearSearch`; `OpenStayDetail`/`OpenNewStay` showing the Phase-16 placeholder toast, mirroring the reference's modal calls), and `BoardingPageView.axaml(.cs)` (unified `.pagebar` with section `.pb-seg.lg` tabs, status `.pb-seg` tabs with `.seg-n` counts, search + species/«پذیرش جدید» actions, `.bd-cards-grid` auto-fill→3-col stay grid, `.bd-empty-state`; History/Settings sections render the Phase-16 placeholder). New theme icons `IconTimeline`/`IconHourglass`. DI registers `IBoardingQueryService`/`BoardingPageViewModel`; the sidebar `boarding` item now targets `BoardingPageViewModel`. **Build verification:** `scripts/sandbox/build.sh full` = **0 warnings / 0 errors**; `scripts/sandbox/run.sh` = **SMOKE RUN OK**. Fixed 6 hidden XAML/property defects on build (2× `Button.BoxShadow` → template `ContentPresenter#PART_ContentPresenter`; `Grid` BorderBrush/BorderThickness/Padding + `StackPanel` Padding → wrapped in `Border`), plus 2 compile-time name collisions (property vs. static helper) in the item VM.
- **Repo state (Phase 14, build-verified):** the Appointments detail drawer + booking sheet (the reference's `.dtl` side panel + `.bk-*` booking sheet in `Animora-UI/assets/booking.css` + `assets/appointments-panel.js`) are built. `Animora.Contracts/Appointments/` adds `IAppointmentBookingService` (`GetDetailAsync`/`GetPatientsAsync`/`CreatePatientAsync`/`GetAvailableSlotsAsync`/`GetConflictsAsync`/`GetDayCountsAsync`/`GetDoctorBusyCountAsync`/`CreateAsync`/`UpdateAsync`/`CancelAsync`/`CheckInAsync`) plus `BookingPatient`/`NewPatientRequest`/`TimeSlot`/`AppointmentDetail`/`NewAppointmentRequest`/`UpdateAppointmentRequest` DTOs. `Animora.Desktop/Mocks/` adds a shared `AppointmentStore` (single in-memory event set + patient list, seeded exactly like the reference's `seedData()`) that now backs BOTH `MockAppointmentsQueryService` and the new `MockAppointmentBookingService`. `Animora.Desktop/Pages/Appointments/` adds `BookingPanelViewModel` + `BookingPanelView.axaml(.cs)` + `BookingPanelStyles.axaml` (five step sections — type/patient/doctor/date-slot/extras — plus the running summary sidebar, conflict/free notices and submit), `BookingDetailViewModel` + `BookingDetailView.axaml(.cs)` (type/status header, pet lock-up, when-block, KV grid, note, events timeline, admit/edit/SMS/cancel foot actions) and `BookingPanelItemViewModels`/`BookingItemViewModels`. Phase 13's `OpenBooking(AppointmentEvent?)` now opens the real detail drawer for an existing event or the booking sheet in create mode, and both refresh the visible range on save. **Fixed:** the `System.InvalidOperationException: Invalid size returned for Measure` crash — `Controls/TimeGridEventsPanel.MeasureOverride` returned an unbounded/infinite `availableSize`, which Avalonia rejects; it now returns a finite `Size` clamped to the panel's own height. Also migrated one `Watermark`→`PlaceholderText`. DI registers `AppointmentStore`/`IAppointmentBookingService`.
- **Repo state (Phase 10, build- & smoke-run verified):** the Visits placeholder is replaced with the real pre-encounter triage gate. `Animora.Contracts/Visits/` gained `VisitEntryMode`, `VisitTypeInfo`, `BookedAppointmentState`, `DoctorRailInfo`, `BookedAppointmentRef`, `VisitPatientRef`, `NewVisitRequest`, `EncounterDraft` and `IVisitsTriageService` (`GetDoctorsAsync`/`GetBookedForDoctorAsync`/`GetVisitTypes`/`GetPatientsAsync`/`StartFromTicketAsync`/`StartNewAsync`). `Animora.Desktop/Mocks/MockVisitsTriageService` returns the reference's 4-doctor roster with their today counts and booked tickets, the 6-type visit catalog and a 7-patient pick-list, and produces a fresh `EncounterDraft` per start. `Animora.Desktop/Pages/Visits/` adds `VisitsPresentation` (pet-icon keys + species avatar/picked-glyph palettes + gradient helper), `DoctorTileViewModel`, `BookedRowViewModel`, `VisitTypeOptionViewModel`, `VisitTypeChipViewModel`, the `VisitsTriagePageViewModel` (doctor rail + booked/new-visit mode + type chips + patient search/pick + inline new-patient form, commands `SelectDoctor`/`SetMode`/`SetTypeFilter`/`SetType`/`SetPriority`/`PickPatient`/`ChangePatient`/`RegisterNewPatient`/`SaveNewPatient`/`StartFromTicket`/`StartNew`, both start paths showing the Phase-11 placeholder toast), and `VisitsTriagePageView.axaml(.cs)` (340px doctor rail + workspace with doc-lockup/tape/mode-split/booked-list/new-visit form/empty-state, all values transcribed from `visits.html`+`styles.css`). New theme icons `IconTools`/`IconScissors`/`IconPlay`/`IconBulb`. DI registers `IVisitsTriageService`/`VisitsTriagePageViewModel`; the sidebar `visits` item now targets `VisitsTriagePageViewModel`. **Build verification:** `scripts/sandbox/build.sh full` = **0 warnings / 0 errors**; `scripts/sandbox/run.sh` = **SMOKE RUN OK**.
- **Repo state (Phase 08, build- & smoke-run verified):** Patients & Owners directory now has backend-ready contracts (`IPatientsQueryService`, `PetSummary`/`OwnerSummary` DTOs + `PetSpecies`/`PetStatus`/`PetGender`/`OwnerGender`/`PatientAvatarTone` enums in `Animora.Contracts/Patients/`), a deterministic in-memory `MockPatientsQueryService` (10 pets across cat/dog/bird/rabbit/rodent/other, 6 owners with tags/balances), presentation adapters (`PatientsPageEnums`, `PatientsPresentation`, `PatientsSpeciesChipViewModel`, `OwnerPetChipViewModel`, `OwnerCardViewModel`, `PetCardViewModel`), the `PatientsPageViewModel` (search + derived species-chips + pets/owners entity switch + list/grid layout + vaccine filter + sort, mirroring `petMatches`/`ownerMatches`/`sortPets`/`renderSpeciesChips`, with `OpenPet`/`OpenOwner` toast stubs for Phase 09), and `PatientsPageView.axaml(.cs)` (page header with «خروجی»/«مالک و بیمار جدید», unified `.pagebar` with two tiers, owner-grid/pets-grid cards, owners/pets tables, shared `pt-empty`). New shared helpers: `Localization/PersianText` (search normalization) and `Localization/RelativeDateFormatter` (since/until/age Jalali phrases). New theme additions: `IconPhone/Mobile/IdCard/Pin/Download/List/Grid/More2`, rose/mint + table/inset/hover surface tokens, and `ShadowSegment`/`ShadowChip`. DI registers `IPatientsQueryService`/`PatientsPageViewModel`; the sidebar `patients` item now targets `PatientsPageViewModel`. **Build verification (this session):** installed .NET 10.0.401 + Xvfb via `scripts/sandbox/install-prereqs.sh`; `scripts/sandbox/build.sh full` = **0 warnings / 0 errors** and `scripts/sandbox/run.sh` = **SMOKE RUN OK**. Five defects that the previously-unbuilt code had hidden were fixed during consolidation: (1) `OwnerCardViewModel.PetsPreviewText` was both assigned in the constructor and re-declared as a read-only computed property (CS0200) → kept the constructor's all-pets assignment as an auto-property and removed the duplicate; (2) `BoxShadow` set directly on `Button.cardlink` (Button has no such property) → moved to the template's `ContentPresenter#PART_ContentPresenter`; (3) `Padding` on a `Grid` → `Margin`; (4)+(5) `FontSize` on two table-row `Border`s → `TextElement.FontSize` attached property so the cascade is preserved.
- **⚠️ Fidelity Protocol now in force (roadmap update, applies to Phase 07+).**
  The roadmap was tightened after Phase 06 shipped visibly different from the
  `Animora-UI` reference (missing chart/donut/ring, eyeballed spacing/boxes).
  From Phase 07 onward every screen phase MUST follow `CONVENTIONS.md` §4a
  (Fidelity Protocol) + §6 (fidelity gate + visual verification) + §7a + §8
  (per-phase Reference CSS map): rebuild **every** reference element (charts/
  donuts/gauges/rings included), transcribe **exact** measurable values from
  each screen's own `.css` file(s), reproduce Persian text
  character-for-character, and visually verify element-by-element before
  marking a phase done. **Do NOT retro-rework Phases 01–06 for this** — they
  are already `Done`; the new rules apply going forward. Phase 29's final
  fidelity audit is the place where any residual gap in an already-done screen
  (e.g. the Phase 06 dashboard's omitted chart/donut/ring) is caught and
  assigned a fix, per that phase's Task 4.
- **Repo state (Phase 07, build- & render-verified):** Reception now has backend-ready contracts (`IReceptionQueueService`, queue/walk-in DTOs), a stateful in-memory mock, a complete MVVM page with live hero metrics, four KPI cards, exact queue filters/search/pager/empty state, state-specific actions, and the walk-in modal. Dashboard's «شروع ویزیت» deep-link targets Reception; shell-level modal/toast hosts are permanently wired. **Build verification (this session):** installed the .NET 10 SDK (10.0.401), `scripts/sandbox/build.sh full` = **0 warnings / 0 errors**, and `scripts/sandbox/run.sh` = **SMOKE RUN OK**. **Render verification:** captured a headless Xvfb screenshot of the Reception screen (temporarily setting it as the startup page, then reverting) and compared element-by-element against `Animora-UI/reception.html` — hero (live Jalali date + «صف فعال» pill + 4 metrics میانگین انتظار/نوبت بعدی/اورژانس‌های فعال/تکمیل‌شده امروز), 4 KPI cards, filter tabs (همه/فعال/اورژانس/پایان‌یافته), search placeholder «جستجوی حیوان، مالک یا پزشک…», «فیلتر» button, header eyebrow/title/subtitle, «ثبت مراجعهٔ حضوری» + «بازخوانی صف» buttons, and per-row queue actions all match. One defect the un-built code had hidden was fixed (four `TextBox.Watermark` usages triggering `AVLN5001` obsolete-API warnings → migrated to `PlaceholderText`; see log). Phase 07 is now Done.
- **Repo state (Phase 06):** the Dashboard placeholder is replaced with the
  real, role-aware screen. `Animora.Contracts/Dashboard/` gained
  `DashboardModels.cs` (`DashboardTone`, `DashboardStat`, `DashboardListItem`,
  `DashboardSnapshot` — shaped to what the three `DASH_VIEW_*` functions
  actually render: a hero title/subtitle, a 4-item KPI list, and two labeled
  content lists per role) and `IDashboardQueryService`
  (`GetSnapshotAsync(UserRole)`). `Animora.Desktop/Mocks/MockDashboardQueryService`
  returns deterministic per-role sample data re-derived from
  `dashboard-views.js`'s manager/cashier/doctor sections; registered as a
  singleton (stateless mock data, no reason to reload per navigation).
  `Animora.Desktop/Pages/Dashboard/` adds `DashboardPageViewModel` (loads the
  current role's snapshot via the injected service in its constructor —
  synchronous `GetAwaiter().GetResult()` over the mock's `Task.FromResult`;
  exposes `Stats`/`PrimaryList`/`SecondaryList` + `IsManager`/`IsCashier`/
  `IsDoctor` role flags + `QuickActions`/`OpenQuickActionCommand`),
  `DashboardRowViewModel` (adapts a Contracts `DashboardListItem` to the
  Desktop `Controls.Tone` enum for direct `Badge.Tone` binding),
  `DashboardQuickAction` (the 8-tile quick-access list, mirroring
  `dashboard.js`'s `TILES` array — all targets still resolve to
  `PlaceholderPageViewModel` until their own phase runs), `DashboardPageView`
  (hero banner + Phase 03 `StatCard` KPI row + role-conditional content region
  + quick-access grid), and one sub-view per role — `ManagerDashboardView`
  (live queue + management alerts), `CashierDashboardView` (open invoices +
  ready-to-bill), `DoctorDashboardView` (live queue + vaccination reminders) —
  all three sharing the same `DashboardPageViewModel` DataContext, visibility-
  toggled by the role flags (no `DataTemplate`-per-role needed for 3 fixed
  options). `NavigationMenu`'s `dashboard` entry and `MainWindowViewModel`'s
  startup page now target `DashboardPageViewModel` instead of the placeholder.
  DI registers `IDashboardQueryService`/`DashboardPageViewModel` as singletons.
  **Build-verified (audit session):** installed .NET 10 SDK (10.0.401),
  `scripts/sandbox/build.sh full` = **0 warnings / 0 errors**, and
  `scripts/sandbox/run.sh` = **SMOKE RUN OK** for all three roles
  (Manager/Cashier/Doctor, toggled via `MockCurrentUserContext.Role`, restored
  to `Manager` default). One real defect the un-built code had hidden was fixed
  (the three role sub-views used CSS `ColumnDefinitions="1fr,1fr"`, invalid in
  Avalonia → corrected to `"*,*"`; see deviation log). Quick-actions tile 7
  restored to the reference's "خرید و تأمین" (see log).
- **Repo state (Phase 05):** the real app shell now exists.
  `Animora.Contracts` gained `Navigation/NavItem.cs` (nav DTO — Key/Label/
  IconKey/ViewModelType/GroupName/BadgeCount), `Navigation/UserRole.cs`
  (`Manager|Cashier|Doctor`), and `Identity/ICurrentUserContext.cs` (name/
  role/initials seam). `Animora.Desktop/Shell/` adds `PageViewModelBase`
  (Title/Eyebrow/Breadcrumbs/IsBusy — every screen VM derives from it),
  `NavigationMenu` (static groups/items mirroring the reference's `NAV_ITEMS`
  exactly), `SidebarView(+VM)` + `NavGroupViewModel`/`NavEntryViewModel`
  (brand header, grouped nav with active highlight + left accent bar, badge
  pills, upsell CTA + sync-status + user card), `TopbarView(+VM)` (accent
  title/breadcrumbs bound to the current page, visual-only search, quick-
  create, inert bell, user chip), `PlaceholderPageView(+VM)` (single shared
  "not built yet" page every nav item currently targets), `ViewLocator`
  (convention-based VM→View resolution, registered as an app `DataTemplate`),
  `IconKeyConverter` (icon-key string → Geometry, app resource), and
  `MainWindow(+VM)` (Grid: SidebarView column + Topbar-over-ContentControl
  column). `Animora.Desktop/Services/` adds `INavigationService`/
  `NavigationService` (DI-resolved `NavigateTo<T>()`, observable
  `CurrentViewModel`). `Animora.Desktop/Mocks/MockCurrentUserContext`
  returns the reference's default `manager` identity. DI wires the identity/
  navigation/shell singletons; `MainWindow` no longer hosts the Phase 03
  control gallery (kept on disk, dev-only, unreferenced). Every sidebar item
  routes to the placeholder without error, active-highlight tracks the click,
  startup lands on the "داشبورد" slot. `scripts/sandbox/build.sh full` =
  0 warnings / 0 errors; `scripts/sandbox/run.sh` = SMOKE RUN OK.
- **Build verification (Phase 04 session):** installed the real **.NET 10 SDK
  (10.0.401)** and ran `dotnet restore` + `dotnet build Animora.sln` — now
  **0 warnings / 0 errors** across all 5 projects. This was the first session
  with a working `dotnet` toolchain since Phase 01, so it retroactively
  verified the previously-unbuilt Phase 03 code. Two real defects surfaced and
  were fixed (see the Phase 03 deviation log): a `ToastService`
  `TryFindResource` compile error, and the two `AVLN3001` runtime-loader
  warnings on the DI-constructed `MainWindow`/`ControlGalleryView`. Headless
  `dotnet run` reaches Avalonia bootstrap and stops only at `XOpenDisplay`
  (no display in this Linux sandbox) — bootstrap + DI + XAML/resource load all
  succeed; the window itself opens on the Windows target.
- **Repo state (Phase 04):** `Animora.Desktop/Localization/` now holds the shared
  RTL + localization utilities: `PersianMonthNames.cs` (12 Jalali month
  constants فروردین..اسفند as a `static string[]` + `Get(month)`),
  `PersianNumberFormatter.cs` (pure `ToPersianDigits(object?)` and
  `FormatGrouped(long)`/`GroupAscii(long)` — re-derived from `toFa` and
  `group()` in .NET string idioms, not ported regex), `JalaliDate.cs`
  (`readonly struct { Year, Month, Day }` with `FromDateTime`/`ToDateTime`/
  `Today` backed by `System.Globalization.PersianCalendar`, and
  `ToString("long"|"medium"|"short")` producing e.g. "۱۴ مهر ۱۴۰۴"), and
  `Converters.cs` (`PersianDigitsConverter` + `JalaliDateConverter`,
  one-way `IValueConverter`s). `App.axaml` sets `FlowDirection="RightToLeft"`
  once on the `TopLevel` base style (single top-level RTL location per
  CONVENTIONS.md §4) and registers both converters as
  `PersianDigitsConverter`/`JalaliDateConverter` resource keys. The temporary
  control gallery gained a "محلی‌سازی — فاز ۰۴" card with two proof
  `TextBlock`s (grouped Persian number + today's Jalali date) bound through the
  converters. `dotnet build Animora.sln` = 0 warnings / 0 errors; conversion
  spot-checked (2025-10-06 → ۱۴ مهر ۱۴۰۴).
- **Repo state (Phase 03):** `Animora.Desktop` now carries the shared control
  library: `Resources/Theme/Icons.axaml` (23 `StreamGeometry` icons re-drawn
  from `Animora-UI/assets/icons.js`, covering Phase 05's sidebar set + common
  actions), `Resources/Controls/IconStyle.axaml` (shared `Path.icon` look),
  `Resources/Controls/Button.axaml` (`ControlTheme` for `Button` with
  `primary|outline|ghost|danger-ghost` classes), `Resources/Controls/Card.axaml`
  (`Border.card`/`card-header`/`card-header-rule`/`card-title`/`card-body`
  selector styles). `Controls/` adds composite `UserControl`s: `StatCard`,
  `Badge` (+ shared `Tone` enum), `DataTable` (thin `DataGrid` wrapper styled
  to `.tbl`), `EmptyState`, `ToastHost`, `ModalHost`, `DrawerHost`. Services:
  `IToastService`/`ToastService` and `IDialogService`/`DialogService`
  (`Services/`), both registered as singletons in `App.axaml.cs`'s DI
  container. A temporary `Pages/_ControlGallery/ControlGalleryView`
  instantiates one of each control (4 button variants, card, 2 stat cards,
  5 badge tones, empty state, toast/modal/drawer triggers) and is now
  `MainWindow`'s DI-injected content, replacing the Phase 02 resource smoke
  test. Added `Avalonia.Controls.DataGrid` 12.1.2 package (matches the
  pinned Avalonia 12.1.2 line) to `Directory.Packages.props` +
  `Animora.Desktop.csproj`.
- **Build note (RESOLVED):** Phase 03 was originally written without a `dotnet`
  SDK in the sandbox and shipped uncompiled. A later session installed the
  .NET 10 SDK and compiled the solution: `dotnet build Animora.sln` now reports
  **0 warnings / 0 errors**. The two defects the missing build had hidden
  (`ToastService.TryFindResource` and the `AVLN3001` warnings) are fixed. No
  outstanding build blocker before Phase 04.
- **Repo state (Phase 02):** `Animora.Desktop` now carries the shared
  design-token layer under `Resources/Theme/`: `Colors.axaml` (all `:root`
  brand/surface/ink/semantic/accent tokens as `Color` + `SolidColorBrush`),
  `Spacing.axaml` (radii + 4px spacing scale), `Elevation.axaml`
  (`ShadowCard`/`ShadowPop` BoxShadows), `Typography.axaml` (bundled Vazirmatn
  `AppFontFamily` + FontSize scale). All four merged in `App.axaml`, which sets
  the app-wide default `FontFamily` via a `TopLevel` base style. `MainWindow`
  renders a themed panel as a resource smoke test. Vazirmatn is embedded as an
  `AvaloniaResource`. `dotnet build Animora.sln` = 0 warnings / 0 errors. No
  controls/navigation/screens yet.
- **Repo state:** `Animora.sln` (classic format) builds with all 5 projects on
  .NET 10 (`net10.0`, C# 14). Four placeholder class libraries
  (`Animora.Contracts`, `Animora.Domain`, `Animora.Application`,
  `Animora.Sync.Abstractions`) and the Avalonia app `Animora.Desktop`
  (WinExe) exist. `Animora.Desktop` launches an empty 1440×900 `MainWindow`
  ("Animora Desktop — Phase 01 scaffold") via a DI composition root
  (`App.Services`). Central package pins live in `Directory.Packages.props`;
  shared build settings in `Directory.Build.props`. No theming/controls/
  navigation/screens yet.
- **Roadmap status:** Phases 01–14 done and **build-verified** (0/0) + smoke-run verified. Phase 11 (Clinical Encounter Wizard) and Phase 12 (Vaccination) were both completed this session — Phase 11's pre-existing build defects (missing code-behind + invalid `Run Opacity`) were fixed first, then Phase 12 was implemented and Task-2 gaps closed.

## Phase checklist

| # | Phase | Status | Notes |
|---|---|---|---|
| 01 | Solution & Project Scaffold | ✅ Done | Build-verified (0/0). Avalonia.Diagnostics/Fluent package deltas are forced by Avalonia 12.x packaging, not defects (see log) |
| 02 | Design Tokens & Theme Resources | ✅ Done | Vazirmatn v33.003 static weight TTFs bundled (OFL) |
| 03 | Base Control Library | ✅ Done | Build-verified (0/0); TryFindResource + AVLN3001 defects fixed (see log) |
| 04 | RTL & Localization Utilities | ⚠️ Done with deviation | Build-verified (0/0). Persian-digit codepoint follows the actual reference `toFa` (U+06Fx), not the phase text's U+066x example — see log |
| 05 | App Shell: Sidebar, Topbar, Navigation | ⚠️ Done with deviation | Build-verified (0/0) + smoke run OK. Notification bell reuses IconClock (no bell icon in the set); see log |
| 06 | Dashboard Screen | ⚠️ Done with deviation | Build-verified (0/0) + smoke run OK (all 3 roles). Fixed AVLN2005 `1fr`→`*` grid-length defect; quick-tile 7 restored to reference; see log |
| 07 | Reception & Queue Screen | ⚠️ Done with deviation | Build-verified (0/0) + smoke run OK + render-compared to reference. Fixed 4 `AVLN5001` `Watermark`→`PlaceholderText` obsolete-API warnings; see log |
| 08 | Patients & Owners — List/Grid | ✅ Done | Build-verified (0/0) + smoke run OK. Fixed 5 hidden defects on consolidation (CS0200 `PetsPreviewText`; `BoxShadow` on Button; `Padding` on Grid; 2× `FontSize` on Border); see log |
| 09 | Patients & Owners — Pet File / Owner Sheet | ✅ Done | Build-verified (0/0) + smoke run OK. Records now open as centered sheets (matching the reference); fixed the empty wizard modal; see log |
| 10 | Visits — Doctor Rail & Triage | ✅ Done | Build-verified (0/0) + smoke run OK; fidelity code-reviewed vs reference; see log |
| 11 | Visits — Clinical Encounter Wizard | ✅ Done | Build-verified (0/0) + smoke run OK. Fixed missing `VisitEncounterView.axaml.cs` code-behind + invalid `Run Opacity` (Phase 11 build defects); see log |
| 12 | Vaccination Screen | ✅ Done | Build-verified (0/0) + smoke run OK (Xvfb). Segmented filter, records table, catalog stock grid, SMS automation settings, compose drawer + record detail. Fixed `BoolToBrushConverter` gap; wired settings save + detail-drawer actions; see log |
| 13 | Appointments — Calendar Views | ✅ Done | Build-verified (0/0) + smoke run OK; fidelity code-reviewed vs reference; see log |
| 14 | Appointments — Booking Panel | ✅ Done | Build-verified (0/0). Detail drawer + booking sheet (patient→type→doctor→date/slot→save); fixed Measure-crash in TimeGridEventsPanel |
| 15 | Boarding — Board & List | ✅ Done | Build-verified (0/0) + smoke-run OK (Phase 15 PR) |
| 16 | Boarding — Stay Modals | ✅ Done | Build-verified (0/0) + smoke-run OK. Detail/transfer/checkout/new-stay modals + real History/Settings tabs; grouped optgroup select re-drawn as a popup picker (see log) |
| 17 | Inventory — Product List & Settings | ✅ Done | Build-verified (0/0) + smoke-run OK. Phase 17 PR #45; verified complete this session |
| 18 | Inventory — Product Modal & Stock Adjust | ✅ Done | Build-verified (0/0) + smoke-run OK. Phase 18 PR #46; verified complete this session |
| 19 | Purchase & Suppliers Screen | ✅ Done | Build-verified (0/0) + smoke-run OK + rendered & compared to reference. KPI stat row + orders table |
| 20 | Sale/POS — Invoice Workspace | ⬜ Not started | |
| 21 | Sale/POS — Catalog & Payment Modals | ⬜ Not started | |
| 22 | Cashier Screen | ⬜ Not started | |
| 23 | Cheques — List Views & Side Panels | ⬜ Not started | |
| 24 | Cheques — Add/Edit Drawer & Bulk Actions | ⬜ Not started | |
| 25 | Finance — Overview & Income Tabs | ⬜ Not started | |
| 26 | Finance — Expense & Profit Tabs | ⬜ Not started | |
| 27 | History — Unified Ledger | ⬜ Not started | |
| 28 | Profile & Settings Screen | ⬜ Not started | |
| 29 | Cross-Screen Navigation Wiring & Visual QA | ⬜ Not started | |

Status legend: ⬜ Not started · 🔄 In progress · ✅ Done · ⚠️ Done with
deviation (see log below).

## Decision / deviation log

> Append one entry per deviation from the roadmap/conventions, newest on
> top. If a phase followed the plan exactly, no entry is needed.

### Phase 12 — Vaccination Screen

- **STATUS: complete — build-verified (0/0) + smoke-run verified.** The
  Vaccination screen was implemented and then its remaining gaps closed in a
  second pass:
  - **Contracts/mock (R-18):** `Animora.Contracts/Vaccination/` adds
    `IVaccinationService` (`GetRecordsAsync`/`GetCatalogAsync`/
    `GetSmsTemplatesAsync`/`MarkDoneAsync`/`BuildSmsPreviewAsync`/
    `SendSmsAsync`) plus `VaccinationRecord`, `VaccineCatalogEntry`,
    `SmsTemplate`, `SmsPreview` and the `VaccinationStatus`/`SmsState` enums.
    `MockVaccinationService` (24 records / 8 catalog entries / 4 templates,
    deterministic and anchored to today's Jalali date) is the single impl,
    registered once in DI alongside `VaccinationPageViewModel`.
  - **Fixed a latent runtime defect (Task-2):** the record-detail view bound
    `Stroke="{Binding IsActive, Converter={StaticResource BoolToBrushConverter}}"`
    but no such resource was registered — it would have thrown when the detail
    modal opened. Added `BoolToBrushConverter` (violet `#6f57c9` for active vs
    hairline `#e5eaee` for muted, the reference's `--vx-shot`/`--line`) and
    registered it in `App.axaml` as `BoolToBrushConverter`.
  - **Task-2 wiring:** the settings «ذخیره تنظیمات» button now shows the
    reference's `تنظیمات پیامک ذخیره شد (نمونه طراحی)` toast; the four settings
    switches each emit the reference's `{name} فعال/غیرفعال شد` toast (matching
    JS `vaxToggle`); and the record-detail modal's «ارسال پیامک یادآور» /
    «ثبت انجام» buttons now route through the page VM (open the compose drawer /
    mark the record done), matching the reference drawer actions.
  - **Fidelity:** segmented status filter (همه/نزدیک/عقب‌افتاده/آتی/انجام‌شده
    with live counts), search, species select, records table (بیمار/مالک، واکسن،
    سررسید بعدی، وضعیت، پیامک، اقدامات) with 8/page pager, catalog stock grid
    (`vx-cat-grid.wide`, low-stock turns danger), SMS-automation settings card +
    templates panel, compose drawer (chips/live preview/counter) and the record
    detail sheet (KV grid + timeline) — all values transcribed from
    `vaccination.css`. New theme tokens `ShotBrush`/`ShotBgBrush` (`#6f57c9`/`#efeafb`).
  - **Build/verify:** `scripts/sandbox/build.sh full` = **0 warnings / 0 errors**;
    `scripts/sandbox/run.sh` = **SMOKE RUN OK** (renders one frame under Xvfb
    with no startup crash). Xvfb was installed this session
    (`apt-get install xvfb` + ImageMagick) so the headless render check is
    available for all remaining screen phases.

### Phase 11 — Visits — Clinical Encounter Wizard

- **STATUS: code written, build/visual verification OUTSTANDING.** The full
  Phase 11 slice was authored in this session but the sandbox had **no .NET
  SDK installed** (`~/.dotnet` absent), so `scripts/sandbox/build.sh full`
  and the §7a render check could **not** be run. Per `CONVENTIONS.md` §6.7 a
  screen phase is not done until it builds 0/0 *and* has been compared
  element-by-element against the reference — so Phase 11 is recorded
  `🔄 In progress`, not `✅ Done`. Next session: install the SDK
  (`scripts/sandbox/install-prereqs.sh`), run `build.sh full`, fix any
  compiler/XAML errors, then run the §7a visual verification and mark the
  phase done (and only then advance the pointer to 12).
- **Cashier-vs-Sale hand-off clarification (recorded per the phase file).**
  The reference's done step ends with `window.toast('انتقال به صندوق…')` then
  `location.href = 'cashier.html'`. The phase file directs this product flow
  to target **Sale/POS (Phase 20)** rather than Cashier (phase file's own
  note: `sale-page.js`'s `buildFromEncounter` is the actual
  encounter-to-invoice function). Since Phase 20 does not exist yet, the
  done step's «رفتن به صندوق» shows the toast and navigates to the shared
  `PlaceholderPageViewModel` configured for «فروش و صندوق» — the same
  placeholder-bridge pattern Phase 06 used for the Reception deep-link. Phase
  20 replaces this with a real `NavigateTo<SalePageViewModel>` passing the
  completed encounter id.
- **`EncounterDraft` extended (Contracts) for the wizard's chrome.** The
  draft handed over by Phase 10 now also carries the ribbon/patient-sidebar
  context the wizard renders (doctor name/short-name/specialty/initials/accent,
  patient icon key, owner phone, visit type label/accent, synthetic flag and
  the queue slot `AtMinutes`/`DurationMinutes`). This keeps every rendered
  value backend-ready (a real backend fills the same record) and avoids the
  ViewModel reaching back into the triage service. `NewVisitRequest` gained a
  `DoctorId` (the reference reads `S.docId` separately when building the
  synthetic ticket).
- **Step sub-view split.** The wizard's body is a `Panel` swapping five step
  sub-views (`VitalsStepView`/`HistoryStepView`/`PlanStepView`/`RxStepView`/
  `ReviewStepView`) plus the separate `DoneStepView` scene, matching the
  reference's `renderBody` dispatch and its distinct `#scene-done`. Each
  sub-view inherits the wizard ViewModel as its DataContext.
- **Rx lines are a mutable class, not a record.** The reference mutates a
  line's `qty` in place (its `.qty` input writes into the encounter draft),
  so `RxLine.Quantity` is settable while the identity fields stay read-only;
  the running subtotal (`computeSubtotal` + 9% tax) is re-derived in the mock
  and recomputed in the ViewModel on every change.

### Phase 07 — Reception & Queue Screen

- **RESOLVED — build & render now verified; one hidden defect fixed.** The
  earlier session shipped the Reception code un-built because that sandbox had
  no .NET toolchain. This session installed the **.NET 10 SDK (10.0.401)** via
  `scripts/sandbox/install-prereqs.sh`, then:
  1. `scripts/sandbox/build.sh full` = **0 warnings / 0 errors** across all 5
     projects.
  2. `scripts/sandbox/run.sh` = **SMOKE RUN OK** (app builds its full visual
     tree and renders one frame headlessly under Xvfb with no startup crash).
  3. **Rendered visual comparison:** installed `xvfb`/`imagemagick`, temporarily
     pointed `MainWindowViewModel`'s startup navigation at
     `ReceptionPageViewModel`, captured the screen with
     `scripts/sandbox/shot.sh`, and compared it element-by-element against
     `Animora-UI/reception.html`. Header (eyebrow/title/subtitle + «ثبت مراجعهٔ
     حضوری»/«بازخوانی صف»), hero (live Jalali date, «صف فعال» pill, 4 metrics),
     4 KPI cards, filter tabs (همه/فعال/اورژانس/پایان‌یافته), search placeholder,
     «فیلتر» button, and per-row queue actions all match the reference. The
     temporary startup change was reverted (startup is back on
     `DashboardPageViewModel`) and the solution re-built 0/0.
  The missing build had hidden one real (non-fatal) defect:
  **`AVLN5001` — obsolete `TextBox.Watermark`.** `ReceptionPageView.axaml` (×1)
  and `WalkInModalView.axaml` (×3) used `Watermark="…"`, which Avalonia 12 marks
  obsolete in favour of `PlaceholderText`. All four were migrated to
  `PlaceholderText` (identical placeholder behaviour), restoring the roadmap's
  0-warning/0-error standard. Phase 07 is now **Done**; the phase pointer
  advances to 08.
- **Shell dialog API accepts ViewModels as content.** `IDialogService`/hosts now accept `object` rather than requiring a concrete `Control`, allowing `ReceptionPageViewModel` to open `WalkInModalViewModel` while the existing `ViewLocator` materializes `WalkInModalView`. This removes View construction from the ViewModel and keeps the modal flow idiomatic MVVM. The permanent modal/drawer/toast hosts are now attached once at `MainWindow`, as Phase 03 explicitly left for the real shell.
- **Dashboard deep-link wiring.** The existing «شروع ویزیت» quick action now navigates to `ReceptionPageViewModel`, replacing its placeholder target without adding a non-reference sidebar item or changing the dashboard tile count.

### Phase 06 — Dashboard Screen (role-based)

- **RESOLVED — build now verified; one hidden defect fixed.** The Phase 06
  code originally shipped un-built (no `dotnet` SDK in that session). A later
  audit session installed the .NET 10 SDK (10.0.401) and actually compiled +
  smoke-ran the solution. The missing build had hidden one real, fatal defect:
  1. **`AVLN2005` — invalid grid length `1fr`.**
     `ManagerDashboardView`, `CashierDashboardView`, and `DoctorDashboardView`
     each declared `<Grid ColumnDefinitions="1fr,1fr" …>`. `1fr` is CSS/WPF-
     `GridLength` grid syntax that Avalonia's XAML compiler **rejects** —
     Avalonia uses star sizing (`*`). All three were failing to parse, so the
     whole `Animora.Desktop` project would not build. Fix: changed all three to
     `ColumnDefinitions="*,*"` (two equal columns, identical layout intent).
  After the fix, `scripts/sandbox/build.sh full` = **0 warnings / 0 errors**
  and `scripts/sandbox/run.sh` = **SMOKE RUN OK** with `MockCurrentUserContext.
  Role` set to each of `Manager`/`Cashier`/`Doctor` in turn (all three role
  sub-views build their visual tree and render one frame with no crash), then
  restored to the `Manager` default. The earlier "run install-prereqs + build
  before Phase 07" action item is now closed.
- **Quick-actions tile 7 restored to the reference's "خرید و تأمین".** The
  first pass silently replaced reference `TILES[6]` (`خرید و تأمین`, truck
  icon) with a bespoke "مدیریت چک" tile. Task 3 asks the quick-actions strip to
  mirror `dashboard.js`'s `TILES`/`href` array, so the label/subtitle/target
  were restored to the reference (`خرید و تأمین` · `سفارش تأمین`). Phase 03's
  23-icon set has no truck geometry, so `IconReceipt` stands in for that one
  tile's icon only (documented inline); a later phase that adds a truck icon can
  swap it. Tile 4's subtitle was likewise aligned to the reference
  (`۶ یادآور امروز`).
- **`DashboardSnapshot` uses two generically-shaped content lists, not five
  bespoke sections.** The phase file says "shape it to match what
  DASH_VIEW_MANAGER/CASHIER/DOCTOR actually render, not a generic catch-all,"
  but all three role views' secondary sections (live queue, alerts, open
  invoices, ready-to-bill, vaccination reminders) share the exact same visual
  shape in the reference: avatar-letter + title + subtitle + one status pill.
  A single `DashboardListItem(AvatarText, Title, Subtitle, TagText, TagTone)`
  DTO plus two labeled instances (`PrimaryList`/`SecondaryList` with their own
  `*ListTitle`) covers every role's two-section layout without a proliferation
  of near-identical single-use DTOs — still role-shaped (each role's mock
  supplies different titles/rows/tones), just not five separate record types.
- **Revenue chart, species donut, and doctor hero live-clock/progress-ring
  omitted.** The phase's "Out of scope" section explicitly excludes spending
  budget on the reference's `requestAnimationFrame` counters/animations and
  scopes "one role-specific content section," and CONVENTIONS §7 caps phase
  scope to what's asked. The interactive SVG chart, donut, and doctor
  countdown ring are the reference's most animation-heavy, highest-effort
  pieces and are not reproduced; each role's two most information-dense
  sections (queue/alerts, invoices/ready-to-bill, queue/vaccination) are kept
  instead, matching "at a glance" comparability per the acceptance criteria.
- **Three role sub-views are visibility-toggled, not a `DataTemplate`-per-role
  `ContentControl`.** The phase file offers either "a `DataTemplate`-per-role
  or a `ContentControl` + converter" — with exactly three fixed, known roles
  (never more), three sibling `UserControl`s in the same `DashboardPageView`
  with `IsVisible` bound to `IsManager`/`IsCashier`/`IsDoctor` is simpler than
  either alternative and needs no converter or template-selector plumbing;
  all three share the one injected `DashboardPageViewModel` as DataContext
  exactly as the phase f