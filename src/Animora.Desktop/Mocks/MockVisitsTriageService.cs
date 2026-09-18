using Animora.Contracts.Visits;

namespace Animora.Desktop.Mocks;

/// <summary>
/// Deterministic in-memory implementation of <see cref="IVisitsTriageService"/>.
/// The doctor roster, their booked-appointment tickets and the pick-list of
/// patients are re-derived from what the reference gate screen visually needs
/// (Animora-UI/assets/clinic-data.js DOCTORS + QUEUE) so the rebuilt rail,
/// booked list and patient picker render the same boxes, counts and Persian
/// text as the reference. This is the single swap point a future backend
/// replaces (R-18) — no ViewModel or View change is required to do so.
/// </summary>
public sealed class MockVisitsTriageService : IVisitsTriageService
{
    private readonly VisitEncounterStore _store;

    public MockVisitsTriageService(VisitEncounterStore store) => _store = store;

    private readonly IReadOnlyList<VisitTypeInfo> _types =
    [
        new("visit", "ویزیت عمومی", "IconBriefcase", "#0d9488", 30),
        new("vaccine", "واکسیناسیون", "IconSyringe", "#1570cd", 20),
        new("dental", "دندان‌پزشکی", "IconTools", "#8f74d1", 60),
        new("surgery", "جراحی", "IconTools", "#d92d20", 90),
        new("groom", "آرایش و پیرایش", "IconScissors", "#e08a5c", 60),
        new("followup", "پیگیری", "IconClock", "#b54708", 20),
    ];

    private readonly IReadOnlyList<DoctorRailInfo> _doctors =
    [
        new("d1", "دکتر آریا نیک", "داخلی و جراحی", "آ", "#0d9488", AppointmentCount: 2, WalkInCount: 1),
        new("d2", "دکتر مینا همتی", "واکسن و پیشگیری", "م", "#1570cd", AppointmentCount: 1, WalkInCount: 1),
        new("d3", "دکتر سهیل کاویانی", "دندان‌پزشکی", "س", "#8f74d1", AppointmentCount: 0, WalkInCount: 1),
        new("d4", "نگار راد", "آرایش و نگهداری", "ن", "#e08a5c", AppointmentCount: 0, WalkInCount: 0),
    ];

    private readonly Dictionary<string, IReadOnlyList<BookedAppointmentRef>> _booked = new()
    {
        ["d1"] =
        [
            new("qt-1", 480, 30, "پشمک", "گربه پرشین", "cat", "نگار محمدی", "۰۹۱۲۳۴۵۶۷۸۹", "visit", false, BookedAppointmentState.Waiting),
            new("qt-5", 570, 30, "میلو", "سگ پامرانین", "dog", "رضا کریمی", "۰۹۳۰۴۴۵۵۶۶۷", "visit", false, BookedAppointmentState.Called),
        ],
        ["d2"] =
        [
            new("qt-2", 510, 20, "لئو", "سگ ژرمن", "dog", "امیر رستگار", "۰۹۳۵۱۱۲۲۳۳۴", "vaccine", false, BookedAppointmentState.Waiting),
        ],
        ["d3"] = [],
        ["d4"] = [],
    };

    private readonly IReadOnlyList<VisitPatientRef> _patients =
    [
        new("pt-1", "پشمک", "گربه پرشین", "نگار محمدی", "۰۹۱۲۳۴۵۶۷۸۹", "cat"),
        new("pt-2", "لئو", "سگ ژرمن", "امیر رستگار", "۰۹۳۵۱۱۲۲۳۳۴", "dog"),
        new("pt-3", "رکس", "سگ هاسکی", "محسن دهقان", "۰۹۳۶۲۲۳۳۴۴۵", "dog"),
        new("pt-4", "آبی", "کاسکو", "سارا یوسفی", "۰۹۱۹۸۷۶۵۴۳۲", "bird"),
        new("pt-5", "میلو", "سگ پامرانین", "رضا کریمی", "۰۹۳۰۴۴۵۵۶۶۷", "dog"),
        new("pt-6", "نانا", "گربه اسکاتیش", "الهام صادقی", "۰۹۱۲۷۷۸۸۹۹۰", "cat"),
        new("pt-7", "کوکو", "خرگوش لوپ", "مریم آذری", "۰۹۱۰۵۵۶۶۷۷۸", "rabbit"),
    ];

    public Task<IReadOnlyList<DoctorRailInfo>> GetDoctorsAsync() =>
        Task.FromResult(_doctors);

