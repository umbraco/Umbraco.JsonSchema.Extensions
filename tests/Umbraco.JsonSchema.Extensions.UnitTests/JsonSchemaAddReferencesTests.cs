using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Build.Framework;
using NUnit.Framework;

namespace Umbraco.JsonSchema.Extensions.UnitTests;

/// <summary>
/// Tests for the <see cref="JsonSchemaAddReferences" /> MSBuild task.
/// </summary>
[TestFixture]
public class JsonSchemaAddReferencesTests
{
    private string _tempDir = null!;

    /// <summary>
    /// Sets up the test environment by creating a temporary directory.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "JsonSchemaAddReferencesTests_" + Guid.NewGuid().ToString("N"));
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
    /// Tests whether passing empty references to the task does not create the file.
    /// </summary>
    [Test]
    public void EmptyReferences_DoesNotCreateFile()
    {
        var sut = new JsonSchemaAddReferences()
        {
            JsonSchemaFile = TempFile("should-not-exist.json"),
            References = Array.Empty<ITaskItem>()
        };

        var result = sut.Execute();
        Assert.True(result);

        FileAssert.DoesNotExist(TempFile("should-not-exist.json"));
    }

    /// <summary>
    /// A new file is created with $schema and allOf containing the single reference.
    /// </summary>
    [Test]
    public void SingleReference_CreatesNewSchemaFile()
    {
        var path = TempFile();
        var sut = new JsonSchemaAddReferences
        {
            JsonSchemaFile = path,
            References = new ITaskItem[] { new FakeTaskItem("ref1.json") }
        };

        var result = sut.Execute();
        Assert.True(result);
        FileAssert.Exists(path);

        JsonObject schema = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.That(schema["$schema"]?.GetValue<string>(), Is.EqualTo("http://json-schema.org/draft-04/schema#"));

        JsonArray? allOf = schema["allOf"]?.AsArray();
        Assert.That(allOf, Is.Not.Null);
        Assert.That(allOf!.Count, Is.EqualTo(1));
        Assert.That(allOf[0]!["$ref"]!.GetValue<string>(), Is.EqualTo("ref1.json"));
    }

    /// <summary>
    /// Multiple references appear in the allOf array ordered by their Weight metadata.
    /// </summary>
    [Test]
    public void MultipleReferences_OrderedByWeight()
    {
        var path = TempFile();
        var sut = new JsonSchemaAddReferences
        {
            JsonSchemaFile = path,
            References = new ITaskItem[]
            {
                new FakeTaskItem("second.json", weight: "20"),
                new FakeTaskItem("first.json", weight: "10"),
                new FakeTaskItem("third.json", weight: "30")
            }
        };

        var result = sut.Execute();
        Assert.True(result);

        JsonObject schema = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        JsonArray allOf = schema["allOf"]!.AsArray();
        Assert.That(allOf.Count, Is.EqualTo(3));
        Assert.That(allOf[0]!["$ref"]!.GetValue<string>(), Is.EqualTo("first.json"));
        Assert.That(allOf[1]!["$ref"]!.GetValue<string>(), Is.EqualTo("second.json"));
        Assert.That(allOf[2]!["$ref"]!.GetValue<string>(), Is.EqualTo("third.json"));
    }

    /// <summary>
    /// References without a Weight metadata value default to weight 0.
    /// </summary>
    [Test]
    public void ReferencesWithoutWeight_DefaultToZero()
    {
        var path = TempFile();
        var sut = new JsonSchemaAddReferences
        {
            JsonSchemaFile = path,
            References = new ITaskItem[]
            {
                new FakeTaskItem("weighted.json", weight: "10"),
                new FakeTaskItem("noweight.json") // no Weight → 0
            }
        };

        var result = sut.Execute();
        Assert.True(result);

        JsonArray allOf = JsonNode.Parse(File.ReadAllText(path))!["allOf"]!.AsArray();
        Assert.That(allOf[0]!["$ref"]!.GetValue<string>(), Is.EqualTo("noweight.json"));
        Assert.That(allOf[1]!["$ref"]!.GetValue<string>(), Is.EqualTo("weighted.json"));
    }

