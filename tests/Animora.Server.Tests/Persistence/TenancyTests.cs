using Animora.Domain.Identity;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Tenancy;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Animora.Server.Tests.Persistence;

public sealed class TenancyTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
    }

    private static Tenant NewTenant(string tenantId)
    {
        var tenant = Tenant.Create("Clinic", new FixedClock());
        tenant.TenantId = tenantId;
        return tenant;
    }

    [Fact]
    public void NullTenant_Throws_TenantContextMissing()
    {
        using var db = TestSupport.CreateContext(new NullTenantContext());
        Assert.Throws<TenantContextMissingException>(() => db.CurrentTenantId);
    }

    [Fact]
    public void Stamps_FromContext_Never_FromEntity()
    {
        var stamping = new TenantStampingInterceptor(new FixedTenantContext("TENANT-CTX"));
        using var db = TestSupport.CreateContext(new FixedTenantContext("TENANT-CTX"));
        var tenant = NewTenant(string.Empty);
        db.Tenants.Add(tenant);

        stamping.Stamp(db);

        Assert.Equal("TENANT-CTX", tenant.TenantId);
    }

    [Fact]
    public void Stamping_WithoutAnyTenant_Throws_ContextMissing()
    {
        var stamping = new TenantStampingInterceptor(new NullTenantContext());
        using var db = TestSupport.CreateContext();
        db.Tenants.Add(NewTenant(string.Empty));

        Assert.Throws<TenantContextMissingException>(() => stamping.Stamp(db));
    }

    [Fact]
    public void Mismatch_Throws_TenantMismatch()
    {
        var stamping = new TenantStampingInterceptor(new FixedTenantContext("TENANT-CTX"));
        using var db = TestSupport.CreateContext();
        db.Tenants.Add(NewTenant("TENANT-OTHER"));

        var ex = Assert.Throws<TenantMismatchException>(() => stamping.Stamp(db));
        Assert.Equal("TENANT-CTX", ex.ContextTenant);
        Assert.Equal("TENANT-OTHER", ex.EntityTenant);
    }

    [Fact]
    public void Operator_MayWriteTenantRows()
    {
        var stamping = new TenantStampingInterceptor(new FixedTenantContext(null, isOperator: true));
        using var db = TestSupport.CreateContext();
        var tenant = NewTenant("T-NEW");
        db.Tenants.Add(tenant);

        stamping.Stamp(db);

        Assert.Equal("T-NEW", tenant.TenantId);
    }

    [Fact]
    public void UnnamedIgnoreFilters_IsBanned()
    {
        var interceptor = new NoUnnamedIgnoreFiltersInterceptor(new FixedTenantContext("T1"));
        using var db = TestSupport.CreateContext(new FixedTenantContext("T1"));

        var ex = Assert.Throws<InvalidOperationException>(
            () => interceptor.QueryCompilationStarting(db.Tenants.IgnoreQueryFilters().Expression, null!));
        Assert.Contains("without names is banned", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NamedIgnore_SoftDelete_IsAllowed_ForTenantUsers()
    {
        var interceptor = new NoUnnamedIgnoreFiltersInterceptor(new FixedTenantContext("T1"));
        using var db = TestSupport.CreateContext(new FixedTenantContext("T1"));

        var expression = db.Tenants.IgnoreQueryFilters(["SoftDelete"]).Expression;
        Assert.Same(expression, interceptor.QueryCompilationStarting(expression, null!));
    }

    [Fact]
    public void NamedIgnore_Tenant_IsOperatorOnly()
    {
        using var db = TestSupport.CreateContext();
        var expression = db.Tenants.IgnoreQueryFilters(["Tenant"]).Expression;

        Assert.Throws<InvalidOperationException>(
            () => new NoUnnamedIgnoreFiltersInterceptor(new FixedTenantContext("T1")).QueryCompilationStarting(expression, null!));
        Assert.Same(
            expression,
            new NoUnnamedIgnoreFiltersInterceptor(new FixedTenantContext(null, isOperator: true)).QueryCompilationStarting(expression, null!));
    }
}
