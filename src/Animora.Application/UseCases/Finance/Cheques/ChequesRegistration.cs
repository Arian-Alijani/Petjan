using Animora.Application.Abstractions;
using Animora.Application.Dispatch;
using Animora.Application.Ports;

namespace Animora.Application.UseCases.Finance.Cheques;

public static class ChequesRegistration
{
    public static ApplicationRegistrationBuilder AddChequeUseCases(this ApplicationRegistrationBuilder builder) => builder
        .AddQuery<ChequeListQuery, PagedResult<ChequeRow>, ChequeListQueryHandler>()
        .AddQuery<ChequeCashflowQuery, IReadOnlyList<ChequeCashflowWeek>, ChequeCashflowQueryHandler>()
        .AddQuery<ChequeBankBreakdownQuery, IReadOnlyList<ChequeBankRow>, ChequeBankBreakdownQueryHandler>()
        .AddQuery<ChequeAlertsQuery, IReadOnlyList<ChequeAlertRow>, ChequeAlertsQueryHandler>();
}
