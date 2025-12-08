using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace FBC.Repository;

public static class EFRepositoryBaseExtensions
{
    /// <summary>
    /// It's registers repositories in the DI container using the <c>IAsyncRepository&lt;TEntity, TEntityId&gt;</c> interface.
    /// Example:
    /// <code>
    /// internal sealed class CreateDeviceHandler(
    ///     ILogger&lt;CreateDeviceHandler&gt; logger,
    ///     IAsyncRepository&lt;Device, long&gt; repo
    /// ) : IRequestHandler&lt;Command, long&gt;
    /// </code>
    /// </summary>
    /// <param name="services"></param>
    /// <param name="assemblies"></param>
    /// <returns></returns>
    private static IServiceCollection RegisterRepositoriesForBaseInterface(this IServiceCollection services, params Assembly[] assemblies)
    {
        var allAssemblies = assemblies.Length > 0 ? assemblies : AppDomain.CurrentDomain.GetAssemblies();
        var repositoryTypes = allAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => !t.IsAbstract && !t.IsInterface)
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType &&
                            i.GetGenericTypeDefinition() == typeof(IAsyncRepository<,>))
                .Select(i => new { RepositoryType = t, InterfaceType = i }));
        foreach (var repository in repositoryTypes)
        {
            services.AddScoped(repository.InterfaceType, repository.RepositoryType);
        }
        return services;
    }

    /// <summary>
    /// Registers all non-generic repository interfaces that inherit from IAsyncRepository&lt;,&gt; and their
    /// corresponding implementations with scoped lifetime in the dependency injection container.
    /// </summary>
    /// <remarks>This method scans the specified assemblies for interfaces that inherit from
    /// IAsyncRepository&lt;,&gt; and registers their concrete implementations as scoped services. If multiple
    /// implementations exist for an interface, only the first one found is registered. This method is intended to
    /// simplify repository registration in applications using dependency injection.</remarks>
    /// <param name="services">The IServiceCollection to which the repository services will be added.</param>
    /// <param name="_assemblies">An optional array of assemblies to scan for repository interfaces and implementations. If not specified or
    /// empty, all assemblies loaded in the current application domain are scanned.</param>
    /// <returns>The IServiceCollection instance with the repository services registered.</returns>
    public static IServiceCollection RegisterRepositories(this IServiceCollection services, params Assembly[] _assemblies)
    {
        var target = typeof(IAsyncRepository<,>);
        RegisterRepositoriesForBaseInterface(services, _assemblies);
        var assemblies = _assemblies.Length > 0 ? _assemblies : AppDomain.CurrentDomain.GetAssemblies();
        // Scan all non-abstract, non-interface types from the provided assemblies
        var allTypes = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => !t.IsAbstract && !t.IsInterface)
            .ToList();

        //All interfaces that are derived from IAsyncRepository<,>
        var repoInterfaces = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t =>
                t.IsInterface &&
                t.IsGenericType == false && //like IDeviceRepository 
                t.GetInterfaces()
                 .Any(i => i.IsGenericType &&
                           i.GetGenericTypeDefinition() == target)
            )
            .ToList();

        foreach (var repoInterface in repoInterfaces)
        {
            var impl = allTypes.FirstOrDefault(c =>
                repoInterface.IsAssignableFrom(c)); //the class that implements the interface. If there are multiple, it takes the first one.

            if (impl != null)
            {
                services.AddScoped(repoInterface, impl);
            }
        }

        return services;
    }
}
