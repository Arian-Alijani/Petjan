using System;
using Animora.Domain.Cashier;
using Animora.Domain.Results;
using Animora.Domain.ValueObjects;
using Xunit;

namespace Animora.Server.Tests.HandlersS09;

/// <summary>
/// Cash-session invariants: expected/variance math, the variance-note requirement, the
/// unsettled-commands close guard and closed-session immutability.
/// </summary>
public sealed class ServerS09RederiveTests
{
    private static readonly DateTimeOffset Opened = new(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);

    private static CashSession Open(FakeClock clock, long floatMinor = 500_000) =>
        CashSession.Open("T1", "CS-1", "CASHIER1", Money.Irt(floatMinor), clock.UtcNow, "T1").Value!;

    private static CashMovement Movement(CashMovementKind kind, long amountMinor) =>
        CashMovement.Create(kind, Money.Irt(amountMinor), "reason", Opened, "U1").Value!;

    [Fact]
    public void ExpectedClosing_IsFloatPlusMovements()
    {
        var clock = new FakeClock(Opened);
        var session = Open(clock);

        Assert.True(session.AddMovement(Movement(CashMovementKind.SalePayment, 200_000)).IsSuccess);
        Assert.True(session.AddMovement(Movement(CashMovementKind.PettyCashOut, -50_000)).IsSuccess);

        Assert.Equal(Money.Irt(650_000), session.ExpectedClosing);
    }

    [Fact]
    public void Variance_IsDeclaredMinusExpected()
    {
        var clock = new FakeClock(Opened);
        var session = Open(clock);
        session.AddMovement(Movement(CashMovementKind.SalePayment, 200_000));

        Assert.True(session.BeginClose(Money.Irt(690_000), "counted twice", clock, hasUnsettled: false).IsSuccess);

        Assert.Equal(Money.Irt(690_000), session.DeclaredClosing);
        Assert.Equal(Money.Irt(-10_000), session.Variance);
        Assert.Equal(CashSessionState.Closing, session.State);
    }

    [Fact]
    public void Variance_WithEmptyNote_IsRefused()
    {
        var clock = new FakeClock(Opened);
        var session = Open(clock);
        session.AddMovement(Movement(CashMovementKind.SalePayment, 200_000));

        var result = session.BeginClose(Money.Irt(710_000), note: "  ", clock, hasUnsettled: false);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.CashVarianceNoteRequired, result.Error);
        Assert.Equal(CashSessionState.Open, session.State);
    }

    [Fact]
    public void ZeroVariance_NeedsNoNote()
    {
        var clock = new FakeClock(Opened);
        var session = Open(clock);

        Assert.True(session.BeginClose(Money.Irt(500_000), note: null, clock, hasUnsettled: false).IsSuccess);
        Assert.True(session.Variance.IsZero);
    }

    [Fact]
    public void Close_WithUnsettledCommands_IsRefused()
    {
        var clock = new FakeClock(Opened);
        var session = Open(clock);

        var result = session.BeginClose(Money.Irt(500_000), null, clock, hasUnsettled: true);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.CashSessionUnsettledCommands, result.Error);
        Assert.Equal(CashSessionState.Open, session.State);
    }

    [Fact]
    public void HappyPath_OpenToReconciled()
    {
        var clock = new FakeClock(Opened);
        var session = Open(clock);

        Assert.True(session.BeginClose(Money.Irt(500_000), null, clock, hasUnsettled: false).IsSuccess);
        Assert.True(session.Close(clock).IsSuccess);
        Assert.Equal(CashSessionState.Closed, session.State);
        Assert.Equal(clock.UtcNow, session.ClosedUtc);
        Assert.True(session.IsClosed);

        Assert.True(session.Reconcile("MANAGER1", clock).IsSuccess);
        Assert.Equal(CashSessionState.Reconciled, session.State);
    }

    [Fact]
    public void ClosedSession_RefusesFurtherMovements()
    {
        var clock = new FakeClock(Opened);
        var session = Open(clock);
        session.BeginClose(Money.Irt(500_000), null, clock, hasUnsettled: false);
        session.Close(clock);

        var result = session.AddMovement(Movement(CashMovementKind.SalePayment, 1_000));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.CashSessionClosed, result.Error);
    }

    [Fact]
    public void EnsureNoOpenSession_ReflectsTheCallersAnswer()
    {
        Assert.True(CashSession.EnsureNoOpenSession(anyOpenExists: false).IsSuccess);

        var result = CashSession.EnsureNoOpenSession(anyOpenExists: true);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.CashSessionAlreadyOpen, result.Error);
    }

    [Fact]
    public void NegativeOpeningFloat_IsRefused()
    {
        var result = CashSession.Open("T1", "CS-1", "CASHIER1", Money.Irt(-1), Opened);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.ValidationFailed, result.Error);
    }
}
