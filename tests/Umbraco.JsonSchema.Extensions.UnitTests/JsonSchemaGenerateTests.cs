using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
    /// The generated schema excludes non-public properties (matching System.Text.Json).
    /// </summary>
    [Test]
    public void Execute_ExcludesNonPublicProperties()
    {
        var outputPath = TempFile("schema.json");
        var sut = CreateTask(outputPath);

        sut.Execute();

        var properties = ParseSchema(outputPath)["properties"]!.AsObject();
        Assert.That(properties.ContainsKey("InternalValue"), Is.False);
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
        var enumSchema = schema["properties"]!["Status"]!;
        var enumValues = enumSchema["enum"]!.AsArray();

        var values = enumValues.Select(v => v?.GetValue<string>()).ToList();
        Assert.That(values, Does.Contain("Active"));
        Assert.That(values, Does.Contain("Inactive"));
        Assert.That(values, Does.Contain("Pending"));
    }

    /// <summary>
    /// The generated schema includes the names of flags enums, while allowing comma-separated combinations.
    /// </summary>
    [Test]
    public void Execute_FlagsEnumAllowsCombinations()
    {
        var outputPath = TempFile("schema.json");
        var sut = CreateTask(outputPath);

        sut.Execute();

        var flagsSchema = ParseSchema(outputPath)["properties"]!["Flags"]!;
        Assert.That(flagsSchema["type"]?.GetValue<string>(), Is.EqualTo("string"));

        var anyOf = flagsSchema["anyOf"]!.AsArray();
        var values = anyOf[0]!["enum"]!.AsArray().Select(v => v?.GetValue<string>()).ToList();
        Assert.That(values, Is.EqualTo(new[] { "None", "Read", "Write", "All" }));

        var pattern = new Regex(anyOf[1]!["pattern"]!.GetValue<string>());
        Assert.That(pattern.IsMatch("Read"), Is.True);
        Assert.That(pattern.IsMatch("Read, Write"), Is.True);
        Assert.That(pattern.IsMatch("Read,Write"), Is.True);
        Assert.That(pattern.IsMatch("Read, Unknown"), Is.False);
    }

    /// <summary>
    /// The generated schema includes nested types inline.
    /// </summary>
    [Test]
    public void Execute_NestedTypeGeneratesProperSchema()
    {
        var outputPath = TempFile("schema.json");
        var sut = CreateTask(outputPath);

        sut.Execute();

        var schema = ParseSchema(outputPath);
        Assert.That(schema["properties"]!["Address"], Is.Not.Null);

        var addressSchema = schema["properties"]!["Address"]!;
        Assert.That(addressSchema["properties"]!["Street"], Is.Not.Null);
        Assert.That(addressSchema["properties"]!["City"], Is.Not.Null);
    }

    /// <summary>
    /// The generated schema references recursive types instead of expanding them indefinitely.
    /// </summary>
    [Test]
    public void Execute_RecursiveTypeUsesReference()
    {
        var outputPath = TempFile("schema.json");
        var sut = CreateTask(outputPath);

        var result = sut.Execute();

        Assert.That(result, Is.True);

        var treeSchema = ParseSchema(outputPath)["properties"]!["Tree"]!;
        Assert.That(treeSchema["properties"]!["Name"], Is.Not.Null);
        Assert.That(treeSchema["properties"]!["Child"]!["properties"]!["Child"]!["$ref"]?.GetValue<string>(), Is.EqualTo("#/properties/Tree/properties/Child"));
    }

    /// <summary>
    /// The generated schema uses the property names and ignores from System.Text.Json attributes.
    /// </summary>
    [Test]
    public void Execute_UsesJsonAttributes()
    {
        var outputPath = TempFile("schema.json");
        var sut = CreateTask(outputPath);

        sut.Execute();

        var properties = ParseSchema(outputPath)["properties"]!.AsObject();
        Assert.That(properties.ContainsKey("renamed"), Is.True);
        Assert.That(properties.ContainsKey("RenamedValue"), Is.False);
        Assert.That(properties.ContainsKey("IgnoredValue"), Is.False);
    }

    /// <summary>
    /// The generated schema includes the value of <see cref="DefaultValueAttribute" /> as default.
    /// </summary>
    [Test]
    public void Execute_IncludesDefaultValues()
    {
        var outputPath = TempFile("schema.json");
        var sut = CreateTask(outputPath);

        sut.Execute();

        var properties = ParseSchema(outputPath)["properties"]!;
        Assert.That(properties["Age"]!["default"]?.GetValue<int>(), Is.EqualTo(18));
        Assert.That(properties["Status"]!["default"]?.GetValue<string>(), Is.EqualTo("Pending"));
        Assert.That(properties["Timeout"]!["default"]?.GetValue<string>(), Is.EqualTo("00:20:00"));
    }

    /// <summary>
    /// The generated schema doesn't restrict time spans to the constant format, as configuration binding parses other formats as well.
    /// </summary>
    [Test]
    public void Execute_TimeSpanAllowsAnyString()
    {
        var outputPath = TempFile("schema.json");
        var sut = CreateTask(outputPath);

        sut.Execute();

        var timeoutSchema = ParseSchema(outputPath)["properties"]!["Timeout"]!.AsObject();
        Assert.That(timeoutSchema["type"]?.GetValue<string>(), Is.EqualTo("string"));
        Assert.That(timeoutSchema.ContainsKey("pattern"), Is.False);
    }

    /// <summary>
    /// The generated schema marks obsolete properties as deprecated when included.
    /// </summary>
    [Test]
    public void Execute_MarksObsoletePropertiesAsDeprecated_WhenEnabled()
    {
        var outputPath = TempFile("schema.json");
        var sut = CreateTask(outputPath);
        sut.IncludeObsoleteProperties = true;

        sut.Execute();

        var obsoleteSchema = ParseSchema(outputPath)["properties"]!["ObsoleteValue"]!;
        Assert.That(obsoleteSchema["x-deprecated"]?.GetValue<bool>(), Is.True);
        Assert.That(obsoleteSchema["x-deprecatedMessage"]?.GetValue<string>(), Is.EqualTo("This property is obsolete."));
    }

    /// <summary>
    /// The generated schema declares the JSON schema draft.
    /// </summary>
    [Test]
    public void Execute_IncludesSchemaDraft()
    {
        var outputPath = TempFile("schema.json");
        var sut = CreateTask(outputPath);

        sut.Execute();

        var schema = ParseSchema(outputPath);
        Assert.That(schema["$schema"]?.GetValue<string>(), Is.EqualTo("http://json-schema.org/draft-04/schema#"));
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
    /// The generated schema doesn't contain boolean schemas (e.g. <c>true</c> for any value), as draft-04 doesn't support them.
    /// </summary>
    [Test]
    public void Execute_ConvertsBooleanSchemasToObjects()
    {
        var outputPath = TempFile("schema.json");
        var sut = CreateTask(outputPath);

        sut.Execute();

        var properties = ParseSchema(outputPath)["properties"]!;
        Assert.That(properties["Anything"], Is.InstanceOf<JsonObject>());
        Assert.That(properties["Anything"]!.AsObject(), Is.Empty);
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
    /// The generated schema falls back to the <see cref="System.ComponentModel.DescriptionAttribute" /> when a property has no XML documentation.
    /// </summary>
    [Test]
    public void Execute_FallsBackToDescriptionAttribute()
    {
        var outputPath = TempFile("schema.json");
        var sut = CreateTask(outputPath);

        sut.Execute();

        var properties = ParseSchema(outputPath)["properties"]!;
        Assert.That(properties["AttributeDescribed"]!["description"]?.GetValue<string>(), Is.EqualTo("A property described by an attribute."));
        Assert.That(properties["BothDescribed"]!["description"]?.GetValue<string>(), Is.EqualTo("A property described by both XML documentation and an attribute."));
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
    /// Test flags enum for schema generation.
    /// </summary>
    [Flags]
    private enum TestFlags
    {
        None = 0,
        Read = 1,
        Write = 2,
        All = Read | Write,
    }

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
    /// A recursive type for testing recursive schema generation.
    /// </summary>
    private sealed class TestNode
    {
        public string Name { get; set; } = string.Empty;

        public TestNode? Child { get; set; }
    }

    /// <summary>
    /// A base class for testing inherited properties.
    /// </summary>
    private abstract class TestModelBase
    {
        /// <summary>
        /// A property declared on the base class.
        /// </summary>
        public string BaseValue { get; set; } = string.Empty;

        /// <summary>
        /// A virtual property documented on the base class.
        /// </summary>
        public virtual string VirtualValue { get; set; } = string.Empty;
    }

    /// <summary>
    /// Primary test model exercising all schema generation scenarios.
    /// </summary>
    private sealed class TestModel : TestModelBase
    {
        /// <summary>
        /// A writable string property (should appear in schema).
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// A writable int property (should appear in schema).
        /// </summary>
        [DefaultValue(18)]
        public int Age { get; set; } = 18;

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
        /// An internal property (should be excluded from schema, matching System.Text.Json).
        /// </summary>
        internal string InternalValue { get; set; } = string.Empty;

        /// <summary>
        /// An enum property (should use string values via JsonStringEnumConverter).
        /// </summary>
        [DefaultValue(TestStatus.Pending)]
        public TestStatus Status { get; set; } = TestStatus.Pending;

        /// <summary>
        /// A flags enum property (should allow comma-separated combinations).
        /// </summary>
        public TestFlags Flags { get; set; }

        /// <summary>
        /// A time span property (should allow any string).
        /// </summary>
        [DefaultValue(typeof(TimeSpan), "00:20:00")]
        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(20);

        /// <summary>
        /// A renamed property (should use the name from JsonPropertyName).
        /// </summary>
        [JsonPropertyName("renamed")]
        public string RenamedValue { get; set; } = string.Empty;

        /// <summary>
        /// An ignored property (should be excluded from schema).
        /// </summary>
        [JsonIgnore]
        public string IgnoredValue { get; set; } = string.Empty;

        public object? Anything { get; set; }

        [System.ComponentModel.Description("A property described by an attribute.")]
        public string? AttributeDescribed { get; set; }

        /// <summary>
        /// A property described by both XML documentation and an attribute.
        /// </summary>
        [System.ComponentModel.Description("The attribute description.")]
        public string? BothDescribed { get; set; }

        /// <inheritdoc />
        public override string VirtualValue { get; set; } = string.Empty;

        /// <summary>
        /// A recursive object property (should reference itself instead of expanding indefinitely).
        /// </summary>
        public TestNode? Tree { get; set; }

        /// <summary>
        /// A nested object property (should generate proper nested schema).
        /// </summary>
        public TestAddress? Address { get; set; }
    }
}
