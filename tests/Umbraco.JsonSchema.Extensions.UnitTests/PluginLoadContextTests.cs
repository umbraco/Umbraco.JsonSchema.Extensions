using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using NUnit.Framework;

namespace Umbraco.JsonSchema.Extensions.UnitTests;

/// <summary>
/// Tests for <see cref="PluginLoadContext" />.
/// </summary>
[TestFixture]
public class PluginLoadContextTests
{
    private string _tempDir = null!;

    /// <summary>
    /// Sets up the test environment by creating a temporary directory.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "PluginLoadContextTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>
    /// Cleans up the test environment by deleting the temporary directory.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    /// <summary>
    /// System.Text.Json is shared with the task, even when the plugin directory contains its own copy (e.g. from a newer
    /// package), as its attributes are matched by type.
    /// </summary>
    [Test]
    public void Load_SystemTextJson_SharesTaskAssembly()
    {
        var pluginPath = Path.Combine(_tempDir, "Plugin.dll");
        var pluginSystemTextJsonPath = Path.Combine(_tempDir, "System.Text.Json.dll");
        File.Copy(typeof(PluginLoadContextTests).Assembly.Location, pluginPath);
        File.Copy(typeof(JsonSerializer).Assembly.Location, pluginSystemTextJsonPath);
        File.WriteAllText(Path.Combine(_tempDir, "Plugin.deps.json"), """
            {
              "runtimeTarget": { "name": ".NETCoreApp,Version=v10.0" },
              "targets": {
                ".NETCoreApp,Version=v10.0": {
                  "Plugin/1.0.0": { "runtime": { "Plugin.dll": {} } },
                  "System.Text.Json/10.0.0": { "runtime": { "System.Text.Json.dll": {} } }
                }
              },
              "libraries": {
                "Plugin/1.0.0": { "type": "project", "serviceable": false, "sha512": "" },
                "System.Text.Json/10.0.0": { "type": "project", "serviceable": false, "sha512": "" }
              }
            }
            """);

        // The plugin's own copy would be loaded when not explicitly shared
        var resolvedPath = new AssemblyDependencyResolver(pluginPath).ResolveAssemblyToPath(new AssemblyName("System.Text.Json"));
        Assert.That(resolvedPath, Is.SamePath(pluginSystemTextJsonPath));

        using var sut = new PluginLoadContext(pluginPath);
        Assembly assembly = sut.LoadFromAssemblyName(new AssemblyName("System.Text.Json"));

        Assert.That(assembly, Is.SameAs(typeof(JsonSerializer).Assembly));
    }
}
