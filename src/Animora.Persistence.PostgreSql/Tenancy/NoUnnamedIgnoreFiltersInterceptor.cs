using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Animora.Persistence.PostgreSql.Tenancy;

/// <summary>
/// Bans the unnamed <c>IgnoreQueryFilters()</c> overload (it would disable the tenant filter
/// together with soft-delete). Named overloads are allowed, except one naming the
/// <c>"Tenant"</c> filter when the caller is not an operator.
/// <para><b>ISO-03 — breadth of the operator bypass.</b> <see cref="ITenantContext.IsOperator"/> is a single boolean that, for the whole
/// context it is set on, lets ANY query name <c>IgnoreQueryFilters(["Tenant"])</c> and read across every tenant. Code running with an
/// operator context therefore <i>is</i> the tenant wall and must be reviewed/audited as such: keep operator-context code paths minimal,
/// operator-authenticated and audited, and prefer a tenant-bound context (see <c>TenantBoundContext</c>) whenever one clinic's data is
/// the target — e.g. the per-tenant backup export (BAK-02) deliberately reads through a bound context, never through this bypass.</para>
/// </summary>
public sealed class NoUnnamedIgnoreFiltersInterceptor(ITenantContext tenantContext) : IQueryExpressionInterceptor
{
    /// <summary>Name of the tenant query filter (see <c>ServerModelConventions</c>).</summary>
    public const string TenantFilterName = "Tenant";

    /// <inheritdoc />
    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
    {
        var visitor = new IgnoreFiltersVisitor();
        visitor.Visit(queryExpression);

        if (visitor.UnnamedFound)
        {
            throw new InvalidOperationException("IgnoreQueryFilters() without names is banned");
        }

        if (visitor.TenantFilterIgnored && !tenantContext.IsOperator)
        {
            throw new InvalidOperationException(
                "IgnoreQueryFilters([\"Tenant\"]) is allowed for operators only");
        }

        return queryExpression;
    }

    private sealed class IgnoreFiltersVisitor : ExpressionVisitor
    {
        public bool UnnamedFound { get; private set; }

        public bool TenantFilterIgnored { get; private set; }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.Name == "IgnoreQueryFilters"
                && node.Method.DeclaringType?.Name == "EntityFrameworkQueryableExtensions")
            {
                // Unnamed overload: (IQueryable<T>) only. Named: (IQueryable<T>, ICollection<string>).
                if (node.Arguments.Count == 1)
                {
                    UnnamedFound = true;
                }
                else if (TryEvaluate(node.Arguments[1]) is IEnumerable names
                         && names.Cast<object?>().Any(n => string.Equals(n as string, TenantFilterName, StringComparison.Ordinal)))
                {
                    TenantFilterIgnored = true;
                }
            }

            return base.VisitMethodCall(node);
        }

        private static object? TryEvaluate(Expression expression)
        {
            try
            {
                return expression switch
                {
                    ConstantExpression c => c.Value,
                    _ => Expression.Lambda(expression).Compile().DynamicInvoke(),
                };
            }
            catch (Exception ex) when (ex is InvalidOperationException or TargetInvocationException or ArgumentException)
            {
                return null;
            }
        }
    }
}
