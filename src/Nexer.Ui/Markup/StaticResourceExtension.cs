namespace Nexer.Ui.Markup;

/// <summary>
/// <c>{StaticResource key}</c>: resolves a resource once, while the XAML is loaded, by walking the parent stack
/// (innermost first) for objects implementing <see cref="IResourceProvider"/>, then the document root, then an
/// <see cref="IResourceProvider"/> supplied by the service provider (e.g. the application).
/// </summary>
public sealed class StaticResourceExtension
{
    /// <summary>Creates the extension with no key; set <see cref="ResourceKey"/>.</summary>
    public StaticResourceExtension()
    {
    }

    /// <summary>Creates the extension for <paramref name="resourceKey"/>.</summary>
    public StaticResourceExtension(string resourceKey) => ResourceKey = resourceKey;

    /// <summary>The resource key.</summary>
    public string? ResourceKey { get; set; }

    /// <summary>Resolves the resource.</summary>
    /// <exception cref="KeyNotFoundException">No provider in scope defines <see cref="ResourceKey"/>.</exception>
    public object? ProvideValue(IServiceProvider serviceProvider)
    {
        var key = ResourceKey ?? throw new InvalidOperationException("StaticResource requires a resource key.");
        foreach (var provider in EnumerateProviders(serviceProvider))
        {
            if (provider.TryGetResource(key, out var value))
            {
                return value;
            }
        }

        var target = serviceProvider?.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;
        var where = target?.TargetObject is { } obj
            ? $" (while setting {DescribeProperty(target.TargetProperty)} on {obj.GetType().Name})"
            : "";
        throw new KeyNotFoundException($"StaticResource '{key}' was not found{where}. Define it in the Resources of an enclosing element or the application.");
    }

    private static IEnumerable<IResourceProvider> EnumerateProviders(IServiceProvider? serviceProvider)
    {
        if (serviceProvider is null)
        {
            yield break;
        }

        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        if (serviceProvider.GetService(typeof(IParentStackProvider)) is IParentStackProvider parents)
        {
            foreach (var parent in parents.Parents)
            {
                if (parent is IResourceProvider provider && seen.Add(parent))
                {
                    yield return provider;
                }
            }
        }

        if (serviceProvider.GetService(typeof(IRootObjectProvider)) is IRootObjectProvider { RootObject: IResourceProvider root }
            && seen.Add(root))
        {
            yield return root;
        }

        if (serviceProvider.GetService(typeof(IResourceProvider)) is IResourceProvider external && seen.Add(external))
        {
            yield return external;
        }
    }

    private static string DescribeProperty(object? property) => property switch
    {
        null => "a property",
        System.Reflection.MemberInfo member => member.Name,
        _ => property.ToString() ?? "a property",
    };
}
