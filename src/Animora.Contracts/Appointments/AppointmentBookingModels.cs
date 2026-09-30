namespace Animora.Contracts.Appointments;

/// <summary>
/// A patient/owner row for the booking panel's picker, shaped to exactly what
/// the reference's <c>petRow</c>/<c>selectedCard</c>/summary <c>sum-pet</c>
/// blocks render: animal name + species/breed, owner name + contact, and the
/// avatar tone + species glyph key. The reference keeps this on its shared
/// <c>S.PETS</c> array (Animora-UI/assets/appointments.js); the booking service
/// is the seam a future backend fills (R-18).
/// </summary>
public sealed record BookingPatient(
    string Id,
    string Name,
    string Kind,
    string OwnerName,
    string Phone,
    string AvatarTone,
    string SpeciesKey,
    string SpeciesIconKey);

/// <summary>
/// The quick patient-registration payload behind the panel's "ثبت سریع بیمار
/// جدید" form (the reference's <c>savePet()</c>). Mirrors the four fields the
/// reference collects: animal name, species/breed, owner name and phone.
/// </summary>
public sealed record NewPatientRequest(
    string Name,
    string Kind,
    string OwnerName,
    string Phone);

/// <summary>
/// One selectable/non-selectable time slot in the booking panel's slot grid,
/// mirroring one entry of the reference's <c>freeSlots()</c> output:
/// <c>{ m, free }</c> — minutes-from-midnight plus whether the range is open.
/// </summary>
public sealed record TimeSlot(int Minutes, bool IsFree);

/// <summary>
/// The full detail of an existing appointment shown by the detail drawer,
/// mirroring everything the reference's <c>detailHtml(a)</c> reads: the
/// appointment itself plus the human "created by" line
/// (<c>a.created</c> — «ثبت توسط پذیرش»).
/// </summary>
public sealed record AppointmentDetail(AppointmentEvent Event, string CreatedText);

/// <summary>
/// Payload to create a new appointment (the reference's new-booking branch in
/// <c>submitBook()</c>): date + start + duration + type + doctor + the chosen
/// patient's identity snapshot + note, plus the two notification switches.
/// </summary>
public sealed record NewAppointmentRequest(
    DateOnly Date,
    int StartMinutes,
    int DurationMinutes,
    string TypeId,
    string DoctorId,
    string PetName,
    string PetKind,
    string OwnerName,
    string OwnerPhone,
    string AvatarTone,
    string SpeciesIconKey,
    string Note,
    bool SendSms,
    bool SendReminder,
    string PatientId = "");

/// <summary>
/// Payload to edit an existing appointment (the reference's edit branch in
/// <c>submitBook()</c>): the same editable fields as
/// <see cref="NewAppointmentRequest"/>.
/// </summary>
public sealed record UpdateAppointmentRequest(
    DateOnly Date,
    int StartMinutes,
    int DurationMinutes,
    string TypeId,
    string DoctorId,
    string PetName,
    string PetKind,
    string OwnerName,
    string OwnerPhone,
    string AvatarTone,
    string SpeciesIconKey,
    string Note,
    bool SendSms,
    bool SendReminder);
