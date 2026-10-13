using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Umbraco.JsonSchema.Extensions;

/// <summary>
/// A collectible <see cref="AssemblyLoadContext" /> that resolves assemblies
/// from the same directory as a specified plugin assembly without locking the files.
/// The file paths of the loaded assemblies are tracked, so their XML documentation files can be found.
/// </summary>
internal sealed class PluginLoadContext : AssemblyLoadContext, IDisposable
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly Dictionary<Assembly, string> _assemblyPaths = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginLoadContext" /> class.
    /// </summary>
    /// <param name="pluginPath">The plugin path.</param>
    public PluginLoadContext(string pluginPath)
        : base(isCollectible: true)
        => _resolver = new AssemblyDependencyResolver(pluginPath);

    /// <summary>
    /// Loads an assembly from a file path without locking the file.
    /// </summary>
    /// <param name="assemblyPath">The path to the assembly file.</param>
    /// <returns>The loaded assembly.</returns>
    public Assembly LoadFromAssemblyFile(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        Assembly assembly = LoadFromStream(stream);
        _assemblyPaths[assembly] = assemblyPath;
        return assembly;
    }

    /// <summary>
    /// Gets the file path of an assembly, which is not available from <see cref="Assembly.Location" /> for assemblies loaded from a stream.
    /// </summary>
    /// <param name="assembly">The assembly.</param>
    /// <returns>The file path of the assembly, or <c>null</c> when unknown.</returns>
    public string? GetAssemblyPath(Assembly assembly)
        => _assemblyPaths.TryGetValue(assembly, out string? assemblyPath) ? assemblyPath : (string.IsNullOrEmpty(assembly.Location) ? null : assembly.Location);

    /// <inheritdoc />
    public void Dispose() => Unload();

    /// <inheritdoc />
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Always share System.Text.Json with the task, as the schema exporter matches its attributes (e.g. JsonPropertyName) by type
        if (assemblyName.Name == "System.Text.Json")
        {
            return null;
        }

        string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
        {
            return LoadFromAssemblyFile(assemblyPath);
        }

        return null;
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
}
