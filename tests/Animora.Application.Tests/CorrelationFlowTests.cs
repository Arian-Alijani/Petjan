using Animora.Application.Correlation;
using Animora.Domain.Primitives;
using Xunit;

namespace Animora.Application.Tests;

public sealed class CorrelationFlowTests
{
    [Fact]
    public void OneGestureFlowsOneCorrelationAndGesturesDiffer()
    {
        var context = new CorrelationContext();
        var first = IdGenerator.NewId();
        string handler;
        string audit;
        string log;
        using (context.Begin(new AmbientCorrelation(first, "T", "U", "D", "O")))
        {
            handler = context.CurrentId!;
            audit = context.Current!.CorrelationId;
            log = context.CurrentId!;
        }
        var second = IdGenerator.NewId();
        Assert.Equal(first, handler);
        Assert.Equal(handler, audit);
        Assert.Equal(audit, log);
        Assert.NotEqual(first, second);
    }
}
