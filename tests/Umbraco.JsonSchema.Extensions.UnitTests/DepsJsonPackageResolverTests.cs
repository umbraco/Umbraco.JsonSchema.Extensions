using System;
using System.IO;
using NUnit.Framework;

namespace Umbraco.JsonSchema.Extensions.UnitTests;

/// <summary>
/// Tests for the <see cref="DepsJsonPackageResolver" /> class.
/// </summary>
[TestFixture]
public class DepsJsonPackageResolverTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DepsJsonPackageResolverTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    /// <summary>
    /// Returns null when no deps.json file exists alongside the plugin.
    /// </summary>
    [Test]
    public void ResolveAssemblyToPath_ReturnsNull_WhenNoDepsJson()
    {
        var pluginPath = Path.Combine(_tempDir, "MyPlugin.dll");
        var sut = new DepsJsonPackageResolver(pluginPath);

        var result = sut.ResolveAssemblyToPath("SomeAssembly");

        Assert.That(result, Is.Null);
    }

    /// <summary>
    /// Resolves a package assembly to the NuGet global cache path.
    /// </summary>
    [Test]
    public void ResolveAssemblyToPath_ResolvesPackageAssembly()
    {
        var nugetRoot = Path.Combine(_tempDir, "packages");
        var dllDir = Path.Combine(nugetRoot, "my.package", "1.0.0", "lib", "net8.0");
        Directory.CreateDirectory(dllDir);
        File.WriteAllText(Path.Combine(dllDir, "MyAssembly.dll"), "fake");

        var pluginPath = Path.Combine(_tempDir, "MyPlugin.dll");
        WriteDepsJson(pluginPath, """
        {
          "runtimeTarget": { "name": ".NETCoreApp,Version=v8.0" },
          "targets": {
            ".NETCoreApp,Version=v8.0": {
              "My.Package/1.0.0": {
                "runtime": {
                  "lib/net8.0/MyAssembly.dll": {}
                }
              }
            }
          },
          "libraries": {
            "My.Package/1.0.0": {
              "type": "package",
              "path": "my.package/1.0.0"
            }
          }
        }
        """);

        var sut = new DepsJsonPackageResolver(pluginPath, nugetRoot);

        var result = sut.ResolveAssemblyToPath("MyAssembly");

        Assert.That(result, Is.Not.Null);
        Assert.That(Path.GetFileName(result!), Is.EqualTo("MyAssembly.dll"));
        Assert.That(File.Exists(result), Is.True);
    }

    /// <summary>
    /// Ignores entries with type "project" (ProjectReference outputs are not in NuGet cache).
    /// </summary>
    [Test]
    public void ResolveAssemblyToPath_IgnoresProjectTypeEntries()
    {
        var pluginPath = Path.Combine(_tempDir, "MyPlugin.dll");
        WriteDepsJson(pluginPath, """
        {
          "runtimeTarget": { "name": ".NETCoreApp,Version=v8.0" },
          "targets": {
            ".NETCoreApp,Version=v8.0": {
              "My.ProjectRef/1.0.0": {
                "runtime": {
                  "My.ProjectRef.dll": {}
                }
              }
            }
          },
          "libraries": {
            "My.ProjectRef/1.0.0": {
              "type": "project"
            }
          }
        }
        """);

        var sut = new DepsJsonPackageResolver(pluginPath, Path.Combine(_tempDir, "packages"));

        var result = sut.ResolveAssemblyToPath("My.ProjectRef");

        Assert.That(result, Is.Null);
    }

    /// <summary>
    /// Returns null for an assembly name not present in deps.json.
    /// </summary>
    [Test]
    public void ResolveAssemblyToPath_ReturnsNull_WhenAssemblyNotFound()
    {
        var pluginPath = Path.Combine(_tempDir, "MyPlugin.dll");
        WriteDepsJson(pluginPath, """
        {
          "runtimeTarget": { "name": ".NETCoreApp,Version=v8.0" },
          "targets": { ".NETCoreApp,Version=v8.0": {} },
          "libraries": {}
        }
        """);

        var sut = new DepsJsonPackageResolver(pluginPath, Path.Combine(_tempDir, "packages"));

        var result = sut.ResolveAssemblyToPath("NonExistent");

        Assert.That(result, Is.Null);
    }

    /// <summary>
    /// Handles packages where the assembly filename differs from the package ID
    /// (e.g., Umbraco.Cms.Core ships Umbraco.Core.dll).
    /// </summary>
    [Test]
    public void ResolveAssemblyToPath_HandlesAssemblyNameDifferentFromPackageId()
    {
        var nugetRoot = Path.Combine(_tempDir, "packages");
        var dllDir = Path.Combine(nugetRoot, "umbraco.cms.core", "17.3.0", "lib", "net10.0");
        Directory.CreateDirectory(dllDir);
        File.WriteAllText(Path.Combine(dllDir, "Umbraco.Core.dll"), "fake");

        var pluginPath = Path.Combine(_tempDir, "MyPlugin.dll");
        WriteDepsJson(pluginPath, """
        {
          "runtimeTarget": { "name": ".NETCoreApp,Version=v10.0" },
          "targets": {
            ".NETCoreApp,Version=v10.0": {
              "Umbraco.Cms.Core/17.3.0": {
                "runtime": {
                  "lib/net10.0/Umbraco.Core.dll": {
                    "assemblyVersion": "17.3.0.0"
                  }
                }
              }
            }
          },
          "libraries": {
            "Umbraco.Cms.Core/17.3.0": {
              "type": "package",
              "path": "umbraco.cms.core/17.3.0"
            }
          }
        }
        """);

        var sut = new DepsJsonPackageResolver(pluginPath, nugetRoot);

        var result = sut.ResolveAssemblyToPath("Umbraco.Core");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.EndWith("Umbraco.Core.dll"));
    }

    /// <summary>
    /// Returns null when the file doesn't exist on disk (e.g., corrupted NuGet cache).
    /// </summary>
    [Test]
    public void ResolveAssemblyToPath_ReturnsNull_WhenFileNotOnDisk()
    {
        var nugetRoot = Path.Combine(_tempDir, "packages");

        // Don't actually create the DLL file
        var pluginPath = Path.Combine(_tempDir, "MyPlugin.dll");
        WriteDepsJson(pluginPath, """
        {
          "runtimeTarget": { "name": ".NETCoreApp,Version=v8.0" },
          "targets": {
            ".NETCoreApp,Version=v8.0": {
              "Missing.Package/1.0.0": {
                "runtime": {
                  "lib/net8.0/Missing.dll": {}
                }
              }
            }
          },
          "libraries": {
            "Missing.Package/1.0.0": {
              "type": "package",
              "path": "missing.package/1.0.0"
            }
          }
        }
        """);

        var sut = new DepsJsonPackageResolver(pluginPath, nugetRoot);

        var result = sut.ResolveAssemblyToPath("Missing");

        Assert.That(result, Is.Null);
    }

    private static void WriteDepsJson(string pluginPath, string json)
    {
        var depsPath = Path.ChangeExtension(pluginPath, ".deps.json");
        File.WriteAllText(depsPath, json);
    }
}
