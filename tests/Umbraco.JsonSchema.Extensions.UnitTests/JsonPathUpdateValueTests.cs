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
    /// A malformed path logs an error and returns false, leaving the file untouched.
    /// </summary>
    [Test]
    public void InvalidPath_LogsErrorAndReturnsFalse()
    {
        var original = """{ "items": ["a"] }""";
        var path = WriteJsonFile(original);

        var engine = new FakeBuildEngine();
        var sut = new JsonPathUpdateValue
        {
            BuildEngine = engine,
            JsonFile = path,
            Path = "$.items[-1]",
            Value = "\"new\""
        };

        Assert.That(sut.Execute(), Is.False);
        Assert.That(engine.Errors, Has.Count.EqualTo(1));
        Assert.That(File.ReadAllText(path), Is.EqualTo(original));
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
