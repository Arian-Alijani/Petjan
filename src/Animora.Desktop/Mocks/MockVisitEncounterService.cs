using Animora.Contracts.Visits;

namespace Animora.Desktop.Mocks;

/// <summary>
/// Deterministic in-memory implementation of <see cref="IVisitEncounterService"/>
/// over the shared <see cref="VisitEncounterStore"/>. The billable catalog is
/// re-derived from what the reference's rx step visually offers
/// (Animora-UI/assets/clinic-data.js SERVICES + ITEMS) and the subtotal is
/// re-computed with the same <c>price × qty</c> rule as the reference's
/// <c>computeSubtotal</c> — re-derived logic, not copied. This is the single
/// swap point a future backend replaces (R-18).
/// </summary>
public sealed class MockVisitEncounterService : IVisitEncounterService
{
    private const double TaxRate = 0.09;

    private readonly VisitEncounterStore _store;

    private readonly IReadOnlyList<RxCatalogItem> _catalog =
    [
        new(RxCatalogKind.Service, "sv-1", "ویزیت عمومی", 250000),
        new(RxCatalogKind.Service, "sv-2", "واکسن چهارگانه برونشیت", 380000),
        new(RxCatalogKind.Service, "sv-3", "جراحی عقیم‌سازی", 2800000),
        new(RxCatalogKind.Service, "sv-4", "دندان‌پزشکی و جرم‌گیری", 1200000),
        new(RxCatalogKind.Service, "sv-5", "آرایش و پیرایش", 650000),
        new(RxCatalogKind.Service, "sv-6", "آزمایش خون کامل", 800000),
        new(RxCatalogKind.Service, "sv-7", "سونوگرافی شکمی", 1500000),
        new(RxCatalogKind.Service, "sv-8", "رادیولوژی", 900000),
        new(RxCatalogKind.Service, "sv-9", "ویزیت تخصصی پوست", 350000),
        new(RxCatalogKind.Service, "sv-10", "تزریق و سرم‌تراپی", 450000),
        new(RxCatalogKind.Service, "sv-11", "بستری شبانه‌روزی", 1000000),
        new(RxCatalogKind.Service, "sv-12", "معاینه پیگیری", 180000),
        new(RxCatalogKind.Item, "it-1", "قرص ضد انگل", 160000),
        new(RxCatalogKind.Item, "it-2", "غذای کنسرو مرغ", 85000),
        new(RxCatalogKind.Item, "it-3", "غذای خشک بالغ سگ (۵ کیلو)", 850000),
        new(RxCatalogKind.Item, "it-4", "شامپوی مخصوص حیوانات", 220000),
        new(RxCatalogKind.Item, "it-5", "مکمل تقویت مفصل", 450000),
        new(RxCatalogKind.Item, "it-6", "آنتی‌بیوتیک خوراکی", 320000),
        new(RxCatalogKind.Item, "it-7", "برس و شانه پاکسازی", 125000),
        new(RxCatalogKind.Item, "it-8", "خاک مخصوص گربه", 280000),
        new(RxCatalogKind.Item, "it-9", "ویتامین مایع", 150000),
        new(RxCatalogKind.Item, "it-10", "قلاده ضدکک", 390000),
    ];

    public MockVisitEncounterService(VisitEncounterStore store) => _store = store;

    public Task<EncounterDraft> GetAsync(string encounterId) =>
        Task.FromResult(_store.Get(encounterId).Draft);

    public Task SaveVitalsAsync(string encounterId, Vitals vitals)
    {
        ArgumentNullException.ThrowIfNull(vitals);
        _store.Get(encounterId).Vitals = vitals;
        return Task.CompletedTask;
    }

    public Task SaveHistoryNoteAsync(string encounterId, string note)
    {
        _store.Get(encounterId).History = note ?? string.Empty;
        return Task.CompletedTask;
    }

    public Task SaveExamNoteAsync(string encounterId, string note)
    {
        _store.Get(encounterId).Exam = note ?? string.Empty;
        return Task.CompletedTask;
    }

    public Task SaveDiagnosisAsync(string encounterId, string diagnosis)
    {
        _store.Get(encounterId).Diagnosis = diagnosis ?? string.Empty;
        return Task.CompletedTask;
    }

    public Task SavePlanAsync(string encounterId, string plan)
    {
        _store.Get(encounterId).Plan = plan ?? string.Empty;
        return Task.CompletedTask;
    }

    public Task AddRxLineAsync(string encounterId, RxLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        _store.Get(encounterId).AddLine(line);
        return Task.CompletedTask;
    }

    public Task RemoveRxLineAsync(string encounterId, string rxLineId)
    {
        ArgumentNullException.ThrowIfNull(rxLineId);
        _store.Get(encounterId).RemoveLine(rxLineId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RxCatalogItem>> GetCatalogAsync() =>
        Task.FromResult(_catalog);

    public Task<EncounterSummary> CompleteAsync(string encounterId)
    {
        var state = _store.Get(encounterId);
        var subtotal = ComputeSubtotal(state);
        var tax = (long)Math.Round(subtotal * TaxRate, MidpointRounding.AwayFromZero);
        var total = subtotal + tax;

        state.IsCompleted = true;
        state.CompletedAt = DateTime.Now;

        var summary = new EncounterSummary(
            EncounterId: state.Draft.Id,
            PatientName: state.Draft.PatientName,
            PatientKind: state.Draft.PatientKind,
            DoctorId: state.Draft.DoctorId,
            DoctorShortName: state.Draft.DoctorShortName,
            Subtotal: subtotal,
            Tax: tax,
            Total: total,
            CompletedAt: state.CompletedAt.Value);

        return Task.FromResult(summary);
    }

    /// <summary>
    /// Sums every line's <c>price × qty</c> — the re-derived equivalent of the
    /// reference's <c>computeSubtotal</c> (services first, then items).
    /// </summary>
    private static long ComputeSubtotal(VisitEncounterStore.EncounterState state)
    {
        long total = 0;
        foreach (var line in state.Lines)
        {
            total += line.LineTotal;
        }

        return total;
    }
}
