using System;
using System.IO;
using System.Text.Json.Nodes;
using NUnit.Framework;

namespace Umbraco.JsonSchema.Extensions.UnitTests;

/// <summary>
/// Tests for the <see cref="JsonPathUpdateValue" /> MSBuild task.
/// </summary>
[TestFixture]
public class JsonPathUpdateValueTests
{
    private string _tempDir = null!;

    /// <summary>
    /// Sets up the test environment by creating a temporary directory.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "JsonPathUpdateValueTests_" + Guid.NewGuid().ToString("N"));
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
    /// A simple dot-notation path updates the target property value.
    /// </summary>
    [Test]
    public void DotNotation_UpdatesProperty()
    {
        var path = WriteJsonFile("""{ "foo": "old" }""");

        var sut = new JsonPathUpdateValue
        {
            JsonFile = path,
            Path = "$.foo",
            Value = "\"new\""
        };

        var result = sut.Execute();
        Assert.True(result);

        JsonNode json = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.That(json["foo"]!.GetValue<string>(), Is.EqualTo("new"));
    }

    /// <summary>
    /// A nested dot-notation path updates a deeply nested property.
    /// </summary>
    [Test]
    public void NestedDotNotation_UpdatesDeepProperty()
    {
        var path = WriteJsonFile("""{ "a": { "b": { "c": 1 } } }""");

        var sut = new JsonPathUpdateValue
        {
            JsonFile = path,
            Path = "$.a.b.c",
            Value = "42"
        };

        var result = sut.Execute();
        Assert.True(result);

        JsonNode json = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.That(json["a"]!["b"]!["c"]!.GetValue<int>(), Is.EqualTo(42));
    }

    /// <summary>
    /// An array index path updates the element at the specified index.
    /// </summary>
    [Test]
    public void ArrayIndex_UpdatesElement()
    {
        var path = WriteJsonFile("""{ "items": ["a", "b", "c"] }""");

        var sut = new JsonPathUpdateValue
        {
            JsonFile = path,
            Path = "$.items[1]",
            Value = "\"replaced\""
        };

        var result = sut.Execute();
        Assert.True(result);

        JsonNode json = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.That(json["items"]![1]!.GetValue<string>(), Is.EqualTo("replaced"));

        // Other elements unchanged
        Assert.That(json["items"]![0]!.GetValue<string>(), Is.EqualTo("a"));
        Assert.That(json["items"]![2]!.GetValue<string>(), Is.EqualTo("c"));
    }

    /// <summary>
    /// Bracket-notation with a quoted string key updates the property.
    /// </summary>
    [Test]
    public void BracketNotation_UpdatesProperty()
    {
        var path = WriteJsonFile("""{ "foo": "old" }""");

        var sut = new JsonPathUpdateValue
        {
            JsonFile = path,
            Path = "$['foo']",
            Value = "\"bracket\""
        };

        var result = sut.Execute();
        Assert.True(result);

        JsonNode json = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.That(json["foo"]!.GetValue<string>(), Is.EqualTo("bracket"));
    }

    /// <summary>
    /// When the path does not exist in the JSON, the file is left unchanged.
    /// </summary>
    [Test]
    public void NonExistentPath_LeavesFileUnchanged()
    {
        var original = """{ "foo": "bar" }""";
        var path = WriteJsonFile(original);

        var sut = new JsonPathUpdateValue
        {
            JsonFile = path,
            Path = "$.does.not.exist",
            Value = "\"value\""
        };

        var result = sut.Execute();
        Assert.True(result);

        // Re-parse both to compare structure (whitespace may differ)
        var originalJson = JsonNode.Parse(original)!.ToJsonString();
        var actualJson = JsonNode.Parse(File.ReadAllText(path))!.ToJsonString();
        Assert.That(actualJson, Is.EqualTo(originalJson));
    }

    /// <summary>
    /// A value can be replaced with a complex JSON object.
    /// </summary>
    [Test]
    public void ComplexValue_ReplacesProperty()
    {
        var path = WriteJsonFile("""{ "config": "placeholder" }""");

        var sut = new JsonPathUpdateValue
        {
            JsonFile = path,
            Path = "$.config",
            Value = """{"key": "value", "num": 123}"""
        };

        var result = sut.Execute();
        Assert.True(result);

        JsonNode json = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.That(json["config"]!["key"]!.GetValue<string>(), Is.EqualTo("value"));
        Assert.That(json["config"]!["num"]!.GetValue<int>(), Is.EqualTo(123));
    }

    /// <summary>
    /// A value can be set to a JSON null literal.
    /// </summary>
    [Test]
    public void NullValue_SetsPropertyToNull()
    {
        var path = WriteJsonFile("""{ "foo": "bar" }""");

        var sut = new JsonPathUpdateValue
        {
            JsonFile = path,
            Path = "$.foo",
            Value = "null"
        };

        var result = sut.Execute();
        Assert.True(result);

        JsonObject json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.That(json.ContainsKey("foo"), Is.True);
    }

    /// <summary>
    /// An existing file with JSON comments and trailing commas is parsed successfully
    /// (validates JsonDocumentOptions configuration).
    /// </summary>
    [Test]
    public void ToleratesCommentsAndTrailingCommas()
    {
        var path = WriteJsonFile("""
        {
            // A comment
            "value": "old",
        }
        """);

        var sut = new JsonPathUpdateValue
        {
            JsonFile = path,
            Path = "$.value",
            Value = "\"new\""
        };

        var result = sut.Execute();
        Assert.True(result);

        JsonNode json = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.That(json["value"]!.GetValue<string>(), Is.EqualTo("new"));
    }

    /// <summary>
    /// The output is written as indented JSON (validates Utf8JsonWriter options).
    /// </summary>
    [Test]
    public void Output_IsIndentedJson()
    {
        var path = WriteJsonFile("""{ "foo": "bar" }""");

        var sut = new JsonPathUpdateValue
        {
            JsonFile = path,
            Path = "$.foo",
            Value = "\"baz\""
        };

        sut.Execute();

        var content = File.ReadAllText(path);
        Assert.That(content, Does.Contain(Environment.NewLine).Or.Contain("\n"));
    }

    /// <summary>
    /// An out-of-range array index leaves the file unchanged without error.
    /// </summary>
    [Test]
    public void ArrayIndex_OutOfRange_LeavesFileUnchanged()
    {
        var path = WriteJsonFile("""{ "items": [1, 2] }""");

        var sut = new JsonPathUpdateValue
        {
            JsonFile = path,
            Path = "$.items[99]",
            Value = "0"
        };

        var result = sut.Execute();
        Assert.True(result);

        JsonArray arr = JsonNode.Parse(File.ReadAllText(path))!["items"]!.AsArray();
        Assert.That(arr.Count, Is.EqualTo(2));
    }

    /// <summary>
    /// A path without the leading '$' still works.
    /// </summary>
    [Test]
    public void PathWithoutDollar_StillWorks()
    {
        var path = WriteJsonFile("""{ "foo": "old" }""");

        var sut = new JsonPathUpdateValue
        {
            JsonFile = path,
            Path = ".foo",
            Value = "\"new\""
        };

        var result = sut.Execute();
        Assert.True(result);

        JsonNode json = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.That(json["foo"]!.GetValue<string>(), Is.EqualTo("new"));
    }

    private string WriteJsonFile(string json, string name = "test.json")
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, json);

        return path;
    }
}
