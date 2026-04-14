using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Umbraco.JsonSchema.Extensions;

/// <summary>
/// Resolves assembly paths by parsing a <c>.deps.json</c> file and probing the NuGet global packages folder.
/// <para>
/// <see cref="System.Runtime.Loader.AssemblyDependencyResolver" /> cannot locate NuGet-packaged
/// assemblies for library projects because the required <c>runtimeconfig.dev.json</c> (which lists
/// additional probing paths) is only emitted for executable projects. This class fills that gap by
/// reading the same <c>.deps.json</c> information and resolving against the NuGet global cache.
/// </para>
/// </summary>
internal sealed class DepsJsonPackageResolver
{
    private readonly Dictionary<string, string> _lookup;

    /// <summary>
    /// Initializes a new instance of the <see cref="DepsJsonPackageResolver" /> class.
    /// </summary>
    /// <param name="pluginPath">Path to the plugin assembly (used to locate the co-located <c>.deps.json</c>).</param>
    public DepsJsonPackageResolver(string pluginPath)
        : this(pluginPath, GetNuGetPackagesRoot())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DepsJsonPackageResolver" /> class with an explicit NuGet packages root (for testing).
    /// </summary>
    /// <param name="pluginPath">Path to the plugin assembly.</param>
    /// <param name="nugetPackagesRoot">Absolute path to the NuGet global packages folder.</param>
    internal DepsJsonPackageResolver(string pluginPath, string nugetPackagesRoot)
    {
        _lookup = BuildLookup(pluginPath, nugetPackagesRoot);
    }

    /// <summary>
    /// Attempts to resolve an assembly by its simple name to an absolute file path in the NuGet global cache.
    /// </summary>
    /// <param name="assemblySimpleName">The simple name of the assembly (e.g. <c>Umbraco.Core</c>).</param>
    /// <returns>The absolute path to the assembly DLL, or <c>null</c> if not found.</returns>
    public string? ResolveAssemblyToPath(string assemblySimpleName)
    {
        return _lookup.TryGetValue(assemblySimpleName, out var path) ? path : null;
    }

    private static Dictionary<string, string> BuildLookup(string pluginPath, string nugetPackagesRoot)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var depsJsonPath = Path.ChangeExtension(pluginPath, ".deps.json");
        if (!File.Exists(depsJsonPath))
        {
            return result;
        }

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(depsJsonPath));
        JsonElement root = doc.RootElement;

        if (!root.TryGetProperty("targets", out JsonElement targets) ||
            !root.TryGetProperty("libraries", out JsonElement libraries))
        {
            return result;
        }

        // The first (and typically only) property in "targets" is the TFM-specific target.
        JsonElement.ObjectEnumerator targetsEnumerator = targets.EnumerateObject();
        if (!targetsEnumerator.MoveNext())
        {
            return result;
        }

        JsonElement tfmTarget = targetsEnumerator.Current.Value;

        foreach (JsonProperty packageEntry in tfmTarget.EnumerateObject())
        {
            // packageEntry.Name is e.g. "Umbraco.Cms.Core/17.3.0"
            if (!packageEntry.Value.TryGetProperty("runtime", out JsonElement runtime))
            {
                continue;
            }

            // Check that this is a "package" type library (not "project").
            if (!libraries.TryGetProperty(packageEntry.Name, out JsonElement libraryEntry))
            {
                continue;
            }

            if (!libraryEntry.TryGetProperty("type", out JsonElement typeElement) ||
                !string.Equals(typeElement.GetString(), "package", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!libraryEntry.TryGetProperty("path", out JsonElement pathElement))
            {
                continue;
            }

            string? packagePath = pathElement.GetString();
            if (string.IsNullOrEmpty(packagePath))
            {
                continue;
            }

            foreach (JsonProperty dllEntry in runtime.EnumerateObject())
            {
                // dllEntry.Name is e.g. "lib/net10.0/Umbraco.Core.dll"
                string dllRelativePath = dllEntry.Name;
                string assemblyName = Path.GetFileNameWithoutExtension(dllRelativePath);

                string absolutePath = Path.Combine(nugetPackagesRoot, packagePath, dllRelativePath);
                absolutePath = Path.GetFullPath(absolutePath);

                if (File.Exists(absolutePath) && !result.ContainsKey(assemblyName))
                {
                    result[assemblyName] = absolutePath;
                }
            }
        }

        return result;
    }

    private static string GetNuGetPackagesRoot()
    {
        string? envValue = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrEmpty(envValue))
        {
            return envValue;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages");
    }
}
