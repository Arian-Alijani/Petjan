using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Reception.ReadModels;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Application.UseCases.Reception;

/// <summary>
/// Today's reception queue — a <b>derived</b> read (C02). <paramref name="LocalDay"/> is the clinic's
/// local calendar day; the handler turns it into a UTC half-open range so the single-statement
/// projection can filter on an index without a SQL date function.
/// </summary>
[RequiresPermission(PermissionCodes.ReceptionManage)]
public sealed record ReceptionQueueQuery(DateOnly LocalDay)
    : IQuery<IReadOnlyList<QueueTicketReadModel>>;

public sealed class ReceptionQueueQueryHandler(IReceptionQueueQueries queries)
    : IQueryHandler<ReceptionQueueQuery, IReadOnlyList<QueueTicketReadModel>>
{
    public async Task<Result<IReadOnlyList<QueueTicketReadModel>>> HandleAsync(
        ReceptionQueueQuery query, QueryContext context, CancellationToken ct)
    {
        var startUtc = new DateTimeOffset(query.LocalDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var endUtc = startUtc.AddDays(1);
        return Result<IReadOnlyList<QueueTicketReadModel>>.Ok(
            await queries.GetQueueAsync(startUtc, endUtc, ct));
    }
}

/// <summary>Active doctors offered on the reception walk-in modal (C02).</summary>
[RequiresPermission(PermissionCodes.ReceptionManage)]
public sealed record ReceptionDoctorsQuery
    : IQuery<IReadOnlyList<ReceptionDoctorReadModel>>;

public sealed class ReceptionDoctorsQueryHandler(IReceptionQueueQueries queries)
    : IQueryHandler<ReceptionDoctorsQuery, IReadOnlyList<ReceptionDoctorReadModel>>
{
    public async Task<Result<IReadOnlyList<ReceptionDoctorReadModel>>> HandleAsync(
        ReceptionDoctorsQuery query, QueryContext context, CancellationToken ct) =>
        Result<IReadOnlyList<ReceptionDoctorReadModel>>.Ok(await queries.GetDoctorsAsync(ct));
}