    public Task<IReadOnlyList<BookedAppointmentRef>> GetBookedForDoctorAsync(string doctorId)
    {
        ArgumentNullException.ThrowIfNull(doctorId);
        return Task.FromResult(
            _booked.TryGetValue(doctorId, out var rows) ? rows : (IReadOnlyList<BookedAppointmentRef>)[]);
    }

    public IReadOnlyList<VisitTypeInfo> GetVisitTypes() => _types;

    public Task<IReadOnlyList<VisitPatientRef>> GetPatientsAsync() =>
        Task.FromResult(_patients);

    public Task<EncounterDraft> StartFromTicketAsync(string ticketId)
    {
        ArgumentNullException.ThrowIfNull(ticketId);

        BookedAppointmentRef? match = null;
        var doctorId = string.Empty;
        foreach (var (id, rows) in _booked)
        {
            match = rows.FirstOrDefault(row => row.TicketId == ticketId);
            if (match is not null)
            {
                doctorId = id;
                break;
            }
        }

        var doctor = _doctors.FirstOrDefault(d => d.Id == doctorId);
        var bookedType = ResolveType(match?.TypeId);
        var draft = new EncounterDraft(
            Id: NewEncounterId(),
            TicketId: ticketId,
            DoctorId: doctorId,
            DoctorName: doctor?.Name ?? string.Empty,
            DoctorShortName: ShortName(doctor),
            DoctorSpecialty: doctor?.Specialty ?? string.Empty,
            DoctorInitials: doctor?.Initials ?? string.Empty,
            DoctorAccent: doctor?.Accent ?? "#0d9488",
            PatientName: match?.PatientName ?? string.Empty,
            PatientKind: match?.PatientKind ?? string.Empty,
            PatientIconKey: match?.PatientIconKey ?? "paw",
            OwnerName: match?.OwnerName ?? string.Empty,
            OwnerPhone: match?.OwnerPhone ?? string.Empty,
            TypeId: bookedType.Id,
            TypeLabel: bookedType.Label,
            TypeAccent: bookedType.Accent,
            IsUrgent: match?.IsUrgent ?? false,
            IsSynthetic: false,
            AtMinutes: match?.AtMinutes ?? 0,
            DurationMinutes: match?.DurationMinutes ?? 0,
            Note: string.Empty,
            SourceTicketId: ticketId);

        _store.Create(draft);
        return Task.FromResult(draft);
    }

    public Task<EncounterDraft> StartNewAsync(NewVisitRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var doctor = _doctors.FirstOrDefault(d => d.Id == request.DoctorId);
        var type = ResolveType(request.TypeId);
        var now = DateTime.Now;
        var draft = new EncounterDraft(
            Id: NewEncounterId(),
            TicketId: string.Empty,
            DoctorId: doctor?.Id ?? string.Empty,
            DoctorName: doctor?.Name ?? string.Empty,
            DoctorShortName: ShortName(doctor),
            DoctorSpecialty: doctor?.Specialty ?? string.Empty,
            DoctorInitials: doctor?.Initials ?? string.Empty,
            DoctorAccent: doctor?.Accent ?? "#0d9488",
            PatientName: request.PatientName,
            PatientKind: request.PatientKind,
            PatientIconKey: request.PatientIconKey,
            OwnerName: request.OwnerName,
            OwnerPhone: request.OwnerPhone,
            TypeId: type.Id,
            TypeLabel: type.Label,
            TypeAccent: type.Accent,
            IsUrgent: request.IsUrgent,
            IsSynthetic: true,
            AtMinutes: (now.Hour * 60) + now.Minute,
            DurationMinutes: type.DefaultDurationMinutes,
            Note: request.Note,
            SourceTicketId: string.Empty);

        _store.Create(draft);
        return Task.FromResult(draft);
    }

    private VisitTypeInfo ResolveType(string? typeId) =>
        _types.FirstOrDefault(t => t.Id == typeId) ?? _types[0];

    /// <summary>
    /// The doctor's display short-name (without the «دکتر» honorific) used by
    /// the ribbon badge and the done step, mirroring the reference's
    /// <c>DOCTORS[].short</c>.
    /// </summary>
    private static string ShortName(DoctorRailInfo? doctor)
    {
        if (doctor is null)
        {
            return string.Empty;
        }

        const string prefix = "دکتر ";
        return doctor.Name.StartsWith(prefix, StringComparison.Ordinal)
            ? doctor.Name[prefix.Length..]
            : doctor.Name;
    }

    private static string NewEncounterId() => $"enc-{Guid.NewGuid():N}"[..12];
}
