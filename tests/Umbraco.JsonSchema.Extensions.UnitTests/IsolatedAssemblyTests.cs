using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using NUnit.Framework;

namespace Umbraco.JsonSchema.Extensions.UnitTests;

/// <summary>
/// Tests for <see cref="IsolatedAssembly" />, loading this test assembly.
/// </summary>
[TestFixture]
public class IsolatedAssemblyTests
{
    private string _tempDir = null!;

    /// <summary>
    /// Sets up the test environment by creating a temporary directory.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "IsolatedAssemblyTests_" + Guid.NewGuid().ToString("N"));
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
    /// A type of the assembly is found, loaded in isolation (separately from the already loaded assembly).
    /// </summary>
    [Test]
    public void TryGetType_ExistingType_ReturnsIsolatedType()
    {
        using var sut = new IsolatedAssembly(typeof(DocumentedFixture).Assembly.Location);

        Assert.That(sut.TryGetType(typeof(DocumentedFixture).FullName!, out Type? type), Is.True);
        Assert.That(type!.FullName, Is.EqualTo(typeof(DocumentedFixture).FullName));
        Assert.That(type, Is.Not.SameAs(typeof(DocumentedFixture)));
    }

    /// <summary>
    /// A type that doesn't exist in the assembly isn't found.
    /// </summary>
    [Test]
    public void TryGetType_UnknownType_ReturnsFalse()
    {
        using var sut = new IsolatedAssembly(typeof(DocumentedFixture).Assembly.Location);

        Assert.That(sut.TryGetType("Does.Not.Exist", out Type? type), Is.False);
        Assert.That(type, Is.Null);
    }

    /// <summary>
    /// The summary is read from the XML documentation file next to the (stream-loaded) assembly.
    /// </summary>
    [Test]
    public void GetSummary_ReturnsSummaryFromXmlDocumentationFile()
    {
        using var sut = new IsolatedAssembly(typeof(DocumentedFixture).Assembly.Location);
        sut.TryGetType(typeof(DocumentedFixture).FullName!, out Type? type);

        Assert.That(sut.GetSummary(type!), Is.EqualTo("A documented fixture."));
    }

    /// <summary>
    /// System.Text.Json is shared with the task, even when the assembly directory contains its own copy (e.g. from a
    /// newer package), as its attributes are matched by type.
    /// </summary>
    [Test]
    public void TryGetType_TypeUsingSystemTextJson_SharesTaskAssembly()
    {
        var assemblyPath = Path.Combine(_tempDir, "Plugin.dll");
        File.Copy(typeof(SystemTextJsonFixture).Assembly.Location, assemblyPath);
        File.Copy(typeof(JsonSerializer).Assembly.Location, Path.Combine(_tempDir, "System.Text.Json.dll"));
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

        // The directory's own copy would be loaded when not explicitly shared (only comparing the end of the path, as the
        // temporary directory can be a symbolic link that is resolved, e.g. /var to /private/var on macOS)
        var resolvedPath = new AssemblyDependencyResolver(assemblyPath).ResolveAssemblyToPath(new AssemblyName("System.Text.Json"));
        Assert.That(resolvedPath, Does.EndWith(Path.Combine(Path.GetFileName(_tempDir), "System.Text.Json.dll")));

        using var sut = new IsolatedAssembly(assemblyPath);
        sut.TryGetType(typeof(SystemTextJsonFixture).FullName!, out Type? type);

        Type optionsType = type!.GetProperty(nameof(SystemTextJsonFixture.Options))!.PropertyType;
        Assert.That(optionsType.Assembly, Is.SameAs(typeof(JsonSerializer).Assembly));
    }

    /// <summary>
    /// A documented fixture.
    /// </summary>
    private sealed class DocumentedFixture
    {
    }

    private sealed class SystemTextJsonFixture
    {
        public JsonSerializerOptions? Options { get; set; }
    }
}
