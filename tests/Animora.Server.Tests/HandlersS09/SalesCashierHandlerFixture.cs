using Animora.Application;
using Animora.Application.Dispatch;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.UseCases.Sales;
using Animora.Application.UseCases.Cashier;
using Animora.Domain.Sales;
using Animora.Domain.Cashier;
using Animora.Domain.Boarding;
using Animora.Domain.Inventory;
using Animora.Domain.ValueObjects;
using Animora.Server.Handlers;
using Animora.Server.Commands;
using Animora.Server.Tests.HandlersS07;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Server.Tests.HandlersS09;

internal sealed class SalesCashierHandlerFixture : IDisposable
{
    public ClinicalHandlerFixture Clinical { get; }
    public CommandPayloadCatalog Catalog { get; }
    public Invoice Invoice { get; }
    public FixtureInsurerRepository Insurers { get; } = new();
    public FixtureProductRepository Products { get; } = new();
    public FixtureBoardingServiceDefRepository BoardingServices { get; } = new();
    public CashSessionRepositoryFake Sessions { get; } = new();
    public SalesPaymentRepository Payments { get; } = new();
    public SalesPostingFake Posting { get; } = new() { AllowNegativeStock = false };
    public SalesInvoiceRepository Invoices { get; }
    public FixtureSalesDraft Draft { get; }
    public SalesCashierHandlerFixture()
    {
        var clock = new Animora.Server.Tests.Commands.FixedClock();
        Invoice = Invoice.Create("fixture", "draft-1", InvoiceKind.DirectSale, clock.UtcNow, "user", guestName: "Guest").Value!;
        Invoice.AddLine(new InvoiceLine("line", InvoiceLineKind.Custom, null, null, null, "Exam",
            new Quantity(1, "PCS"), Money.Irt(100), 0, 0, InvoiceLineSource.Manual, null));
        Invoices = new(Invoice);
        Insurers.Add(Insurer.Create("fixture", "INS", "Insurer", 10, 0));
        Products.Add(Product.Create("fixture", "SRV", "Service", "category", ProductKind.Service, "PCS", Money.Irt(0), Money.Irt(100),
            [new ProductVariant("variant", "SRV-1", ProductVariant.DefaultAttributeName, "SRV", Money.Irt(0), Money.Irt(100), Quantity.Zero("PCS"), null, true)], trackStock: false).Value!);
        BoardingServices.Add(BoardingServiceDef.Create("fixture", "BS", "Boarding service", Money.Irt(100), BoardingServiceBilling.PerStay));
        Draft = new(Insurers);
        Clinical = new ClinicalHandlerFixture(services =>
        {
            services.AddSingleton<IInvoiceRepository>(Invoices); services.AddSingleton<IPaymentRepository>(Payments);
            services.AddSingleton<IInsurerRepository>(Insurers); services.AddSingleton<IProductRepository>(Products);
            services.AddSingleton<IBoardingServiceRepository>(BoardingServices);
            services.AddSingleton<ISalesDraftData>(Draft); services.AddSingleton<IServiceRateQueries>(Draft);
            services.AddSingleton<ISalesPostingData>(Posting); services.AddSingleton<IInvoiceNumberAllocator>(new SalesNumberAllocator());
            services.AddSingleton<IStockMovementRepository>(new FixtureStockRepository());
            services.AddSingleton<ICashSessionRepository>(Sessions); services.AddSingleton<ICashSessionContext>(new CashSessionContextFake(Sessions));
            services.AddSingleton<ICashierQueries>(new CashierQueriesFake()); services.AddSingleton<ILocalSequenceAllocator>(new FixtureSequence());
            services.AddSingleton<IBoardingRepository>(new FixtureBoardingRepository());
            services.AddAnimoraApplication(ServerHandlerModuleS09.AddHandlers, ApplicationPipelineProfile.Server, registerQueries: false);
        });
        var b = new CommandPayloadCatalogBuilder(); ServerHandlerModuleS09.AddPayloads(b); Catalog = b.Build();
    }
    public object Command(Type type, bool reject)
    {
        var clock = Clinical.Intake.Clock;
        var id = reject ? "missing" : Invoice.Id;
        if (type == typeof(RegisterPaymentCommand) || type == typeof(VoidInvoiceCommand) || type == typeof(RefundInvoiceCommand) || type == typeof(ReturnInvoiceLineCommand))
            Invoice.Post(clock);
        if (type == typeof(RefundInvoiceCommand))
            Invoice.ApplyPayment(Payment.Create("fixture", Invoice.Id, PaymentMethod.Transfer, Money.Irt(100), clock.UtcNow, "user", operationId: "paid").Value!);
        if (type != typeof(OpenCashSessionCommand))
            Sessions.Add(CashSession.Open(Clinical.Intake.TenantId, "CS-1", Clinical.Intake.UserId, Money.Irt(100), clock.UtcNow).Value!);
        return type.Name switch
        {
            nameof(CreateBlankInvoiceCommand) => new CreateBlankInvoiceCommand(null, reject ? null : "Guest"),
            nameof(CreateInvoiceFromVisitCommand) => new CreateInvoiceFromVisitCommand(reject ? "missing" : Draft.VisitHandoffId),
            nameof(CreateInvoiceFromStayCommand) => new CreateInvoiceFromStayCommand(reject ? "missing" : Draft.StayHandoffId),
            nameof(CreateInvoiceFromHandoffCommand) => new CreateInvoiceFromHandoffCommand(reject ? "missing" : Draft.VisitHandoffId),
            nameof(AddInvoiceLineCommand) => new AddInvoiceLineCommand(id, "catalog", 1),
            nameof(AddCustomInvoiceLineCommand) => new AddCustomInvoiceLineCommand(id, "Custom", 100, 1),
            nameof(EditInvoiceLineCommand) => new EditInvoiceLineCommand(id, 0, "line", 2, 0),
            nameof(RemoveInvoiceLineCommand) => new RemoveInvoiceLineCommand(id, 0, "line"),
            nameof(SetInvoiceDiscountCommand) => new SetInvoiceDiscountCommand(id, DiscountKind.Percent, 10),
            nameof(SetInvoiceInsuranceCommand) => new SetInvoiceInsuranceCommand(id, Insurers.Rows[0].Id),
            nameof(AssignInvoiceOwnerCommand) => new AssignInvoiceOwnerCommand(id, null, null, "Guest"),
            nameof(DeleteDraftInvoiceCommand) => new DeleteDraftInvoiceCommand(id),
            nameof(PostInvoiceCommand) => new PostInvoiceCommand(id),
            nameof(RegisterPaymentCommand) => new RegisterPaymentCommand(id, PaymentMethod.Transfer, Money.Irt(50)),
            nameof(VoidInvoiceCommand) => new VoidInvoiceCommand(id, "Void"),
            nameof(RefundInvoiceCommand) => new RefundInvoiceCommand(id, Money.Irt(50), "Refund"),
            nameof(ReturnInvoiceLineCommand) => new ReturnInvoiceLineCommand(id, "line", "Return"),
            nameof(CreateInsurerCommand) => new CreateInsurerCommand(reject ? "" : "New", 10, 0),
            nameof(UpdateInsurerCommand) => new UpdateInsurerCommand(reject ? "missing" : Insurers.Rows[0].Id, "Updated", 10, 0),
            nameof(DeleteInsurerCommand) => new DeleteInsurerCommand(reject ? "missing" : Insurers.Rows[0].Id),
            nameof(CreateServiceRateCommand) => new CreateServiceRateCommand(reject ? "" : "New", 100),
            nameof(CreateBoardingServiceRateCommand) => new CreateBoardingServiceRateCommand(reject ? "" : "New", 100, BoardingServiceBilling.PerStay),
            nameof(UpdateServiceRateCommand) => new UpdateServiceRateCommand(reject ? "missing" : Products.Rows[0].Id, ServiceRateSources.Clinic, "Updated", 100, true),
            nameof(OpenCashSessionCommand) => new OpenCashSessionCommand(Money.Irt(reject ? -1 : 100)),
            nameof(CloseCashSessionCommand) => new CloseCashSessionCommand(Money.Irt(reject ? 90 : 100), null),
            nameof(RecordCashMovementCommand) => new RecordCashMovementCommand(CashMovementKind.FloatIn, Money.Irt(10), reject ? "" : "Float"),
            _ => throw new InvalidOperationException(type.Name)
        };
    }
    public void Dispose() => Clinical.Dispose();
}
internal sealed class FixtureStockRepository : IStockMovementRepository
{
    public List<StockMovement> Rows { get; } = [];
    public Task<StockMovement?> FindAsync(string id, CancellationToken ct = default) => Task.FromResult(Rows.FirstOrDefault(m => m.Id == id));
    public Task<StockMovement> RequireAsync(string id, CancellationToken ct = default) => Task.FromResult(Rows.Single(m => m.Id == id));
    public void Add(StockMovement entity) => Rows.Add(entity);
    public void AddRange(IEnumerable<StockMovement> entities) => Rows.AddRange(entities);
}
internal sealed class FixtureSequence : ILocalSequenceAllocator
{
    private long _next;
    public Task<Animora.Domain.Results.Result<long>> NextAsync(CancellationToken ct = default) => Task.FromResult(Animora.Domain.Results.Result<long>.Ok(++_next));
}
internal sealed class FixtureBoardingRepository : IBoardingRepository
{
    public Task<BoardingStay?> FindAsync(string id, CancellationToken ct = default) => Task.FromResult<BoardingStay?>(null);
    public Task<BoardingStay> RequireAsync(string id, CancellationToken ct = default) => throw new InvalidOperationException(id);
    public void Add(BoardingStay entity) => throw new NotSupportedException();
    public void AddRange(IEnumerable<BoardingStay> entities) => throw new NotSupportedException();
}
