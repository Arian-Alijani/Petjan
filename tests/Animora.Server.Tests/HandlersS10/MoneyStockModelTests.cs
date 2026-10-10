using Animora.Domain.Inventory;
using Animora.Domain.Purchasing;
using Animora.Domain.Finance;
using Animora.Domain.Sales;
using Animora.Domain.Cashier;
using Animora.Persistence.PostgreSql.Context;
using Animora.Domain.Boarding;
using Animora.Persistence.PostgreSql.Boarding;
using Animora.Persistence.PostgreSql.Scheduling;
using Animora.Domain.Patients;
using Animora.Domain.Clinical;
using Animora.Domain.Scheduling;
using Animora.Domain.Primitives;
using Animora.Persistence.PostgreSql.Clinical;
using Animora.Persistence.PostgreSql.Conventions;
using Animora.Server.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Animora.Server.Tests.HandlersS10;

public sealed class MoneyStockModelTests
{
    public static TheoryData<Type, string> Tables => new()
    {
        { typeof(Product), "Products" },
        { typeof(ProductCategory), "ProductCategories" },
        { typeof(UnitOfMeasure), "UnitsOfMeasure" },
        { typeof(StockMovement), "StockMovements" },
        { typeof(PurchaseOrder), "PurchaseOrders" },
        { typeof(GoodsReceipt), "GoodsReceipts" },
        { typeof(Supplier), "Suppliers" },
        { typeof(LedgerAccount), "LedgerAccounts" },
        { typeof(JournalEntry), "JournalEntries" },
        { typeof(AccountingPeriod), "AccountingPeriods" },
        { typeof(Cheque), "Cheques" },
        { typeof(Expense), "Expenses" },
        { typeof(RecurringExpense), "RecurringExpenses" },
        { typeof(RecurringExpensePayment), "RecurringExpensePayments" }
    };

    [Theory, MemberData(nameof(Tables))]
    public void MoneyStockTable_HasTenantIndexesFiltersAndUuid(Type clrType, string table)
    {
        using var db = TestSupport.CreateContext();
        var entity = db.Model.FindEntityType(clrType);
        Assert.NotNull(entity);
        Assert.Equal(table, entity.GetTableName());
        Assert.Equal("public", entity.GetSchema());
        Assert.NotEmpty(entity.GetIndexes());
        Assert.All(entity.GetIndexes(), index => Assert.Equal("TenantId", index.Properties[0].Name));
        var names = entity.GetDeclaredQueryFilters().Select(f => f.Key).ToArray();
        Assert.Contains(ServerModelConventions.TenantFilterName, names);
        Assert.Contains(ServerModelConventions.SoftDeleteFilterName, names);
        var version = entity.FindProperty(nameof(SyncableEntity.Version));
        Assert.NotNull(version);
        Assert.Equal("uuid", version.GetColumnType());
        Assert.True(version.IsConcurrencyToken);
    }

    [Theory]
    [InlineData(typeof(ProductVariant), "ProductVariants", "VariantId")]
    [InlineData(typeof(PurchaseOrderLine), "PurchaseOrderLines", "LineId")]
    [InlineData(typeof(GoodsReceiptLine), "GoodsReceiptLines", "LineId")]
    [InlineData(typeof(JournalLine), "JournalLines", "LineId")]
    [InlineData(typeof(ChequeEvent), "ChequeEvents", "EventId")]
    public void ChildRows_HaveCompositeCascadeKeys(Type type, string table, string key)
    {
        using var db = TestSupport.CreateContext();
        var row = db.Model.FindEntityType(type)!;
        Assert.Equal(table, row.GetTableName());
        Assert.Equal(new[] { "OwnerId", key }, row.FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.Equal(DeleteBehavior.Cascade, Assert.Single(row.GetForeignKeys()).DeleteBehavior);
    }
}
