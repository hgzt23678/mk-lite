using System.Collections.ObjectModel;
using System.Reflection;
using Microsoft.AspNetCore.Components;

namespace ActivityPub.Misskey.Blazor.Routing;

/// <summary>
/// Explicitly selected route assemblies for a host that embeds the Misskey frontend.
/// </summary>
/// <remarks>
/// The production registration uses <see cref="Empty"/>. Additional assemblies can only be
/// selected through concrete routed component types, which avoids loading an assembly from a
/// configuration string or scanning the application directory.
/// </remarks>
public sealed class MisskeyFrontendRouteAssemblies
{
    public static MisskeyFrontendRouteAssemblies Empty { get; } = new([]);

    private MisskeyFrontendRouteAssemblies(IReadOnlyList<Assembly> assemblies)
    {
        Assemblies = assemblies;
    }

    public IReadOnlyList<Assembly> Assemblies { get; }

    public static MisskeyFrontendRouteAssemblies FromRouteComponents(params Type[] routeComponents)
    {
        ArgumentNullException.ThrowIfNull(routeComponents);
        if (routeComponents.Length == 0)
        {
            return Empty;
        }

        var assemblies = new List<Assembly>(routeComponents.Length);
        foreach (Type componentType in routeComponents)
        {
            ArgumentNullException.ThrowIfNull(componentType);
            if (!typeof(IComponent).IsAssignableFrom(componentType) || componentType.IsAbstract)
            {
                throw new ArgumentException(
                    $"'{componentType.FullName}' is not a concrete Razor component.",
                    nameof(routeComponents));
            }

            if (componentType.GetCustomAttributes<RouteAttribute>(inherit: false).Any() is false)
            {
                throw new ArgumentException(
                    $"'{componentType.FullName}' does not declare a component route.",
                    nameof(routeComponents));
            }

            Assembly assembly = componentType.Assembly;
            if (assembly == typeof(Routes).Assembly)
            {
                throw new ArgumentException(
                    "The frontend assembly is already the primary route assembly.",
                    nameof(routeComponents));
            }

            if (!assemblies.Contains(assembly))
            {
                assemblies.Add(assembly);
            }
        }

        return new MisskeyFrontendRouteAssemblies(
            new ReadOnlyCollection<Assembly>(assemblies));
    }
}
