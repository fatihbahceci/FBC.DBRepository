namespace FBC.DBRepository.Tests;

/// <summary>
/// A transaction belongs to the DbContext, not to the repository. These are the messages a caller
/// gets when that difference matters.
/// </summary>
[TestClass]
public sealed class TransactionTests
{
    [TestMethod]
    public async Task Repositories_sharing_a_context_write_inside_one_transaction()
    {
        await using var db = TestDb.Create();

        var first = db.Repository("Owner");
        var second = db.Repository("Owner");

        await first.BeginTransactionAsync();

        await first.ApplyOperation(EntityOperation.Create, new Widget { Name = "A" }, alsoValidate: true);
        await second.ApplyOperation(EntityOperation.Create, new Widget { Name = "B" }, alsoValidate: true);

        Assert.IsTrue(second.HasActiveTransaction, "the second repository should see the shared transaction");

        await first.RollbackTransactionAsync();

        Assert.AreEqual(0, await first.CountAsync());
    }

    [TestMethod]
    public async Task A_second_begin_on_the_same_context_says_why()
    {
        await using var db = TestDb.Create();

        var first = db.Repository("Owner");
        var second = db.Repository("Owner");

        await first.BeginTransactionAsync();

        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => second.BeginTransactionAsync());

        StringAssert.Contains(error.Message, "another repository sharing it");

        await first.RollbackTransactionAsync();
    }

    [TestMethod]
    public async Task Committing_from_a_repository_that_did_not_begin_it_says_why()
    {
        await using var db = TestDb.Create();

        var first = db.Repository("Owner");
        var second = db.Repository("Owner");

        await first.BeginTransactionAsync();

        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => second.CommitTransactionAsync());

        StringAssert.Contains(error.Message, "did not start the transaction");

        await first.RollbackTransactionAsync();
    }

    [TestMethod]
    public async Task Committing_with_no_transaction_at_all_says_that_instead()
    {
        await using var db = TestDb.Create();
        var repository = db.Repository("Owner");

        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => repository.CommitTransactionAsync());

        StringAssert.Contains(error.Message, "No transaction in progress");
    }
}
