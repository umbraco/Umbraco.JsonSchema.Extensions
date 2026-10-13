using System;
using System.IO;
using System.Text.Json.Nodes;
using NUnit.Framework;

namespace Umbraco.JsonSchema.Extensions.UnitTests;

/// <summary>
/// Tests for <see cref="JsonFileEditor" />.
/// </summary>
[TestFixture]
public class JsonFileEditorTests
{
    private string _tempDir = null!;

    /// <summary>
    /// Sets up the test environment by creating a temporary directory.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "JsonFileEditorTests_" + Guid.NewGuid().ToString("N"));
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
    /// A change to an existing file is written and reported.
    /// </summary>
    [Test]
    public void ExistingFile_Changed_WritesFile()
    {
        var path = WriteJsonFile("""{ "foo": "old" }""");

        var written = JsonFileEditor.Update<JsonObject>(path, json => json["foo"] = "new");

        Assert.That(written, Is.True);
        Assert.That(ReadJsonFile(path)["foo"]!.GetValue<string>(), Is.EqualTo("new"));
    }

    /// <summary>
    /// When the content is unchanged (ignoring formatting), the file is not written at all.
    /// </summary>
    [Test]
    public void ExistingFile_Unchanged_DoesNotWriteFile()
    {
        var original = """{"config":{"key":"value","num":123}}""";
        var path = WriteJsonFile(original);
        var lastWriteTime = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, lastWriteTime);

        var written = JsonFileEditor.Update<JsonObject>(path, json => json["config"] = JsonNode.Parse("""{ "key" : "value", "num" : 123 }"""));

