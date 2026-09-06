using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FBC.DBRepository.Tests;

// Fixtures for the scan. They live in this file rather than TestFixtures.cs because their only
// purpose is to be found — or deliberately not found — by RegisterRepositories.

public interface IGadgetRepository : IAsyncRepository<Gadget, int>;

public sealed class Gadget : Entity<int, Gadget>
{
    public string Name { get; set; } = "";
}

public sealed class GadgetRepository(TestDbContext context)
    : EFRepositoryBase<Gadget, int, TestDbContext>(context), IGadgetRepository;

/// <summary>
/// An open generic repository. The scan must skip it: registered against
/// <c>IAsyncRepository&lt;TEntity, TId&gt;</c> with unbound parameters it would break resolution for
/// every entity in the application.
/// </summary>
public sealed class OpenGenericRepository<TEntity, TId>(TestDbContext context)
    : EFRepositoryBase<TEntity, TId, TestDbContext>(context)
    where TEntity : Entity<TId, TEntity>
    where TId : IEquatable<TId>;

[TestClass]
public sealed class RegistrationTests
{
    private static ServiceCollection ServicesWithContext()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(o => o.UseSqlite("Data Source=:memory:"));
        return services;
    }

    [TestMethod]
    public void The_closed_generic_interface_is_registered()
    {
        var services = ServicesWithContext();
        services.RegisterRepositories(typeof(RegistrationTests).Assembly);

        Assert.IsTrue(services.Any(d => d.ServiceType == typeof(IAsyncRepository<Gadget, int>)));
    }

    [TestMethod]
    public void A_named_repository_interface_is_registered()
    {
        var services = ServicesWithContext();
        services.RegisterRepositories(typeof(RegistrationTests).Assembly);

        Assert.IsTrue(services.Any(d => d.ServiceType == typeof(IGadgetRepository)));
    }

    [TestMethod]
    public void An_open_generic_repository_is_skipped()
    {
        // If it were not, every IAsyncRepository<,> resolution in the application would break.
        var services = ServicesWithContext();
        services.RegisterRepositories(typeof(RegistrationTests).Assembly);

        Assert.IsFalse(services.Any(d => d.ImplementationType == typeof(OpenGenericRepository<,>)));
    }

    [TestMethod]
    public void Concrete_types_are_not_registered_by_default()
    {
        // The trap this closes: injecting the concrete repository fails at resolution time with a
        // message about the handler that wanted it, not about the missing registration.
        var services = ServicesWithContext();
        services.RegisterRepositories(typeof(RegistrationTests).Assembly);

        Assert.IsFalse(services.Any(d => d.ServiceType == typeof(GadgetRepository)));
    }

    [TestMethod]
    public void Concrete_types_are_registered_when_asked()
    {
        var services = ServicesWithContext();
        services.RegisterRepositories(includeConcreteTypes: true, typeof(RegistrationTests).Assembly);

        Assert.IsTrue(services.Any(d => d.ServiceType == typeof(GadgetRepository)));
    }

    [TestMethod]
    public void Registered_repositories_actually_resolve()
    {
        var services = ServicesWithContext();
        services.RegisterRepositories(includeConcreteTypes: true, typeof(RegistrationTests).Assembly);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        Assert.IsNotNull(scope.ServiceProvider.GetService<IGadgetRepository>());
        Assert.IsNotNull(scope.ServiceProvider.GetService<GadgetRepository>());
        Assert.IsNotNull(scope.ServiceProvider.GetService<IAsyncRepository<Gadget, int>>());
    }

    [TestMethod]
    public void A_derived_repository_wins_over_the_one_it_derives_from()
    {
        // StrictWidgetRepository derives from WidgetRepository to override CheckRoleRequirement —
        // the pattern the library recommends for making role enforcement fail closed. Treating that
        // as an ambiguity would punish exactly the people who took the advice.
        var services = ServicesWithContext();
        services.RegisterRepositories(typeof(RegistrationTests).Assembly);

        var registration = services.Single(d => d.ServiceType == typeof(IAsyncRepository<Widget, int>));

        Assert.AreEqual(typeof(StrictWidgetRepository), registration.ImplementationType);
    }

    [TestMethod]
    public void Two_unrelated_repositories_for_one_entity_are_refused()
    {
        // Before 0.5.0 one of them won, and which one depended on the order Reflection returned the
        // types in — so the same source could resolve differently between builds.
        var services = new ServiceCollection();

        var error = Assert.ThrowsExactly<InvalidOperationException>(
            () => services.RegisterRepositories(typeof(Fixtures.Doodad).Assembly));

        StringAssert.Contains(error.Message, "PrimaryDoodadRepository");
        StringAssert.Contains(error.Message, "SecondaryDoodadRepository");
    }

    [TestMethod]
    public void An_assembly_whose_types_cannot_all_load_does_not_stop_the_scan()
    {
        // The scan falls back to every assembly in the AppDomain when none is named, and a single
        // unloadable type in an unrelated one used to take startup down with it.
        var services = ServicesWithContext();

        // Named explicitly rather than relying on the AppDomain fallback: that fallback would also
        // pick up the fixtures assembly, which is ambiguous by design.
        services.RegisterRepositories(typeof(RegistrationTests).Assembly, typeof(EFRepositoryBaseExtensions).Assembly);

        Assert.IsTrue(services.Any(d => d.ServiceType == typeof(IGadgetRepository)));
    }
}
