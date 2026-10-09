using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Transactions;
using Npgsql;
using Xunit;

namespace Animora.Server.Tests.Persistence;

/// <summary>Pure retry-core tests: fake attempt delegate, deterministic rng, no-op delay, no database.</summary>
public sealed class RetryPolicyTests
{
    private static PostgresException Pg(string sqlState) =>
        new("boom", "ERROR", "ERROR", sqlState);

    private static bool IsTransient(Exception ex) => ex is PostgresException pg && RetryPolicy.IsTransient(pg.SqlState);

    private static Task NoDelay(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    [Theory]
    [InlineData("40001", true)]
    [InlineData("40P01", true)]
    [InlineData("23505", false)]
    [InlineData("42P01", false)]
    [InlineData(null, false)]
    public void IsTransient_ClassifiesSqlState(string? sqlState, bool expected) =>
        Assert.Equal(expected, RetryPolicy.IsTransient(sqlState));

    [Theory]
    [InlineData(0.0, 50)]
    [InlineData(1.0, 250)]
    [InlineData(0.5, 150)]
    public void Backoff_IsJitteredWithin50To250Ms(double unit, double expectedMs) =>
        Assert.Equal(expectedMs, RetryPolicy.BackoffFor(1, () => unit).TotalMilliseconds, 3);

    [Fact]
    public async Task Retries40001_Max3_ThenFails()
    {
        int attempts = 0;
        var delays = new List<TimeSpan>();

        PostgresException ex = await Assert.ThrowsAsync<PostgresException>(() =>
            RetryPolicy.ExecuteAsync<int>(
                (_, _) => { attempts++; throw Pg("40001"); },
                IsTransient,
                () => 0.5,
                (span, _) => { delays.Add(span); return Task.CompletedTask; },
                CancellationToken.None));

        Assert.Equal("40001", ex.SqlState);
        Assert.Equal(RetryPolicy.MaxAttempts, attempts);
        Assert.Equal(3, attempts);
        Assert.Equal(2, delays.Count);
        Assert.All(delays, d => Assert.InRange(d.TotalMilliseconds, 50, 250));
    }

    [Fact]
    public async Task Succeeds_AfterTransientFailures_ReturnsResult()
    {
        int attempts = 0;

        Result<int> result = await RetryPolicy.ExecuteAsync(
            (number, _) =>
            {
                attempts++;
                return number < 3 ? throw Pg("40P01") : Task.FromResult(Result<int>.Ok(42));
            },
            IsTransient,
            () => 0.1,
            NoDelay,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task DoesNotRetry23505_Unmapped()
    {
        int attempts = 0;

        await Assert.ThrowsAsync<PostgresException>(() =>
            RetryPolicy.ExecuteAsync<int>(
                (_, _) => { attempts++; throw Pg("23505"); },
                IsTransient,
                () => 0.5,
                NoDelay,
                CancellationToken.None));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task HandlerFailureResult_IsReturned_NotRetried_NotThrown()
    {
        int attempts = 0;

        Result<int> result = await RetryPolicy.ExecuteAsync(
            (_, _) => { attempts++; return Task.FromResult(Result<int>.Fail(ErrorCodes.ConcurrencyConflict)); },
            IsTransient,
            () => 0.5,
            NoDelay,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Cancellation_IsNeverSwallowed()
    {
        int attempts = 0;

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            RetryPolicy.ExecuteAsync<int>(
                (_, _) => { attempts++; throw new OperationCanceledException(); },
                _ => true,
                () => 0.5,
                NoDelay,
                CancellationToken.None));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task AlreadyCancelledToken_ThrowsBeforeFirstAttempt()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        int attempts = 0;

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            RetryPolicy.ExecuteAsync(
                (_, _) => { attempts++; return Task.FromResult(Result<int>.Ok(1)); },
                IsTransient,
                () => 0.5,
                NoDelay,
                cts.Token));

        Assert.Equal(0, attempts);
    }

    [Fact]
    public void UniqueViolationMapping_DefaultsToNone_AndOptInMapsToConcurrencyConflict()
    {
        Assert.False(UniqueViolationMapping.None.MapToConcurrencyConflict);
        Assert.True(UniqueViolationMapping.Enabled.MapToConcurrencyConflict);

        var dbEx = new Microsoft.EntityFrameworkCore.DbUpdateException("dup", Pg("23505"));

        Assert.Null(ServerUnitOfWork.TryMapStoreFailure<int>(dbEx, UniqueViolationMapping.None));

        Result<int>? mapped = ServerUnitOfWork.TryMapStoreFailure<int>(dbEx, UniqueViolationMapping.Enabled);
        Assert.NotNull(mapped);
        Assert.True(mapped.Value.IsFailure);
        Assert.Equal(ErrorCodes.ConcurrencyConflict, mapped.Value.Error);
    }

    [Fact]
    public void ConcurrencyException_MapsToConcurrencyConflict()
    {
        Result<int>? mapped = ServerUnitOfWork.TryMapStoreFailure<int>(
            new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException("stale"),
            UniqueViolationMapping.None);

        Assert.NotNull(mapped);
        Assert.Equal(ErrorCodes.ConcurrencyConflict, mapped.Value.Error);
    }
}
