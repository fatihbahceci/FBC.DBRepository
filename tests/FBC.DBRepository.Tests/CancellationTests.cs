namespace FBC.DBRepository.Tests;

/// <summary>
/// The write path took no cancellation token before 0.5.0, so a cancelled request still wrote.
/// </summary>
[TestClass]
public sealed class CancellationTests
{
    [TestMethod]
    public async Task A_cancelled_token_stops_the_write()
    {
        await using var db = TestDb.Create();
        var repository = db.Repository("Owner");

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => repository.ApplyOperation(
                EntityOperation.Create, new Widget { Name = "A" },
                alsoValidate: true, deletePermanent: false, cancellationToken: cancelled.Token));

        Assert.AreEqual(0, await repository.CountAsync());
    }

    [TestMethod]
    public async Task A_cancelled_token_stops_a_bulk_write()
    {
        await using var db = TestDb.Create();
        var repository = db.Repository("Owner");

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => repository.ApplyOperationRange(
                EntityOperation.Create, [new Widget { Name = "A" }, new Widget { Name = "B" }],
                alsoValidate: true, deletePermanent: false, cancellationToken: cancelled.Token));

        Assert.AreEqual(0, await repository.CountAsync());
    }

    [TestMethod]
    public async Task The_call_without_a_token_still_compiles_and_writes()
    {
        // The four-argument form is what every existing caller uses; the new overload must not have
        // made it ambiguous.
        await using var db = TestDb.Create();
        var repository = db.Repository("Owner");

        await repository.ApplyOperation(EntityOperation.Create, new Widget { Name = "A" }, alsoValidate: true);
        await repository.ApplyOperation(EntityOperation.Create, new Widget { Name = "B" }, alsoValidate: true, deletePermanent: false);

        Assert.AreEqual(2, await repository.CountAsync());
    }
}
