using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.Build.Framework;
using NUnit.Framework;

namespace Umbraco.JsonSchema.Extensions.UnitTests;

/// <summary>
/// Tests for the <see cref="JsonSchemaGenerate" /> MSBuild task.
/// </summary>
[TestFixture]
public class JsonSchemaGenerateTests
{
    private string _tempDir = null!;
    private string _assemblyPath = null!;
    private string _typeName = null!;

    /// <summary>
    /// Sets up the test environment.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "JsonSchemaGenerateTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _assemblyPath = typeof(TestModel).Assembly.Location;
        _typeName = typeof(TestModel).FullName!;
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
    /// The task generates a valid JSON schema file.
    /// </summary>
    [Test]
    public void Execute_GeneratesSchemaFile()
    {
        var outputPath = TempFile("schema.json");
        var sut = CreateTask(outputPath);

        var result = sut.Execute();

        Assert.That(result, Is.True);
        Assert.That(File.Exists(outputPath), Is.True);

        var json = JsonNode.Parse(File.ReadAllText(outputPath))!;
        Assert.That(json["type"]?.GetValue<string>(), Is.EqualTo("object"));
    }

    /// <summary>
    /// The generated schema includes writable properties.
    /// </summary>
    [Test]
    public void Execute_IncludesWritableProperties()
    {
        var outputPath = TempFile("schema.json");
        var sut = CreateTask(outputPath);

        sut.Execute();

        var schema = ParseSchema(outputPath);
        Assert.That(schema["properties"]!["Name"], Is.Not.Null);
        Assert.That(schema["properties"]!["Age"], Is.Not.Null);
    }

    /// <summary>
    /// The generated schema excludes read-only properties.
    /// </summary>
    [Test]
    public void Execute_ExcludesReadOnlyProperties()
    {
        var outputPath = TempFile("schema.json");
        var sut = CreateTask(outputPath);

        sut.Execute();

        var properties = ParseSchema(outputPath)["properties"]!.AsObject();
        Assert.That(properties.ContainsKey("ReadOnlyValue"), Is.False);
        Assert.That(properties.ContainsKey("ComputedValue"), Is.False);
    }

    /// <summary>
    /// The generated schema excludes obsolete properties by default.
    /// </summary>
    [Test]
    public void Execute_ExcludesObsoleteProperties()
    {
        var outputPath = TempFile("schema.json");
        var sut = CreateTask(outputPath);

        sut.Execute();

        var properties = ParseSchema(outputPath)["properties"]!.AsObject();
        Assert.That(properties.ContainsKey("ObsoleteValue"), Is.False);
    }

    /// <summary>
    /// The generated schema includes obsolete properties when enabled.
    /// </summary>
    [Test]
    public void Execute_IncludesObsoleteProperties_WhenEnabled()
    {
        var outputPath = TempFile("schema.json");
        var sut = CreateTask(outputPath);
        sut.IncludeObsoleteProperties = true;

        sut.Execute();

        var properties = ParseSchema(outputPath)["properties"]!.AsObject();
        Assert.That(properties.ContainsKey("ObsoleteValue"), Is.True);
    }

    /// <summary>
    /// The generated schema uses string values for enums.
    /// </summary>
    [Test]
    public void Execute_EnumUsesStringValues()
    {
        var outputPath = TempFile("schema.json");
        var sut = CreateTask(outputPath);

        sut.Execute();

        var schema = ParseSchema(outputPath);
        var enumSchema = schema["definitions"]!["TestStatus"]!;
        var enumValues = enumSchema["enum"]!.AsArray();

        var values = enumValues.Select(v => v?.GetValue<string>()).ToList();
        Assert.That(values, Does.Contain("Active"));
        Assert.That(values, Does.Contain("Inactive"));
        Assert.That(values, Does.Contain("Pending"));
    }

    /// <summary>
    /// The generated schema includes nested type definitions.
    /// </summary>
    [Test]
    public void Execute_NestedTypeGeneratesProperSchema()
    {
        var outputPath = TempFile("schema.json");
        var sut = CreateTask(outputPath);

        sut.Execute();

        var schema = ParseSchema(outputPath);
        Assert.That(schema["properties"]!["Address"], Is.Not.Null);

        var addressSchema = schema["definitions"]!["TestAddress"]!;
        Assert.That(addressSchema["properties"]!["Street"], Is.Not.Null);
        Assert.That(addressSchema["properties"]!["City"], Is.Not.Null);
    }

    /// <summary>
    /// The generated schema allows additional properties.
    /// </summary>
    [Test]
    public void Execute_AllowsAdditionalProperties()
    {
        var outputPath = TempFile("schema.json");
        var sut = CreateTask(outputPath);

        sut.Execute();

        var schema = ParseSchema(outputPath);
        Assert.That(
            schema.AsObject().ContainsKey("additionalProperties"),
            Is.False,
            "Schema should not explicitly restrict additional properties");
    }

