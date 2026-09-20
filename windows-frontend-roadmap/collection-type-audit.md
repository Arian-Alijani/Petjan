# Collection-type audit — `ObservableCollection` used for dynamic (rebuilt) data

Scan performed for PROMPT 7 (see `CONVENTIONS.md §3a`). Goal: list bound
collections that are still `ObservableCollection<T>` but are **rebuilt /
filtered / sorted after their initial population** — i.e. per §3a they should be
`RangeObservableCollection<T>` updated with `ReplaceAll` / `AddRange`.

Method: every `public ObservableCollection<T>` property that is `.Clear()`'d was
found, and the enclosing method of each `.Clear()` was resolved. A collection is
classified **DYNAMIC** when its clear+rebuild sits in a method that re-runs on
user interaction (`Render*`, `Apply*Filter`, `Rebuild*`, `Search*`, `Show*`,
paging), and **ONE-TIME** when the clear only runs inside `LoadAsync` / a
one-shot `Build*`.

> Note: fixing these is **out of scope for this task** (documentation + report
> only). This file is the actionable backlog for a follow-up pass. Collections
> already using `RangeObservableCollection<T>` are not listed.

---

## DYNAMIC — recompute on interaction → should migrate to `RangeObservableCollection<T>`

| File | Property | Rebuilt in |
| --- | --- | --- |
| `Pages/Appointments/AppointmentsCalendarViewModel.cs` | `Columns` | `RenderTimeGrid()` |
| `Pages/Appointments/AppointmentsCalendarViewModel.cs` | `MonthCells` | `RenderMonth()` |
| `Pages/Appointments/AppointmentsCalendarViewModel.cs` | `HistoryGroups` | `RenderHistory()` |
| `Pages/Appointments/AppointmentsCalendarViewModel.cs` | `HistoryCalendarDays` | `RenderHistory()` (calendar rebuild) |
| `Pages/Appointments/AppointmentsCalendarViewModel.cs` | `QueueColumns` / `QueueDays` / `QueueStats` | queue re-render |
| `Pages/Appointments/BookingPanelViewModel.cs` | `SlotGroups` | `RenderSlots()` |
| `Pages/Appointments/BookingPanelViewModel.cs` | `CalendarDays` | `RenderCalendarAsync()` |
| `Pages/Appointments/BookingPanelViewModel.cs` | `PatientList` | `RenderPatient()` |
| `Pages/Boarding/NewStayModalViewModel.cs` | `KnownPets` | `ApplyPetFilter()` |
| `Pages/Cheques/ChequesPageViewModel.cs` | `SelectedCheques` | `RenderBulk()` |
| `Pages/Cheques/ChequesPageViewModel.cs` | `Alerts` | `RefreshAlertsAsync()` |
| `Pages/Cheques/ChequesPageViewModel.cs` | `StatusChips` | `RenderChips()` |
| `Pages/Cheques/ChequesPageViewModel.cs` | `Kpis` | `RenderKpis()` |
| `Pages/Finance/FinancePageViewModel.cs` | `CategoryRows` | `BuildCategoryRows()` (re-run on range/tab change) |
| `Pages/Finance/FinancePageViewModel.cs` | `ProductRows` | `BuildProducts()` |
| `Pages/Finance/FinancePageViewModel.cs` | `ServiceSplits` | `BuildServiceSplit()` |
| `Pages/Finance/FinancePageViewModel.cs` | `ChannelBars` | `BuildIncomeExtras()` |
| `Pages/Finance/FinancePageViewModel.cs` | `HeatRows` | `BuildIncomeExtras()` |
| `Pages/Finance/FinancePageViewModel.cs` | `Kpis` | `BuildKpis()` |
| `Pages/Finance/FinancePageViewModel.cs` | `MiniStats` | `BuildExpenseMiniStrip()` / `BuildProfitMiniStrip()` / `BuildIncomeExtras()` |
| `Pages/Finance/FinancePageViewModel.cs` | `Species` | `BuildSpecies()` |
| `Pages/History/HistoryPageViewModel.cs` | `DayGroups` | `RenderFeed()` |
| `Pages/History/HistoryPageViewModel.cs` | `Kpis` | `RenderKpi()` |
| `Pages/Inventory/InventoryPageViewModel.cs` | `CategoryRows` | `BuildSettings()` |
| `Pages/Inventory/ProductModalViewModel.cs` | `HistoryItems` | `ApplyHistoryFilter()` |
| `Pages/Inventory/ProductModalViewModel.cs` | `AttributeOptions` | `RebuildAttributeOptions()` |
| `Pages/Patients/AddPatientWizardViewModel.cs` | `OwnerRows` | `RenderOwnerList()` |
| `Pages/Patients/OwnerSheetViewModel.cs` | `Pets` | `Apply()` |
| `Pages/Patients/PetFileViewModel.cs` | `Vitals` | `BuildVitals()` (re-run on pet switch) |
| `Pages/Patients/PetFileViewModel.cs` | `Alerts` | `BuildAlerts()` |
| `Pages/Patients/PetFileViewModel.cs` | `InfoItems` | `BuildInfoItems()` |
| `Pages/Patients/PetFileViewModel.cs` | `WeightPoints` / `WeightMiniPoints` | `BuildWeight()` |
| `Pages/Reception/ReceptionPageViewModel.cs` | `VisibleQueue` | `ApplyFilter()` |
| `Pages/Sales/CatalogModalViewModel.cs` | `Rows` | `Render()` |
| `Pages/Sales/CatalogModalViewModel.cs` | `Recent` | `Render()` |
| `Pages/Sales/OwnerPickerModalViewModel.cs` | `Rows` | `SearchAsync()` |
| `Pages/Sales/SalePageViewModel.cs` | `InvoiceRows` | `RenderSidebar()` |
| `Pages/Sales/SalePageViewModel.cs` | `ReadyList` | `RenderSidebar()` |
| `Pages/Sales/SalePageViewModel.cs` | `Lines` | `ShowInvoice()` / `ShowEmptyWork()` |
| `Pages/Sales/SalePageViewModel.cs` | `Payments` | `ShowInvoice()` / `ShowEmptyWork()` |
| `Pages/Vaccination/VaccinationPageViewModel.cs` | `Segments` | `RefreshSegments()` |
| `Pages/Vaccination/VaccinationPageViewModel.cs` | `Pages` | `RefreshRows()` (pager rebuild) |
| `Pages/Visits/VisitsTriagePageViewModel.cs` | `BookedRows` | `BuildBookedRows()` |
| `Pages/Visits/VisitsTriagePageViewModel.cs` | `PatientMatches` | `PickPatient()` / `SaveNewPatient()` / `BuildPatientMatches()` |
| `Pages/Profile/ProfilePageViewModel.cs` | `MatrixRows` | `ApplySettings()` |

