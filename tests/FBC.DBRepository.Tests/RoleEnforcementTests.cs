namespace FBC.DBRepository.Tests;

/// <summary>
/// What <see cref="IEntityRequiresRole"/> does and does not guarantee, and the 0.5.0 override that
/// lets an application make the default stricter.
/// </summary>
[TestClass]
public sealed class RoleEnforcementTests
{
    [TestMethod]
    public async Task A_user_without_the_role_is_refused()
    {
        await using var db = TestDb.Create();
        var repository = db.Repository("Editor");

        var widget = new Widget { Name = "First" };
        await repository.ApplyOperation(EntityOperation.Create, widget, alsoValidate: true);

        // Editor may create and update, but Widget requires Owner to delete.
        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(
            () => repository.ApplyOperation(EntityOperation.Delete, widget, alsoValidate: false));
    }

    [TestMethod]
    public async Task Without_a_user_provider_the_check_does_not_happen_at_all()
    {
        // Documented, deliberate, and fail-open: a repository built for a seeder or a migration has
        // no user to check against. It is also the reason CheckRoleRequirement is virtual.
        await using var db = TestDb.Create();
        var repository = db.Repository();

        var widget = new Widget { Name = "First" };
        await repository.ApplyOperation(EntityOperation.Create, widget, alsoValidate: true);
        await repository.ApplyOperation(EntityOperation.Delete, widget, alsoValidate: false);

        Assert.IsTrue(widget.IsDeleted);
    }

    [TestMethod]
    public async Task An_application_can_override_the_check_and_refuse_instead()
    {
        await using var db = TestDb.Create();
        var repository = new StrictWidgetRepository(db.Context, null);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => repository.ApplyOperation(EntityOperation.Create, new Widget { Name = "First" }, alsoValidate: true));
    }

    [TestMethod]
    public async Task The_override_still_defers_to_the_base_check_when_there_is_a_user()
    {
        await using var db = TestDb.Create();
        var repository = new StrictWidgetRepository(db.Context, new FakeUser("Editor"));

        var widget = new Widget { Name = "First" };
        await repository.ApplyOperation(EntityOperation.Create, widget, alsoValidate: true);

        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(
            () => repository.ApplyOperation(EntityOperation.Delete, widget, alsoValidate: false));
    }
}
