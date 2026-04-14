using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Namotion.Reflection;

namespace Umbraco.JsonSchema.Extensions;

/// <summary>
/// A collectible <see cref="AssemblyLoadContext" /> that resolves assemblies
/// from the same directory as a specified plugin assembly without locking the files.
/// XML documentation caches are automatically primed for each loaded assembly.
/// </summary>
internal sealed class PluginLoadContext : AssemblyLoadContext, IDisposable
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly DepsJsonPackageResolver _depsResolver;
    private readonly string _pluginDirectory;
    private readonly List<(Assembly Assembly, string Path)> _pendingXmlDocsPriming = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginLoadContext" /> class.
    /// </summary>
    /// <param name="pluginPath">The plugin path.</param>
    public PluginLoadContext(string pluginPath)
        : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginPath);
        _depsResolver = new DepsJsonPackageResolver(pluginPath);
        _pluginDirectory = System.IO.Path.GetDirectoryName(pluginPath) ?? string.Empty;
    }

    /// <summary>
    /// Loads an assembly from a file path without locking the file.
    /// </summary>
    /// <param name="assemblyPath">The path to the assembly file.</param>
    /// <returns>The loaded assembly.</returns>
    public Assembly LoadFromAssemblyFile(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        Assembly assembly = LoadFromStream(stream);
        _pendingXmlDocsPriming.Add((assembly, assemblyPath));
        return assembly;
    }

    /// <summary>
    /// Pre-populates the Namotion.Reflection XML docs cache for all assemblies loaded so far.
    /// Must be called after all assemblies have been loaded to avoid re-entrant assembly loading
    /// during the <see cref="Load" /> callback.
    /// </summary>
    public void PrimeXmlDocsCache()
    {
        // Process pending assemblies, including any new ones added during priming
        while (_pendingXmlDocsPriming.Count > 0)
        {
            var pending = _pendingXmlDocsPriming.ToList();
            _pendingXmlDocsPriming.Clear();

            foreach ((Assembly assembly, string assemblyPath) in pending)
            {
                PrimeXmlDocsCache(assembly, assemblyPath);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => Unload();

    /// <inheritdoc />
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Tier 1: built-in resolver (handles exe projects and local deps).
        string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);

        // Tier 2: deps.json + NuGet global cache (handles library projects with package deps).
        assemblyPath ??= _depsResolver.ResolveAssemblyToPath(assemblyName.Name!);

        // Tier 3: same-directory scan (handles ProjectReference outputs and CopyLocal scenarios).
        if (assemblyPath is null)
        {
            var candidate = Path.Combine(_pluginDirectory, assemblyName.Name + ".dll");
            if (File.Exists(candidate))
            {
                assemblyPath = candidate;
            }
        }

        if (assemblyPath is null)
        {
            return null;
        }

        using var stream = File.OpenRead(assemblyPath);
        Assembly assembly = LoadFromStream(stream);
        _pendingXmlDocsPriming.Add((assembly, assemblyPath));
        return assembly;
    }

    /// <inheritdoc />
    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        string? libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (libraryPath != null)
        {
            return LoadUnmanagedDllFromPath(libraryPath);
        }

        return nint.Zero;
    }

    private static void PrimeXmlDocsCache(Assembly assembly, string assemblyPath)
    {
        string xmlDocsPath = Path.ChangeExtension(assemblyPath, ".xml");
        if (!File.Exists(xmlDocsPath))
        {
            return;
        }

        // GetXmlDocsElement with an explicit path populates an internal cache keyed by assembly name.
        // Any type from the assembly is sufficient — subsequent lookups for all types in this assembly
        // will find the cached document.
        try
        {
            assembly.DefinedTypes.FirstOrDefault()?.GetXmlDocsElement(xmlDocsPath);
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Some types may fail to load due to missing dependencies.
            // Use the first successfully loaded type to prime the cache.
            ex.Types.FirstOrDefault(t => t is not null)?.GetXmlDocsElement(xmlDocsPath);
        }
    }
}
