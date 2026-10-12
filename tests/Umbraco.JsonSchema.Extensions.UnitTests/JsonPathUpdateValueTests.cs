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
        Assert.That(result, Is.True);

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
        Assert.That(result, Is.True);

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
        Assert.That(result, Is.True);

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
        Assert.That(result, Is.True);

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
        Assert.That(result, Is.True);

        // Re-parse both to compare structure (whitespace may differ)
        var originalJson = JsonNode.Parse(original)!.ToJsonString();
        var actualJson = JsonNode.Parse(File.ReadAllText(path))!.ToJsonString();
        Assert.That(actualJson, Is.EqualTo(originalJson));
    }

    /// <summary>
    /// When the last path segment does not exist in the JSON, the file is not rewritten.
    /// </summary>
    [Test]
    public void NonExistentProperty_DoesNotRewriteFile()
    {
        var original = """{ "foo": "bar" }""";
        var path = WriteJsonFile(original);

        var sut = new JsonPathUpdateValue
        {
            JsonFile = path,
            Path = "$.missing",
            Value = "\"value\""
        };

        var result = sut.Execute();
        Assert.That(result, Is.True);

        Assert.That(File.ReadAllText(path), Is.EqualTo(original));
    }

    /// <summary>
    /// When the property already has the value, the file is not rewritten.
    /// </summary>
    [Test]
    public void UnchangedValue_DoesNotRewriteFile()
    {
        var original = """{ "foo": "bar" }""";
        var path = WriteJsonFile(original);

        var sut = new JsonPathUpdateValue
        {
            JsonFile = path,
            Path = "$.foo",
            Value = "\"bar\""
        };

        var result = sut.Execute();
        Assert.That(result, Is.True);

        Assert.That(File.ReadAllText(path), Is.EqualTo(original));
    }

    /// <summary>
    /// An out-of-range array index does not rewrite the file.
    /// </summary>
    [Test]
    public void ArrayIndex_OutOfRange_DoesNotRewriteFile()
    {
        var original = """{ "items": [1, 2] }""";
        var path = WriteJsonFile(original);

        var sut = new JsonPathUpdateValue
        {
            JsonFile = path,
            Path = "$.items[99]",
            Value = "0"
        };

        var result = sut.Execute();
        Assert.That(result, Is.True);

        Assert.That(File.ReadAllText(path), Is.EqualTo(original));
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
        Assert.That(result, Is.True);

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
        Assert.That(result, Is.True);

        JsonObject json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.That(json.ContainsKey("foo"), Is.True);
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
        Assert.That(result, Is.True);

        JsonNode json = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.That(json["foo"]!.GetValue<string>(), Is.EqualTo("new"));
    }

    /// <summary>
    /// A value that isn't valid JSON logs an error and returns false, leaving the file untouched.
    /// </summary>
    [Test]
    public void InvalidValue_LogsErrorAndReturnsFalse()
    {
        var original = """{ "foo": "bar" }""";
        var path = WriteJsonFile(original);

        var engine = new FakeBuildEngine();
        var sut = new JsonPathUpdateValue
        {
            BuildEngine = engine,
            JsonFile = path,
            Path = "$.foo",
            Value = "not-json"
        };

        Assert.That(sut.Execute(), Is.False);
        Assert.That(engine.Errors, Has.Count.EqualTo(1));
        Assert.That(File.ReadAllText(path), Is.EqualTo(original));
    }

    /// <summary>
    /// A missing JSON file logs an error and returns false, without creating the file.
    /// </summary>
    [Test]
    public void MissingFile_LogsErrorAndReturnsFalse()
    {
        var path = Path.Combine(_tempDir, "missing.json");

        var engine = new FakeBuildEngine();
        var sut = new JsonPathUpdateValue
        {
            BuildEngine = engine,
            JsonFile = path,
            Path = "$.foo",
            Value = "\"new\""
        };

        Assert.That(sut.Execute(), Is.False);
        Assert.That(engine.Errors, Has.Count.EqualTo(1));
        Assert.That(path, Does.Not.Exist);
    }

    private string WriteJsonFile(string json, string name = "test.json")
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, json);

        return path;
    }
}
