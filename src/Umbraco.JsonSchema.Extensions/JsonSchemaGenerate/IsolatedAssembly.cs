using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Umbraco.JsonSchema.Extensions;

/// <summary>
/// An assembly loaded into an isolated, collectible load context (resolving its dependencies from the same directory)
/// without locking the files, providing its types and the summaries from the XML documentation files next to the loaded
/// assemblies.
/// </summary>
/// <remarks>
/// System.Text.Json is shared with the task, as its reflection-based resolver matches attributes (e.g.
/// <c>[JsonPropertyName]</c>) by type. Types and members of this assembly can only be used until it's disposed, which
/// unloads the load context.
/// </remarks>
internal sealed class IsolatedAssembly : IDisposable
{
    private readonly IsolatedLoadContext _loadContext;
    private readonly Assembly _assembly;
    private readonly XmlDocumentation _xmlDocumentation;

    /// <summary>
    /// Initializes a new instance of the <see cref="IsolatedAssembly" /> class, loading the assembly.
    /// </summary>
    /// <param name="assemblyPath">The path to the assembly file.</param>
    /// <param name="logMessage">Logs a message, e.g. when an XML documentation file can't be read and is ignored.</param>
    public IsolatedAssembly(string assemblyPath, Action<string>? logMessage = null)
    {
        _loadContext = new IsolatedLoadContext(assemblyPath);
        try
        {
            _assembly = _loadContext.LoadFromAssemblyFile(assemblyPath);
        }
        catch
        {
            _loadContext.Unload();
            throw;
        }

        _xmlDocumentation = new XmlDocumentation(_loadContext.GetAssemblyPath, logMessage);
    }

    /// <summary>
    /// Gets the type with the specified name from the assembly.
    /// </summary>
    /// <param name="typeName">The fully qualified type name.</param>
    /// <param name="type">The type, or <see langword="null" /> when the assembly doesn't contain it.</param>
    /// <returns>
    /// <see langword="true" /> when the assembly contains the type; otherwise, <see langword="false" />.
    /// </returns>
    public bool TryGetType(string typeName, [NotNullWhen(true)] out Type? type)
    {
        type = _assembly.GetType(typeName, throwOnError: false);

        return type is not null;
    }

    /// <summary>
    /// Gets the summary of the specified type, property or field from the XML documentation file next to its assembly.
    /// </summary>
    /// <param name="member">The type, property or field.</param>
    /// <returns>The summary text, or <see langword="null" /> when not documented.</returns>
    public string? GetSummary(MemberInfo member)
        => _xmlDocumentation.GetSummary(member);

    /// <inheritdoc />
    public void Dispose() => _loadContext.Unload();

    /// <summary>
    /// A collectible load context that loads assemblies from streams (so the files aren't locked), tracking their file
    /// paths (as <see cref="Assembly.Location" /> is empty for assemblies loaded from a stream).
    /// </summary>
    private sealed class IsolatedLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;
        private readonly Dictionary<Assembly, string> _assemblyPaths = [];

        public IsolatedLoadContext(string assemblyPath)
            : base(isCollectible: true)
            => _resolver = new AssemblyDependencyResolver(assemblyPath);

        public Assembly LoadFromAssemblyFile(string assemblyPath)
        {
            using var stream = File.OpenRead(assemblyPath);
            Assembly assembly = LoadFromStream(stream);
            _assemblyPaths[assembly] = assemblyPath;

            return assembly;
        }

        public string? GetAssemblyPath(Assembly assembly)
            => _assemblyPaths.TryGetValue(assembly, out string? assemblyPath) ? assemblyPath : (string.IsNullOrEmpty(assembly.Location) ? null : assembly.Location);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // Share System.Text.Json with the task (instead of a copy from the assembly's directory)
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
}
