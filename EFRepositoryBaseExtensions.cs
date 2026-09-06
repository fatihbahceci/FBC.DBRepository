using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace FBC.DBRepository;

public static class EFRepositoryBaseExtensions
{
    /// <summary>
    /// Returns the types an assembly can actually load, instead of throwing when one of them cannot.
    /// </summary>
    /// <remarks>
    /// <c>Assembly.GetTypes()</c> throws <see cref="ReflectionTypeLoadException"/> when any single type
    /// fails to load — a missing optional dependency is enough. That matters here because the scan
    /// falls back to <c>AppDomain.CurrentDomain.GetAssemblies()</c> when no assembly is named, and one
    /// unrelated assembly could take the whole registration down at startup. The types that did load
    /// are on the exception, so the scan can carry on with them.
    /// </remarks>
    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }

    private static IEnumerable<Type> SafeGetTypes(IEnumerable<Assembly> assemblies)
        => assemblies.SelectMany(SafeGetTypes);

    /// <summary>
    /// Candidate implementation types: concrete, and closed.
    /// </summary>
    /// <remarks>
    /// Open generic definitions are excluded deliberately. A <c>GenericRepository&lt;TEntity, TId&gt;</c>
    /// caught by this scan would be registered against <c>IAsyncRepository&lt;TEntity, TId&gt;</c> with
    /// unbound parameters and break resolution for every entity in the application. Keep such a type
    /// private (a nested class inside a unit of work) or accept that this scan will skip it.
    /// </remarks>
    private static IEnumerable<Type> CandidateTypes(IEnumerable<Assembly> assemblies)
        => SafeGetTypes(assemblies)
            .Where(t => !t.IsAbstract && !t.IsInterface && !t.IsGenericTypeDefinition);

    private static Assembly[] Resolve(Assembly[] assemblies)
        => assemblies.Length > 0 ? assemblies : AppDomain.CurrentDomain.GetAssemblies();

    /// <summary>
    /// Registers repositories against the closed <c>IAsyncRepository&lt;TEntity, TEntityId&gt;</c> interface,
    /// so a handler can inject <c>IAsyncRepository&lt;Device, long&gt;</c> without a named interface.
    /// </summary>
    private static IServiceCollection RegisterRepositoriesForBaseInterface(this IServiceCollection services, params Assembly[] assemblies)
    {
        var allAssemblies = Resolve(assemblies);

        var registrations = CandidateTypes(allAssemblies)
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType &&
                            i.GetGenericTypeDefinition() == typeof(IAsyncRepository<,>))
                .Select(i => new { RepositoryType = t, InterfaceType = i }));

        // Grouped rather than registered as they are found: two repositories for the same entity used
        // to register twice, and the DI container silently kept the last one. Which one that was
        // depended on the order Reflection happened to return the types in, so the same source could
        // resolve differently between builds.
        foreach (var group in registrations.GroupBy(r => r.InterfaceType))
        {
            var implementations = group.Select(r => r.RepositoryType).Distinct().ToList();

            services.AddScoped(group.Key, Pick(implementations, Describe(group.Key)));
        }

        return services;
    }

    /// <summary>
    /// Scans the given assemblies and registers repositories with a scoped lifetime.
    /// </summary>
    /// <remarks>
    /// <para>Two things are registered: the closed <c>IAsyncRepository&lt;TEntity, TEntityId&gt;</c> for every
    /// repository found, and any named repository interface that derives from it
    /// (<c>IDeviceRepository</c>, for example) against its single implementation.</para>
    /// <para><b>Concrete repository types are not registered unless you ask.</b> A class such as
    /// <c>DeviceRepository : EFRepositoryBase&lt;…&gt;</c> with no interface of its own can only be injected
    /// as <c>IAsyncRepository&lt;Device, long&gt;</c>. Injecting the concrete type — which is what you do
    /// when the repository carries queries of its own — fails at resolution time with a message about
    /// the handler, not about the missing registration, and only where DI validation is enabled. Pass
    /// <paramref name="includeConcreteTypes"/> to register those too.</para>
    /// <para>Ambiguity is an error rather than a silent choice; see the exception message.</para>
    /// </remarks>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="includeConcreteTypes">
    /// true to also register each repository class under its own type. Defaults to false, which is the
    /// behaviour of every version before 0.5.0.
    /// </param>
    /// <param name="assemblies">Assemblies to scan. Empty scans everything loaded in the current AppDomain.</param>
    public static IServiceCollection RegisterRepositories(this IServiceCollection services, bool includeConcreteTypes, params Assembly[] assemblies)
    {
        var target = typeof(IAsyncRepository<,>);
        var allAssemblies = Resolve(assemblies);

        RegisterRepositoriesForBaseInterface(services, allAssemblies);

        var allTypes = CandidateTypes(allAssemblies).ToList();

        // Named repository interfaces: non-generic and derived from IAsyncRepository<,>, e.g. IDeviceRepository.
        var repoInterfaces = SafeGetTypes(allAssemblies)
            .Where(t =>
                t.IsInterface &&
                !t.IsGenericType &&
                t.GetInterfaces()
                 .Any(i => i.IsGenericType &&
                           i.GetGenericTypeDefinition() == target))
            .ToList();

        foreach (var repoInterface in repoInterfaces)
        {
            var implementations = allTypes.Where(repoInterface.IsAssignableFrom).ToList();

            if (implementations.Count == 0)
                continue;

            services.AddScoped(repoInterface, Pick(implementations, repoInterface.FullName!));
        }

        if (includeConcreteTypes)
        {
            // The repository classes themselves, for handlers that inject the concrete type to reach
            // queries the generic interface does not carry.
            foreach (var repository in allTypes.Where(IsRepository))
                services.AddScoped(repository);
        }

        return services;
    }

    /// <summary>
    /// Registers repositories with a scoped lifetime, without registering the concrete types.
    /// Kept so that existing calls compile and behave exactly as before.
    /// </summary>
    public static IServiceCollection RegisterRepositories(this IServiceCollection services, params Assembly[] assemblies)
        => services.RegisterRepositories(includeConcreteTypes: false, assemblies);

    /// <summary>
    /// Chooses the single implementation to register, or refuses to choose.
    /// </summary>
    /// <remarks>
    /// <para>Before 0.5.0 this took whichever type Reflection returned first. Type order is not stable
    /// across builds, so a solution with two candidates could resolve differently from one compile to
    /// the next and nothing would say so.</para>
    /// <para><b>An inheritance chain is not ambiguous.</b> Deriving from a repository to override
    /// something — <c>CheckRoleRequirement</c>, for instance — is a supported pattern, and the derived
    /// type is plainly the one that was meant. Only unrelated siblings are a genuine ambiguity, and
    /// those throw.</para>
    /// </remarks>
    private static Type Pick(List<Type> implementations, string serviceName)
    {
        var winner = implementations[0];

        foreach (var candidate in implementations.Skip(1))
        {
            if (winner.IsAssignableFrom(candidate))
                winner = candidate;                     // candidate derives from the current winner
            else if (!candidate.IsAssignableFrom(winner))
                throw new InvalidOperationException(
                    $"More than one unrelated type implements '{serviceName}': " +
                    string.Join(", ", implementations.Select(t => t.FullName)) + ". " +
                    "Automatic registration cannot choose between them, and choosing at random is how " +
                    "this used to behave — register the one you want with services.AddScoped(...) and " +
                    "keep the others out of the scanned assemblies.");
        }

        return winner;
    }

    private static bool IsRepository(Type type)
        => type.GetInterfaces().Any(i => i.IsGenericType &&
                                         i.GetGenericTypeDefinition() == typeof(IAsyncRepository<,>));

    private static string Describe(Type closedInterface)
        => $"{closedInterface.Name[..closedInterface.Name.IndexOf('`')]}<" +
           string.Join(", ", closedInterface.GetGenericArguments().Select(a => a.Name)) + ">";
}