    /// <summary>
    /// When an existing schema already has allOf entries, new references are merged
    /// using union semantics (no duplicates).
    /// </summary>
    [Test]
    public void ExistingSchema_MergesWithoutDuplicates()
    {
        var path = TempFile();
        var existing = new JsonObject
        {
            ["$schema"] = "http://json-schema.org/draft-04/schema#",
            ["allOf"] = new JsonArray(new JsonObject { ["$ref"] = "existing.json" })
        };
        File.WriteAllText(path, existing.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var sut = new JsonSchemaAddReferences
        {
            JsonSchemaFile = path,
            References = new ITaskItem[]
            {
                new FakeTaskItem("existing.json"),  // duplicate
                new FakeTaskItem("new.json")         // new
            }
        };

        var result = sut.Execute();
        Assert.True(result);

        JsonArray allOf = JsonNode.Parse(File.ReadAllText(path))!["allOf"]!.AsArray();
        Assert.That(allOf.Count, Is.EqualTo(2));
        Assert.That(allOf[0]!["$ref"]!.GetValue<string>(), Is.EqualTo("existing.json"));
        Assert.That(allOf[1]!["$ref"]!.GetValue<string>(), Is.EqualTo("new.json"));
    }

    /// <summary>
    /// Existing properties other than allOf are preserved after a merge.
    /// </summary>
    [Test]
    public void ExistingSchema_PreservesOtherProperties()
    {
        var path = TempFile();
        var existing = new JsonObject
        {
            ["$schema"] = "http://json-schema.org/draft-04/schema#",
            ["title"] = "My Schema",
            ["properties"] = new JsonObject
            {
                ["name"] = new JsonObject { ["type"] = "string" }
            }
        };
        File.WriteAllText(path, existing.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var sut = new JsonSchemaAddReferences
        {
            JsonSchemaFile = path,
            References = new ITaskItem[] { new FakeTaskItem("ref.json") }
        };

        var result = sut.Execute();
        Assert.True(result);

        JsonObject schema = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.That(schema["title"]?.GetValue<string>(), Is.EqualTo("My Schema"));
        Assert.That(schema["properties"]?["name"]?["type"]?.GetValue<string>(), Is.EqualTo("string"));
    }

    /// <summary>
    /// An existing schema containing JSON comments and trailing commas can be parsed
    /// (validates JsonDocumentOptions configuration).
    /// </summary>
    [Test]
    public void ExistingSchema_ToleratesCommentsAndTrailingCommas()
    {
        var path = TempFile();
        // Write JSON with comments and a trailing comma (not valid strict JSON)
        File.WriteAllText(
            path,
            """
            {
                // This is a comment
                "$schema": "http://json-schema.org/draft-04/schema#",
                "allOf": [
                    { "$ref": "existing.json" },
                ]
            }
            """);

        var sut = new JsonSchemaAddReferences
        {
            JsonSchemaFile = path,
            References = new ITaskItem[] { new FakeTaskItem("new.json") }
        };

        var result = sut.Execute();
        Assert.True(result);

        JsonArray allOf = JsonNode.Parse(File.ReadAllText(path))!["allOf"]!.AsArray();
        Assert.That(allOf.Count, Is.EqualTo(2));
    }

    /// <summary>
    /// The output file is written as indented JSON (validates Utf8JsonWriter options).
    /// </summary>
    [Test]
    public void Output_IsIndentedJson()
    {
        var path = TempFile();
        var sut = new JsonSchemaAddReferences
        {
            JsonSchemaFile = path,
            References = new ITaskItem[] { new FakeTaskItem("ref.json") }
        };

        sut.Execute();

        // Indented JSON will contain newlines and leading whitespace
        var content = File.ReadAllText(path);
        Assert.That(content, Does.Contain(Environment.NewLine).Or.Contain("\n"));
        Assert.That(content, Does.Match(@"^\s*\{"));
    }

    private string TempFile(string name = "schema.json")
        => Path.Combine(_tempDir, name);

    /// <summary>
    /// Lightweight stub for <see cref="ITaskItem"/> used in tests.
    /// </summary>
    private sealed class FakeTaskItem : ITaskItem
    {
        private readonly Dictionary<string, string> _metadata = new();

        public FakeTaskItem(string itemSpec, string? weight = null)
        {
            ItemSpec = itemSpec;
            if (weight is not null)
            {
                _metadata["Weight"] = weight;
            }
        }

        public string ItemSpec { get; set; }

        public int MetadataCount => _metadata.Count;

        public ICollection MetadataNames => _metadata.Keys;

        public IDictionary CloneCustomMetadata() => new Dictionary<string, string>(_metadata);

        public void CopyMetadataTo(ITaskItem destinationItem) { }

        public string GetMetadata(string metadataName) =>
            _metadata.TryGetValue(metadataName, out var value) ? value : string.Empty;

        public void RemoveMetadata(string metadataName) => _metadata.Remove(metadataName);

        public void SetMetadata(string metadataName, string metadataValue) =>
            _metadata[metadataName] = metadataValue;
    }
}
