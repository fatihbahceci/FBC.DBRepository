using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FBC.DBRepository.Tests;

/// <summary>A soft-deletable, role-guarded, self-validating entity — everything the library hooks into.</summary>
public sealed class Widget : Entity<int, Widget>,
    IEntityHasCreatedDate, IEntityHasUpdatedDate, IEntityHasDeletedDate,
    IEntityHasCreatedBy, IEntityHasUpdatedBy, IEntityHasDeletedBy,
    IEntityHasSoftDeleteFeature,
    IEntityHasCheckDataFor<Widget, int>,
    IEntityRequiresRole
{
    public string Name { get; set; } = "";

    public DateTime CreatedDateUTC { get; set; }
    public DateTime? UpdatedDateUTC { get; set; }
    public DateTime? DeletedDateUTC { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
    public string? DeletedBy { get; set; }
    public bool IsDeleted { get; set; }

    /// <summary>Deleting needs more than editing — the asymmetry restore has to respect.</summary>
    public string[] GetRequiredRolesFor(EntityOperation operation) =>
        operation == EntityOperation.Delete ? ["Owner"] : ["Owner", "Editor"];

    public async Task CheckDataForAsync(EntityOperation operation, bool alsoValidate, IAsyncRepository<Widget, int> repository)
    {
        Name = Name.Trim();

        if (!alsoValidate) return;

        var (name, selfId) = (Name, Id);
        if (await repository.AnyAsync(w => w.Name == name && w.Id != selfId))
            throw new InvalidOperationException($"A widget named '{name}' already exists.");
    }
}

public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<Widget> Widgets => Set<Widget>();

    /// <summary>Declared in RegistrationTests. It has no soft delete, which is what QueryableTests needs it for.</summary>
    public DbSet<Gadget> Gadgets => Set<Gadget>();
}

public class WidgetRepository(TestDbContext context, ICurrentUserProvider? user)
    : EFRepositoryBase<Widget, int, TestDbContext>(context, user);

/// <summary>A repository that refuses to run role-guarded entities without a user (the 0.5.0 override point).</summary>
public sealed class StrictWidgetRepository(TestDbContext context, ICurrentUserProvider? user)
    : WidgetRepository(context, user)
{
    protected override void CheckRoleRequirement(EntityOperation operationType, Widget entity)
    {
        if (entity is IEntityRequiresRole && _currentUserProvider is null)
            throw new InvalidOperationException(
                $"{nameof(Widget)} declares required roles but this repository has no user provider.");

        base.CheckRoleRequirement(operationType, entity);
    }
}

public sealed class FakeUser(params string[] roles) : ICurrentUserProvider
{
    public string? GetUserId() => "user-1";

    public string? GetUserName() => "Test User";

    public string[] GetRoles() => roles;

    public bool IsInRole(string role) => roles.Contains(role);
}

/// <summary>An open SQLite in-memory database; the connection has to stay open or the schema goes with it.</summary>
public sealed class TestDb : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public TestDbContext Context { get; }

    private TestDb(SqliteConnection connection, TestDbContext context)
    {
        _connection = connection;
        Context = context;
    }

    public static TestDb Create()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(connection).Options;
        var context = new TestDbContext(options);
        context.Database.EnsureCreated();

        return new TestDb(connection, context);
    }

    public WidgetRepository Repository(params string[] roles) =>
        new(Context, roles.Length == 0 ? null : new FakeUser(roles));

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
