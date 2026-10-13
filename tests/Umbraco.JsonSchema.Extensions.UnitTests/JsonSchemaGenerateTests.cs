using System;
using System.IO;
using System.Text.Json.Nodes;
using NUnit.Framework;

namespace Umbraco.JsonSchema.Extensions.UnitTests;

/// <summary>
/// Tests for the <see cref="JsonSchemaGenerate" /> MSBuild task, loading this test assembly (the schema conventions are
/// tested by <see cref="JsonSchemaGeneratorTests" />).
/// </summary>
[TestFixture]
public class JsonSchemaGenerateTests
{
    private string _tempDir = null!;

    /// <summary>
    /// Sets up the test environment.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "JsonSchemaGenerateTests_" + Guid.NewGuid().ToString("N"));
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
    /// The task generates the JSON schema file of the type.
    /// </summary>
    [Test]
    public void Execute_GeneratesSchemaFile()
    {
        var outputPath = TempFile("schema.json");
        var sut = CreateTask(outputPath);

        Assert.That(sut.Execute(), Is.True);

        JsonNode schema = ParseSchema(outputPath);
        Assert.That(schema["$schema"]?.GetValue<string>(), Is.EqualTo("http://json-schema.org/draft-04/schema#"));
        Assert.That(schema["properties"]!.AsObject().ContainsKey(nameof(TaskModel.Name)), Is.True);
    }

    /// <summary>
    /// The task only includes obsolete properties when enabled.
    /// </summary>
    /// <param name="includeObsoleteProperties">Whether to include obsolete properties.</param>
    [TestCase(false)]
    [TestCase(true)]
    public void Execute_IncludeObsoleteProperties(bool includeObsoleteProperties)
    {
        var outputPath = TempFile("schema.json");
        var sut = CreateTask(outputPath);
        sut.IncludeObsoleteProperties = includeObsoleteProperties;

        Assert.That(sut.Execute(), Is.True);

        Assert.That(ParseSchema(outputPath)["properties"]!.AsObject().ContainsKey("ObsoleteValue"), Is.EqualTo(includeObsoleteProperties));
    }

    /// <summary>
    /// The generated schema includes descriptions from the XML documentation file next to the (stream-loaded) assembly.
    /// </summary>
    [Test]
    public void Execute_IncludesDescriptionsFromXmlDocumentation()
    {
        var outputPath = TempFile("schema.json");
        var sut = CreateTask(outputPath);

        Assert.That(sut.Execute(), Is.True);

        JsonNode schema = ParseSchema(outputPath);
        Assert.That(schema["description"]?.GetValue<string>(), Is.EqualTo("A model to generate the JSON schema for."));
        Assert.That(schema["properties"]![nameof(TaskModel.Name)]!["description"]?.GetValue<string>(), Is.EqualTo("The name."));
    }

    /// <summary>
    /// The generated schema includes descriptions from the XML documentation of dependency assemblies (loaded from the
    /// directory of the assembly).
    /// </summary>
    [Test]
    public void Execute_IncludesDescriptionsFromDependencyAssemblies()
    {
        var outputPath = TempFile("schema.json");
        var sut = CreateTask(outputPath, typeof(DependencyModel));

        Assert.That(sut.Execute(), Is.True);

        Assert.That(
            ParseSchema(outputPath)["properties"]![nameof(DependencyModel.Dependency)]!["description"]?.GetValue<string>(),
            Is.EqualTo("Updates the value of a property in a JSON file using a JSON path expression."));
    }

    /// <summary>
    /// A missing assembly file logs an error and returns false.
    /// </summary>
    [Test]
    public void Execute_MissingAssembly_LogsErrorAndReturnsFalse()
    {
        var outputPath = TempFile("schema.json");
        var engine = new FakeBuildEngine();
        var sut = CreateTask(outputPath);
        sut.BuildEngine = engine;
        sut.AssemblyPath = TempFile("nonexistent.dll");

        Assert.That(sut.Execute(), Is.False);
        Assert.That(engine.Errors, Has.Count.EqualTo(1));
        Assert.That(outputPath, Does.Not.Exist);
    }

    /// <summary>
    /// A type that doesn't exist in the assembly logs an error mentioning the type and returns false.
    /// </summary>
    [Test]
    public void Execute_UnknownType_LogsErrorAndReturnsFalse()
    {
        var outputPath = TempFile("schema.json");
        var engine = new FakeBuildEngine();
        var sut = CreateTask(outputPath);
        sut.BuildEngine = engine;
        sut.TypeName = "Does.Not.Exist";

        Assert.That(sut.Execute(), Is.False);
        Assert.That(engine.Errors, Has.Count.EqualTo(1));
        Assert.That(engine.Errors[0], Does.Contain("Does.Not.Exist"));
        Assert.That(outputPath, Does.Not.Exist);
    }

    /// <summary>
    /// An output file that can't be written logs an error and returns false.
    /// </summary>
    [Test]
    public void Execute_UnwritableOutput_LogsErrorAndReturnsFalse()
    {
        var engine = new FakeBuildEngine();
        var sut = CreateTask(_tempDir);
        sut.BuildEngine = engine;

        Assert.That(sut.Execute(), Is.False);
        Assert.That(engine.Errors, Has.Count.EqualTo(1));
        Assert.That(engine.Errors[0], Does.StartWith("Could not write JSON schema file"));
    }

    /// <summary>
    /// The task creates the output directory if it does not exist.
    /// </summary>
    [Test]
    public void Execute_CreatesOutputDirectory()
    {
        var outputPath = Path.Combine(_tempDir, "subdir", "schema.json");
        var sut = CreateTask(outputPath);

        Assert.That(sut.Execute(), Is.True);
        Assert.That(outputPath, Does.Exist);
    }

    private static JsonSchemaGenerate CreateTask(string outputPath, Type? type = null) => new JsonSchemaGenerate
    {
        AssemblyPath = typeof(JsonSchemaGenerateTests).Assembly.Location,
        TypeName = (type ?? typeof(TaskModel)).FullName!,
        OutputPath = outputPath,
        BuildEngine = new FakeBuildEngine(),
    };

    private static JsonNode ParseSchema(string path)
        => JsonNode.Parse(File.ReadAllText(path))!;

    private string TempFile(string name) => Path.Combine(_tempDir, name);

    /// <summary>
    /// A model to generate the JSON schema for.
    /// </summary>
    private sealed class TaskModel
    {
        /// <summary>
        /// The name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        [Obsolete]
        public string ObsoleteValue { get; set; } = string.Empty;
    }

    /// <summary>
    /// A model with a property of a type declared in a dependency assembly.
    /// </summary>
    private sealed class DependencyModel
    {
        public JsonPathUpdateValue? Dependency { get; set; }
    }
}
