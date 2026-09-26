using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Domain.Identity;
using Animora.Domain.Sales;
using Animora.Domain.Time;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>
/// B04c acceptance for the B04a <see cref="PermissionEvaluator"/>: the ordered, fail-fast decision
/// (unknown → RBAC → named policy → typed resource rule). Order is asserted through the reported
/// <see cref="AuthorizationDecision.DeniedByRule"/> and an instrumented resource rule — never reflection.
/// </summary>
public sealed class PermissionEvaluatorTests
{
    private const string Tenant = "T-RBAC";
    private const string User = "U-DOC";
    private static readonly DateTimeOffset Base = new(2026, 9, 26, 8, 0, 0, TimeSpan.Zero);

    private static readonly IClock Clock = new FixedClock();

    private static PermissionEvaluator Evaluator(FakeMembershipRepository memberships,
        IResourceAuthorizationRule<Invoice>? invoiceRule = null) =>
        new(memberships, new SeededRoleRepository(Clock),
            invoiceRule ?? new InvoiceAuthorizationRule(), new CashSessionAuthorizationRule());

    private static AuthorizationSubject Subject(StaffRole role, string userId = User, string tenant = Tenant) =>
        new(tenant, userId, new HashSet<string>(StringComparer.Ordinal), role, "D-1", IsOffline: false);

    private static StaffMembership Membership(StaffRole role, string userId = User, string tenant = Tenant)
    {
        var membership = StaffMembership.Create(tenant, userId, role, Clock);
        return membership;
    }

    private static Invoice NewInvoice(string tenant, string createdBy) =>
        Invoice.Create(tenant, "INV-1", InvoiceKind.DirectSale, Base, createdBy, ownerId: "OWN-1").Value!;

    [Fact]
    public async Task UnknownCode_IsDeniedByPermissionUnknown_First()
    {
        var memberships = new FakeMembershipRepository();
        memberships.Add(Membership(StaffRole.Manager));

        var decision = await Evaluator(memberships)
            .EvaluateAsync("not.a.real.code", Subject(StaffRole.Manager), resource: null, CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal(PermissionEvaluator.UnknownRule, decision.DeniedByRule);
    }

    [Fact]
    public async Task StoredRoleGrant_AllowsTheCode()
    {
        var memberships = new FakeMembershipRepository();
        memberships.Add(Membership(StaffRole.Receptionist));

        var decision = await Evaluator(memberships)
            .EvaluateAsync(PermissionCodes.PatientsRead, Subject(StaffRole.Receptionist), resource: null, CancellationToken.None);

        Assert.True(decision.Allowed);
        Assert.Null(decision.DeniedByRule);
    }

    [Fact]
    public async Task StoredRoleWithoutTheCode_DeniesByRbac()
    {
        var memberships = new FakeMembershipRepository();
        memberships.Add(Membership(StaffRole.Receptionist)); // default receptionist has no reports.read

        var decision = await Evaluator(memberships)
            .EvaluateAsync(PermissionCodes.ReportsRead, Subject(StaffRole.Receptionist), resource: null, CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal(PermissionEvaluator.RbacRule, decision.DeniedByRule);
    }

    [Fact]
    public async Task MissingMembership_DeniesByRbac()
    {
        var memberships = new FakeMembershipRepository(); // no membership

        var decision = await Evaluator(memberships)
            .EvaluateAsync(PermissionCodes.PatientsRead, Subject(StaffRole.Receptionist), resource: null, CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal(PermissionEvaluator.RbacRule, decision.DeniedByRule);
    }

    [Fact]
    public async Task ExtraGrant_AllowsACodeBeyondTheRole()
    {
        var memberships = new FakeMembershipRepository();
        var membership = Membership(StaffRole.Receptionist);
        membership.GrantExtra(PermissionCodes.ReportsRead); // not in the receptionist default set
        memberships.Add(membership);

        var decision = await Evaluator(memberships)
            .EvaluateAsync(PermissionCodes.ReportsRead, Subject(StaffRole.Receptionist), resource: null, CancellationToken.None);

        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task DenyOverride_WinsOverARoleGrant()
    {
        var memberships = new FakeMembershipRepository();
        var membership = Membership(StaffRole.Receptionist);
        membership.Deny(PermissionCodes.PatientsWrite); // the role grants it; the deny must win
        memberships.Add(membership);

        var decision = await Evaluator(memberships)
            .EvaluateAsync(PermissionCodes.PatientsWrite, Subject(StaffRole.Receptionist), resource: null, CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal(PermissionEvaluator.RbacRule, decision.DeniedByRule);
    }

    [Fact]
    public async Task FirstFailingPolicy_IsReported_BeforeTheResourceRule()
    {
        var memberships = new FakeMembershipRepository();
        memberships.Add(Membership(StaffRole.Doctor));
        var rule = new InstrumentedInvoiceRule();
        // A foreign invoice: the doctor does not own it, so OwnRecordOrManager fails first.
        var invoice = NewInvoice(Tenant, createdBy: "U-OTHER");

        var decision = await Evaluator(memberships, rule)
            .EvaluateAsync(PermissionCodes.SalesDraft, Subject(StaffRole.Doctor), invoice, CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal(AuthorizationPolicies.OwnRecordOrManager, decision.DeniedByRule);
        Assert.False(rule.Consulted); // the policy short-circuited before the typed rule ran
    }

    [Fact]
    public async Task FirstFailingTypedResourceRule_IsReported_AfterPolicyPasses()
    {
        var memberships = new FakeMembershipRepository();
        memberships.Add(Membership(StaffRole.Manager)); // OwnRecordOrManager is satisfied by a manager
        var rule = new InstrumentedInvoiceRule();
        // Cross-tenant invoice: the policy passes (manager), the typed rule fails on the tenant binding.
        var invoice = NewInvoice("T-OTHER", createdBy: "U-OTHER");

        var decision = await Evaluator(memberships, rule)
            .EvaluateAsync(PermissionCodes.SalesDraft, Subject(StaffRole.Manager), invoice, CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal(nameof(InvoiceAuthorizationRule), decision.DeniedByRule);
        Assert.True(rule.Consulted); // RBAC and policy passed, so the typed rule was reached
    }

    /// <summary>Instrumented typed rule: records whether it was consulted and returns the real decision.</summary>
    private sealed class InstrumentedInvoiceRule : IResourceAuthorizationRule<Invoice>
    {
        private readonly InvoiceAuthorizationRule _inner = new();

        public bool Consulted { get; private set; }

        public string PermissionCode => _inner.PermissionCode;

        public bool IsSatisfied(AuthorizationSubject subject, Invoice resource)
        {
            Consulted = true;
            return _inner.IsSatisfied(subject, resource);
        }
    }
}
