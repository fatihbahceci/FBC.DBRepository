using Microsoft.EntityFrameworkCore;

namespace FBC.DBRepository.Tests;

/// <summary>
/// The difference between the two queryables, which is the sharpest edge in the library: one filters
/// soft-deleted rows and the other does not.
/// </summary>
[TestClass]
public sealed class QueryableTests
{
    private static async Task<WidgetRepository> WithOneDeletedAsync(TestDb db)
    {
        var repository = db.Repository("Owner");

        var kept = new Widget { Name = "Kept" };
        var removed = new Widget { Name = "Removed" };

        await repository.ApplyOperation(EntityOperation.Create, kept, alsoValidate: true);
        await repository.ApplyOperation(EntityOperation.Create, removed, alsoValidate: true);
        await repository.ApplyOperation(EntityOperation.Delete, removed, alsoValidate: false);

        return repository;
    }

    [TestMethod]
    public async Task GetQueryable_returns_deleted_rows_too()
    {
        // Not a defect — it is what the raw queryable is for. It is only a problem when the caller
        // did not know, which is why the alternatives below exist and why the XML doc says so.
        await using var db = TestDb.Create();
        var repository = await WithOneDeletedAsync(db);

        Assert.AreEqual(2, await repository.GetQueryable().CountAsync());
    }

    [TestMethod]
    public async Task GetActiveQueryable_does_not()
    {
        await using var db = TestDb.Create();
        var repository = await WithOneDeletedAsync(db);

        Assert.AreEqual(1, await repository.GetActiveQueryable().CountAsync());
    }

    [TestMethod]
    public async Task GetActiveQueryable_is_reachable_through_the_interface_as_well()
    {
        // It is a default interface member, so it has to work when the repository is held by the
        // interface — which is how handlers usually hold it.
        await using var db = TestDb.Create();
        IAsyncRepository<Widget, int> repository = await WithOneDeletedAsync(db);

        Assert.AreEqual(1, await repository.GetActiveQueryable().CountAsync());
    }

    [TestMethod]
    public async Task Passing_the_raw_queryable_to_GetListAsync_filters_it_on_the_way_through()
    {
        await using var db = TestDb.Create();
        var repository = await WithOneDeletedAsync(db);

        var page = await repository.GetListAsync(repository.GetQueryable());

        Assert.HasCount(1, page.Items);
    }

    [TestMethod]
    public async Task An_entity_without_soft_delete_gets_the_same_queryable_back()
    {
        await using var db = TestDb.Create();
        var repository = new GadgetRepository(db.Context);

        Assert.AreEqual(0, await repository.GetActiveQueryable().CountAsync());
    }
}
