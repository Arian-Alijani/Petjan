using Animora.Application;
using Animora.Application.Ports;
using Animora.Application.UseCases.Inventory;
using Animora.Application.UseCases.Purchasing;
using Animora.Application.UseCases.Finance;
using Animora.Application.UseCases.Finance.Cheques;
using Animora.Domain.Inventory;
using Animora.Domain.Purchasing;
using Animora.Domain.Finance;
using Animora.Domain.Sales;
using Animora.Domain.ValueObjects;
using Animora.Server.Commands;
using Animora.Server.Handlers;
using Animora.Server.Tests.HandlersS07;
using Microsoft.Extensions.DependencyInjection;
namespace Animora.Server.Tests.HandlersS10;
internal sealed class MoneyStockHandlerFixture : IDisposable
{
    public ClinicalHandlerFixture Clinical { get; }
    public CommandPayloadCatalog Catalog { get; }
    public Product Product { get; }
    public PurchaseOrder Order { get; }
    public FixtureProductCategoryRepository Categories { get; } = new();
    public FixtureChequeRepository Cheques { get; } = new();
    public FixtureExpenseRepository Expenses { get; } = new();
    public FixtureRecurringExpenseRepository Recurring { get; } = new();
    public StockMovementRepositoryFake Movements { get; } = new();
    public InventoryQueriesFake Inventory { get; } = new() { OnHandScaled = 10000 };
    public MoneyStockHandlerFixture()
    {
        var clock = new Animora.Server.Tests.Commands.FixedClock();
        Categories.Add(ProductCategory.Create("fixture", "CAT", "Category").Value!);
        Product = Product.Create("fixture", "SKU", "Product", "category", ProductKind.Medicine, "PCS", Money.Irt(10), Money.Irt(20),
            [new ProductVariant("variant", "SKU-1", ProductVariant.DefaultAttributeName, "SKU", Money.Irt(10), Money.Irt(20), Quantity.Zero("PCS"), null, true)]).Value!;
        Order = PurchaseOrder.Create("fixture", "PO-1", "supplier", clock.UtcNow, "user").Value!;
        Order.AddLine(new PurchaseOrderLine("line", Product.Id, "variant", new Quantity(10, "PCS"), Quantity.Zero("PCS"), Money.Irt(10), 0, 0, null, null));
        Order.Submit(clock);
        Cheques.Add(Cheque.Register("fixture", "CHQ", ChequeDirection.Inbound, Money.Irt(100), clock.UtcNow.AddDays(1), clock.UtcNow, ownerId: "owner").Value!);
        var expense = Expense.Create("fixture", "EXP-1", ExpenseCategories.Rent, "Rent", Money.Irt(100), clock.UtcNow, "user", PaymentMethod.Transfer).Value!;
        expense.Post(clock); Expenses.Add(expense);
        Recurring.Add(RecurringExpense.Create("fixture", ExpenseCategories.Rent, "Rent", Money.Irt(100), 10, clock.UtcNow).Value!);
        Clinical = new ClinicalHandlerFixture(services =>
        {
            services.AddSingleton<IProductRepository>(new ProductRepositoryFake(Product));
            services.AddSingleton<IProductCategoryRepository>(Categories); services.AddSingleton<IInventoryQueries>(Inventory);
            services.AddSingleton<IStockMovementRepository>(Movements);
            services.AddSingleton<IPurchasingRepository>(new PurchasingRepositoryFake(Order)); services.AddSingleton<IPurchasingQueries>(new PurchasingQueriesFake());
            services.AddSingleton<ISupplierRepository>(new FixtureSuppliers()); services.AddSingleton<IChequeRepository>(Cheques);
            services.AddSingleton<IExpenseRepository>(Expenses); services.AddSingleton<IExpenseNumberAllocator>(new FixtureExpenseNumbers());
            services.AddSingleton<IRecurringExpenseRepository>(Recurring);
            services.AddAnimoraApplication(ServerHandlerModuleS10.AddHandlers, ApplicationPipelineProfile.Server, registerQueries: false);
        });
        var b = new CommandPayloadCatalogBuilder(); ServerHandlerModuleS10.AddPayloads(b); Catalog = b.Build();
    }
    public object Command(Type type, bool reject)
    {
        var id = reject ? "missing" : Product.Id;
        var now = Clinical.Intake.Clock.UtcNow;
        return type.Name switch
        {
            nameof(CreateProductCategoryCommand) => new CreateProductCategoryCommand(reject ? "" : "New"),
            nameof(RenameProductCategoryCommand) => new RenameProductCategoryCommand(reject ? "missing" : Categories.Rows[0].Id, "Updated"),
            nameof(DeleteProductCategoryCommand) => new DeleteProductCategoryCommand(reject ? "missing" : Categories.Rows[0].Id),
            nameof(CreateProductCommand) => new CreateProductCommand("NEW", reject ? "" : "New", "category", "PCS", ProductKind.Medicine, 10, 20,
                [new ProductVariantInput(null, "NEW-1", ProductVariant.DefaultAttributeName, "NEW", 10, 20, 0)]),
            nameof(UpdateProductInfoCommand) => new UpdateProductInfoCommand(id, "Updated", 10, 20,
                [new ProductVariantInput("variant", "SKU-1", ProductVariant.DefaultAttributeName, "SKU", 10, 20, 0)]),
            nameof(AddProductVariantCommand) => new AddProductVariantCommand(id, "SECOND", "Size", "Large", 10, 20),
            nameof(DeleteProductCommand) => new DeleteProductCommand(id),
            nameof(AdjustStockCommand) => new AdjustStockCommand(id, "variant", 1000, AdjustmentReasons.Count),
            nameof(SetStockCommand) => new SetStockCommand(id, [new StockTargetLine("variant", 12000)], AdjustmentReasons.Count),
            nameof(CreatePurchaseOrderCommand) => new CreatePurchaseOrderCommand("supplier", [new PurchaseOrderLineInput(id, "variant", 1000, 10)]),
            nameof(ReceiveGoodsCommand) => new ReceiveGoodsCommand("supplier", [new GoodsReceiptLineInput(id, "variant", 1000, 10)], Order.Id),
            nameof(CreateSupplierCommand) => new CreateSupplierCommand(reject ? "" : "Supplier"),
            nameof(CancelPurchaseOrderCommand) => new CancelPurchaseOrderCommand(reject ? "missing" : Order.Id, "Cancelled"),
            nameof(AddExpenseCommand) => new AddExpenseCommand(ExpenseCategories.Rent, reject ? "" : "Rent", 100, now),
            nameof(ReverseExpenseCommand) => new ReverseExpenseCommand(reject ? "missing" : Expenses.Rows[0].Id, "Reverse"),
            nameof(AddRecurringExpenseCommand) => new AddRecurringExpenseCommand(ExpenseCategories.Rent, reject ? "" : "Rent", 100, 10),
            nameof(EditRecurringExpenseCommand) => new EditRecurringExpenseCommand(reject ? "missing" : Recurring.Rows[0].Id, ExpenseCategories.Rent, "Updated", 100, 10),
            nameof(ToggleRecurringExpenseCommand) => new ToggleRecurringExpenseCommand(reject ? "missing" : Recurring.Rows[0].Id, false),
            nameof(PayRecurringExpenseForPeriodCommand) => new PayRecurringExpenseForPeriodCommand(reject ? "missing" : Recurring.Rows[0].Id),
            nameof(RegisterChequeCommand) => new RegisterChequeCommand(ChequeDirection.Inbound, reject ? "" : "New", 100, now.AddDays(1), now, OwnerId: "owner"),
            nameof(UpdateChequeMetadataCommand) => new UpdateChequeMetadataCommand(reject ? "missing" : Cheques.Rows[0].Id, "Updated", now.AddDays(2)),
            nameof(DeleteChequeDraftCommand) => new DeleteChequeDraftCommand(reject ? "missing" : Cheques.Rows[0].Id),
            nameof(TransitionChequeCommand) => new TransitionChequeCommand(reject ? "missing" : Cheques.Rows[0].Id, ChequeTransition.MoveToPortfolio),
            _ => throw new InvalidOperationException(type.Name)
        };
    }
    public void Dispose() => Clinical.Dispose();
}