## Borderline — cleared inside a `Build*` that today runs once, but is filter/tab dependent

These are populated by a `Build*` currently invoked once, yet their inputs
(active tab, active filter) can change, at which point they become dynamic.
Convert opportunistically when the screen gains live re-filtering.

| File | Property | Built in |
| --- | --- | --- |
| `Pages/Cheques/ChequesPageViewModel.cs` | `RangeOptions` | `BuildRanges()` |
| `Pages/Cheques/ChequesPageViewModel.cs` | `Tabs` | `BuildTabs()` |
| `Pages/Cheques/ChequesPageViewModel.cs` | `ViewModes` | `BuildViewModes()` |
| `Pages/History/HistoryPageViewModel.cs` | `PeriodChips` | `BuildPeriodChips()` |
| `Pages/Inventory/InventoryPageViewModel.cs` | `CategoryChips` | `BuildFilterChips()` |
| `Pages/Cashier/CashierPageViewModel.cs` | `Methods` / `Presets` | `BuildMethods()` / `BuildPresets()` |
| `Pages/Visits/VisitsTriagePageViewModel.cs` | `Chips` | `BuildChips()` |

## ONE-TIME — filled once, never recomputed → keep as `ObservableCollection<T>` (compliant)

| File | Property | Filled in |
| --- | --- | --- |
| `Pages/Cheques/ChequesPageViewModel.cs` | `BankFilterOptions` | `LoadAsync()` |
| `Pages/Visits/VisitEncounterViewModel.cs` | `ServiceCatalog` / `ItemCatalog` | `LoadAsync()` |
| `Pages/Visits/VisitsTriagePageViewModel.cs` | `Doctors` | `LoadAsync()` |
| `Pages/Boarding/StayDetailModalViewModel.cs` | `DayCells` / `Items` | `LoadAsync()` |
| `Controls/JalaliDatePicker.axaml.cs` | `Days` | month render (control-internal, small fixed 42-cell grid) |

Collections that are only ever appended to with single-item `Add` (e.g.
`Shell/PageViewModelBase.Breadcrumbs`, `Services/ToastService.Toasts`) are
correctly `ObservableCollection<T>` under §3a and are not listed.