        Assert.That(written, Is.False);
        Assert.That(File.ReadAllText(path), Is.EqualTo(original));
        Assert.That(File.GetLastWriteTimeUtc(path), Is.EqualTo(lastWriteTime));
    }

    /// <summary>
    /// An existing file with comments and trailing commas is parsed, and left as-is when unchanged.
    /// </summary>
    [Test]
    public void ExistingFile_WithCommentsAndTrailingCommas_IsParsedAndPreservedWhenUnchanged()
    {
        var original = """
            {
                // A comment
                "foo": "bar",
            }
            """;
        var path = WriteJsonFile(original);

        var written = JsonFileEditor.Update<JsonObject>(path, json => json["foo"] = "bar");

        Assert.That(written, Is.False);
        Assert.That(File.ReadAllText(path), Is.EqualTo(original));
    }

    /// <summary>
    /// The file is written as indented JSON, without escaping non-ASCII or HTML-sensitive characters.
    /// </summary>
    [Test]
    public void Output_IsIndentedAndRelaxedEscaped()
    {
        var path = WriteJsonFile("""{ "foo": "bar" }""");

        JsonFileEditor.Update<JsonObject>(path, json => json["foo"] = "Æblegrød & <'+'>");

        var content = File.ReadAllText(path);
        Assert.That(content, Does.Match(@"^\{\r?\n  ""foo"""));
        Assert.That(content, Does.Contain("Æblegrød & <'+'>"));
    }

    /// <summary>
    /// A missing file is created from the starting document (including its directory), with the change applied.
    /// </summary>
    [Test]
    public void MissingFile_WithStartingDocument_CreatesFile()
    {
        var path = Path.Combine(_tempDir, "nested", "dir", "new.json");

        var written = JsonFileEditor.Update(path, json => json["added"] = true, new JsonObject { ["start"] = 1 });

        Assert.That(written, Is.True);
        JsonNode json = ReadJsonFile(path);
        Assert.That(json["start"]!.GetValue<int>(), Is.EqualTo(1));
        Assert.That(json["added"]!.GetValue<bool>(), Is.True);
    }

    /// <summary>
    /// A missing file without a starting document is an error, and the file is not created.
    /// </summary>
    [Test]
    public void MissingFile_WithoutStartingDocument_Throws()
    {
        var path = Path.Combine(_tempDir, "missing.json");

        var ex = Assert.Throws<JsonFileException>(() => JsonFileEditor.Update<JsonObject>(path, _ => { }));

        Assert.That(ex!.FilePath, Is.EqualTo(path));
        Assert.That(path, Does.Not.Exist);
    }

    /// <summary>
    /// An empty file is replaced by the starting document, with the change applied.
    /// </summary>
    [Test]
    public void EmptyFile_WithStartingDocument_WritesStartingDocument()
    {
        var path = WriteJsonFile(string.Empty);

        var written = JsonFileEditor.Update(path, json => json["added"] = true, new JsonObject { ["start"] = 1 });

        Assert.That(written, Is.True);
        Assert.That(ReadJsonFile(path)["start"]!.GetValue<int>(), Is.EqualTo(1));
    }

    /// <summary>
    /// An empty file without a starting document is an error.
    /// </summary>
    [Test]
    public void EmptyFile_WithoutStartingDocument_Throws()
    {
        var path = WriteJsonFile(string.Empty);

        Assert.Throws<JsonFileException>(() => JsonFileEditor.Update<JsonObject>(path, _ => { }));
    }

    /// <summary>
    /// A file with invalid JSON is an error, and the file is left untouched.
    /// </summary>
    [Test]
    public void InvalidJson_Throws()
    {
        var original = """{ "foo": """;
        var path = WriteJsonFile(original);

        Assert.Throws<JsonFileException>(() => JsonFileEditor.Update<JsonObject>(path, _ => { }));
        Assert.That(File.ReadAllText(path), Is.EqualTo(original));
    }

    /// <summary>
    /// A root that isn't of the expected type (including <c>null</c>) is an error.
    /// </summary>
    /// <param name="json">The JSON file content.</param>
    [TestCase("[]")]
    [TestCase("\"value\"")]
    [TestCase("null")]
    public void UnexpectedRoot_Throws(string json)
    {
        var path = WriteJsonFile(json);

        Assert.Throws<JsonFileException>(() => JsonFileEditor.Update<JsonObject>(path, _ => { }));
        Assert.That(File.ReadAllText(path), Is.EqualTo(json));
    }

    /// <summary>
    /// When the change throws, an existing file is left untouched.
    /// </summary>
    [Test]
    public void ChangeThrows_LeavesExistingFileUntouched()
    {
        var original = """{ "foo": "bar" }""";
        var path = WriteJsonFile(original);

        Assert.Throws<InvalidOperationException>(() => JsonFileEditor.Update<JsonObject>(path, json =>
        {
            json["foo"] = "changed";
            throw new InvalidOperationException();
        }));
        Assert.That(File.ReadAllText(path), Is.EqualTo(original));
    }

    /// <summary>
    /// When the change throws, a missing file is not created.
    /// </summary>
    [Test]
    public void ChangeThrows_DoesNotCreateMissingFile()
    {
        var path = Path.Combine(_tempDir, "new.json");

        Assert.Throws<InvalidOperationException>(() => JsonFileEditor.Update(path, _ => throw new InvalidOperationException(), new JsonObject()));
        Assert.That(path, Does.Not.Exist);
    }

    /// <summary>
    /// Writing a document replaces the file, even when its content is unchanged.
    /// </summary>
    [Test]
    public void Write_UnchangedContent_StillWritesFile()
    {
        var path = WriteJsonFile("""{"foo":"bar"}""");
        var lastWriteTime = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, lastWriteTime);

        JsonFileEditor.Write(path, new JsonObject { ["foo"] = "bar" });

        Assert.That(File.GetLastWriteTimeUtc(path), Is.GreaterThan(lastWriteTime));
        Assert.That(ReadJsonFile(path)["foo"]!.GetValue<string>(), Is.EqualTo("bar"));
    }

    /// <summary>
    /// Writing a document creates the file and its directory when they don't exist.
    /// </summary>
    [Test]
    public void Write_MissingFile_CreatesFileAndDirectory()
    {
        var path = Path.Combine(_tempDir, "nested", "dir", "new.json");

        JsonFileEditor.Write(path, new JsonObject { ["foo"] = "bar" });

        Assert.That(ReadJsonFile(path)["foo"]!.GetValue<string>(), Is.EqualTo("bar"));
    }

    /// <summary>
    /// Writing a document uses the same formatting as updating a file.
    /// </summary>
    [Test]
    public void Write_UsesSameFormattingAsUpdate()
    {
        var writtenPath = Path.Combine(_tempDir, "written.json");
        var updatedPath = Path.Combine(_tempDir, "updated.json");

        JsonFileEditor.Write(writtenPath, new JsonObject { ["foo"] = "Æblegrød & <'+'>", ["items"] = new JsonArray(1, 2) });
        JsonFileEditor.Update(updatedPath, json => json["items"] = new JsonArray(1, 2), new JsonObject { ["foo"] = "Æblegrød & <'+'>" });

        Assert.That(File.ReadAllText(writtenPath), Is.EqualTo(File.ReadAllText(updatedPath)));
    }

    /// <summary>
    /// When writing a document fails, the partially written file is removed.
    /// </summary>
    [Test]
    public void Write_Fails_RemovesPartiallyWrittenFile()
    {
        var path = WriteJsonFile("""{"foo":"bar"}""");

        Assert.Throws<InvalidOperationException>(() => JsonFileEditor.Write(path, new JsonObject { ["foo"] = JsonValue.Create(new ThrowingValue()) }));
        Assert.That(path, Does.Not.Exist);
    }

    private string WriteJsonFile(string json, string name = "test.json")
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, json);

        return path;
    }

    private static JsonNode ReadJsonFile(string path)
        => JsonNode.Parse(File.ReadAllText(path))!;

    private sealed class ThrowingValue
    {
        public string Value => throw new InvalidOperationException();
    }
}
