namespace FBC.DBRepository.Tests;

/// <summary>
/// Restore used to skip both the role check and the entity's own validation. Both gaps are closed in
/// 0.5.0, and these are the two ways they showed up.
/// </summary>
[TestClass]
public sealed class RestoreTests
{
    [TestMethod]
    public async Task Restoring_needs_the_role_that_deleting_needed()
    {
        await using var db = TestDb.Create();

        var owner = db.Repository("Owner");
        var widget = new Widget { Name = "First" };
        await owner.ApplyOperation(EntityOperation.Create, widget, alsoValidate: true);
        await owner.ApplyOperation(EntityOperation.Delete, widget, alsoValidate: false);

        // Before 0.5.0 this succeeded: an Editor could bring back what only an Owner could remove.
        var editor = db.Repository("Editor");
        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => editor.RestoreAsync(widget.Id));
    }

    [TestMethod]
    public async Task An_owner_can_restore()
    {
        await using var db = TestDb.Create();
        var owner = db.Repository("Owner");

        var widget = new Widget { Name = "First" };
        await owner.ApplyOperation(EntityOperation.Create, widget, alsoValidate: true);
        await owner.ApplyOperation(EntityOperation.Delete, widget, alsoValidate: false);

        var restored = await owner.RestoreAsync(widget.Id);

        Assert.IsFalse(restored.IsDeleted);
        Assert.IsNull(restored.DeletedDateUTC);
        Assert.IsNull(restored.DeletedBy);
    }

    [TestMethod]
    public async Task A_row_that_became_invalid_while_deleted_is_refused()
    {
        // The case this was written for: a unique value is taken by another row while the first one
        // sits deleted. Before 0.5.0 the restore went through and the database constraint threw, so
        // the caller saw a provider exception instead of the entity's own message.
        await using var db = TestDb.Create();
        var owner = db.Repository("Owner");

        var first = new Widget { Name = "Shared" };
        await owner.ApplyOperation(EntityOperation.Create, first, alsoValidate: true);
        await owner.ApplyOperation(EntityOperation.Delete, first, alsoValidate: false);

        var second = new Widget { Name = "Shared" };
        await owner.ApplyOperation(EntityOperation.Create, second, alsoValidate: true);

        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => owner.RestoreAsync(first.Id, alsoValidate: true));

        StringAssert.Contains(error.Message, "Shared");
    }

    [TestMethod]
    public async Task A_failed_restore_leaves_the_row_deleted()
    {
        await using var db = TestDb.Create();
        var owner = db.Repository("Owner");

        var first = new Widget { Name = "Shared" };
        await owner.ApplyOperation(EntityOperation.Create, first, alsoValidate: true);
        await owner.ApplyOperation(EntityOperation.Delete, first, alsoValidate: false);
        await owner.ApplyOperation(EntityOperation.Create, new Widget { Name = "Shared" }, alsoValidate: true);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => owner.RestoreAsync(first.Id, alsoValidate: true));

        // Validation runs before the flags are touched, so nothing is left half-restored for some
        // later SaveChanges to persist.
        Assert.IsTrue(first.IsDeleted);
    }

    [TestMethod]
    public async Task Validation_is_off_unless_it_is_asked_for()
    {
        // Backward compatibility, and the reason validation is opt-in. RestoreAsync loads the row
        // with no include, so an entity that validates its children — "an invoice must have at least
        // two lines" — would fail every restore if this ran by default. Turning it on unconditionally
        // broke exactly that in an application using this library.
        await using var db = TestDb.Create();
        var owner = db.Repository("Owner");

        var first = new Widget { Name = "Shared" };
        await owner.ApplyOperation(EntityOperation.Create, first, alsoValidate: true);
        await owner.ApplyOperation(EntityOperation.Delete, first, alsoValidate: false);
        await owner.ApplyOperation(EntityOperation.Create, new Widget { Name = "Shared" }, alsoValidate: true);

        var restored = await owner.RestoreAsync(first.Id);

        Assert.IsFalse(restored.IsDeleted);
    }

    [TestMethod]
    public async Task The_role_check_happens_either_way()
    {
        await using var db = TestDb.Create();

        var owner = db.Repository("Owner");
        var widget = new Widget { Name = "First" };
        await owner.ApplyOperation(EntityOperation.Create, widget, alsoValidate: true);
        await owner.ApplyOperation(EntityOperation.Delete, widget, alsoValidate: false);

        var editor = db.Repository("Editor");

        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(
            () => editor.RestoreAsync(widget.Id, alsoValidate: true));
    }
}
