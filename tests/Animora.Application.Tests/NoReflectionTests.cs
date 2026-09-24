using System.Collections.Frozen;
using System.Reflection;
using Animora.Application.Dispatch;
using Xunit;

namespace Animora.Application.Tests;

public sealed class NoReflectionTests
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    [Fact]
    public void Registry_IsBackedByFrozenDictionary()
    {
        var fields = typeof(HandlerRegistry).GetFields(All);
        Assert.Contains(fields, f => f.FieldType.IsGenericType &&
                                     f.FieldType.GetGenericTypeDefinition() == typeof(FrozenDictionary<,>));
        Assert.All(fields, f => Assert.True(f.IsInitOnly, $"{f.Name} must be readonly"));

        using var sp = TestHost.Build(new StepLog());
        Assert.IsAssignableFrom<FrozenDictionary<Type, HandlerEntry>>(sp.Registry().Entries);
    }

    [Fact]
    public void DispatchNamespace_HasNoReflectionInvocationMembers()
    {
        Type[] forbidden = [typeof(MethodInfo), typeof(MethodBase), typeof(ConstructorInfo), typeof(Activator)];
        var dispatchTypes = typeof(HandlerRegistry).Assembly.GetTypes()
            .Where(t => t.Namespace is "Animora.Application.Dispatch" or "Animora.Application.Behaviors")
            // Delegate types (PipelineStep<T>) inherit Delegate.Method from the BCL — not our member.
            .Where(t => !typeof(Delegate).IsAssignableFrom(t));

        foreach (var type in dispatchTypes)
        {
            foreach (var f in type.GetFields(All | BindingFlags.DeclaredOnly))
                Assert.DoesNotContain(f.FieldType, forbidden);
            foreach (var p in type.GetProperties(All | BindingFlags.DeclaredOnly))
                Assert.DoesNotContain(p.PropertyType, forbidden);
            foreach (var m in type.GetMethods(All | BindingFlags.DeclaredOnly))
            {
                Assert.DoesNotContain(m.ReturnType, forbidden);
                Assert.All(m.GetParameters(), p => Assert.DoesNotContain(p.ParameterType, forbidden));
            }
        }
    }

    [Fact]
    public void EveryEntry_HasPrecompiledTypedPipeline()
    {
        using var sp = TestHost.Build(new StepLog());
        foreach (var entry in sp.Registry().Entries.Values)
        {
            var expected = typeof(PipelineStep<>).MakeGenericType(entry.ResultType);
            Assert.IsType(expected, entry.Pipeline);
            Assert.NotNull(entry.Invoker);
        }
    }

    [Fact]
    public async Task UntypedInvoker_RunsSameChain()
    {
        var log = new StepLog();
        using var sp = TestHost.Build(log);
        Assert.True(sp.Registry().TryGet(typeof(LoggedQuery), out var entry));
        using var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(sp);
        var boxed = await entry.Invoker(scope.ServiceProvider, new LoggedQuery(), TestHost.QueryCtx(), CancellationToken.None);
        Assert.Equal(42, ((Domain.Results.Result<int>)boxed).Value);
        Assert.Equal(["Authorization", "Entitlement", "Handler"], log.Steps);
    }
}