    /// <summary>
    /// The generated schema includes XML doc descriptions.
    /// </summary>
    [Test]
    public void Execute_IncludesTypeDescription()
    {
        var outputPath = TempFile("schema.json");
        var sut = CreateTask(outputPath);

        sut.Execute();

        var schema = ParseSchema(outputPath);
        Assert.That(schema["description"]?.GetValue<string>(), Is.Not.Null.And.Not.Empty);
    }

    /// <summary>
    /// The generated schema includes XML doc descriptions for properties.
    /// </summary>
    [Test]
    public void Execute_IncludesPropertyDescriptions()
    {
        var outputPath = TempFile("schema.json");
        var sut = CreateTask(outputPath);

        sut.Execute();

        var properties = ParseSchema(outputPath)["properties"]!;
        Assert.That(properties["Name"]!["description"]?.GetValue<string>(), Is.Not.Null.And.Not.Empty);
        Assert.That(properties["Age"]!["description"]?.GetValue<string>(), Is.Not.Null.And.Not.Empty);
        Assert.That(properties["Status"]!["description"]?.GetValue<string>(), Is.Not.Null.And.Not.Empty);
    }

    /// <summary>
    /// The task returns false when the assembly file does not exist.
    /// </summary>
    [Test]
    public void Execute_MissingAssembly_ReturnsFalse()
    {
        var sut = new JsonSchemaGenerate
        {
            AssemblyPath = Path.Combine(_tempDir, "nonexistent.dll"),
            TypeName = _typeName,
            OutputPath = TempFile("schema.json"),
            BuildEngine = new FakeBuildEngine(),
        };

        var result = sut.Execute();

        Assert.That(result, Is.False);
    }

    /// <summary>
    /// The task creates the output directory if it does not exist.
    /// </summary>
    [Test]
    public void Execute_CreatesOutputDirectory()
    {
        var outputPath = Path.Combine(_tempDir, "subdir", "schema.json");
        var sut = CreateTask(outputPath);

        var result = sut.Execute();

        Assert.That(result, Is.True);
        Assert.That(File.Exists(outputPath), Is.True);
    }

    private JsonSchemaGenerate CreateTask(string outputPath) => new JsonSchemaGenerate
    {
        AssemblyPath = _assemblyPath,
        TypeName = _typeName,
        OutputPath = outputPath,
        BuildEngine = new FakeBuildEngine(),
    };

    private static JsonNode ParseSchema(string path)
        => JsonNode.Parse(File.ReadAllText(path))!;

    private string TempFile(string name) => Path.Combine(_tempDir, name);

    /// <summary>
    /// Test enum for schema generation.
    /// </summary>
    private enum TestStatus
    {
        Active,
        Inactive,
        Pending,
    }

    /// <summary>
    /// A nested address type for testing nested schema generation.
    /// </summary>
    private sealed class TestAddress
    {
        public string Street { get; set; } = string.Empty;

        public string City { get; set; } = string.Empty;
    }

    /// <summary>
    /// Primary test model exercising all schema generation scenarios.
    /// </summary>
    private sealed class TestModel
    {
        /// <summary>
        /// A writable string property (should appear in schema).
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// A writable int property (should appear in schema).
        /// </summary>
        public int Age { get; set; }

        /// <summary>
        /// A read-only property (should be excluded from schema).
        /// </summary>
        public string ReadOnlyValue { get; } = "constant";

        /// <summary>
        /// A computed read-only property (should be excluded from schema).
        /// </summary>
        public string ComputedValue => $"Computed-{Name}";

        /// <summary>
        /// An obsolete property (should be excluded from schema).
        /// </summary>
        [Obsolete("This property is obsolete.")]
        public string ObsoleteValue { get; set; } = string.Empty;

        /// <summary>
        /// An enum property (should use string values via JsonStringEnumConverter).
        /// </summary>
        public TestStatus Status { get; set; }

        /// <summary>
        /// A nested object property (should generate proper nested schema).
        /// </summary>
        public TestAddress? Address { get; set; }
    }

    private sealed class FakeBuildEngine : IBuildEngine
    {
        public bool ContinueOnError => false;

        public int LineNumberOfTaskNode => 0;

        public int ColumnNumberOfTaskNode => 0;

        public string ProjectFileOfTaskNode => string.Empty;

        public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) => true;

        public void LogCustomEvent(CustomBuildEventArgs e) { }

        public void LogErrorEvent(BuildErrorEventArgs e) { }

        public void LogMessageEvent(BuildMessageEventArgs e) { }

        public void LogWarningEvent(BuildWarningEventArgs e) { }
    }
}
