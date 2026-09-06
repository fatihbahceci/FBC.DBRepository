namespace FBC.DBRepository.Tests;

[TestClass]
public sealed class PaginationTests
{
    [TestMethod]
    public async Task A_page_size_of_zero_returns_everything_in_one_page()
    {
        await using var db = TestDb.Create();
        var repository = db.Repository("Owner");

        for (var i = 0; i < 7; i++)
            await repository.ApplyOperation(EntityOperation.Create, new Widget { Name = $"W{i}" }, alsoValidate: true);

        var page = await repository.GetListAsync(itemsPerPage: 0);

        Assert.HasCount(7, page.Items);
        Assert.AreEqual(1, page.TotalPages);
    }

    [TestMethod]
    public async Task A_page_size_of_zero_ignores_the_page_number()
    {
        // The removed branch claimed to "skip to the specified page" when the page size was zero. It
        // could not: the skip was pageNumber * itemsPerPage, which is zero whenever itemsPerPage is.
        await using var db = TestDb.Create();
        var repository = db.Repository("Owner");

        for (var i = 0; i < 7; i++)
            await repository.ApplyOperation(EntityOperation.Create, new Widget { Name = $"W{i}" }, alsoValidate: true);

        var page = await repository.GetListAsync(pageNumber: 3, itemsPerPage: 0);

        Assert.HasCount(7, page.Items);
    }

    [TestMethod]
    public async Task Paging_returns_the_requested_slice()
    {
        await using var db = TestDb.Create();
        var repository = db.Repository("Owner");

        for (var i = 0; i < 7; i++)
            await repository.ApplyOperation(EntityOperation.Create, new Widget { Name = $"W{i}" }, alsoValidate: true);

        var page = await repository.GetListAsync(orderBy: q => q.OrderBy(w => w.Id), pageNumber: 1, itemsPerPage: 3);

        Assert.HasCount(3, page.Items);
        Assert.AreEqual("W3", page.Items[0].Name);
        Assert.AreEqual(3, page.TotalPages);
        Assert.IsTrue(page.HasPrevious);
        Assert.IsTrue(page.HasNext);
    }
}
