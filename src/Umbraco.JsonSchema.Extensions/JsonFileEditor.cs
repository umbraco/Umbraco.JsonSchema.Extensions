using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Umbraco.JsonSchema.Extensions;

/// <summary>
/// Applies changes to JSON files, only writing a file when its content changes.
/// </summary>
internal static class JsonFileEditor
{
    private static readonly JsonDocumentOptions _documentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonWriterOptions _writerOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Applies <paramref name="change" /> to the root of the JSON file and writes the file only when its content changed.
    /// </summary>
    /// <typeparam name="TRoot">The expected type of the JSON root.</typeparam>
    /// <param name="filePath">The path to the JSON file.</param>
    /// <param name="change">The change to apply to the JSON root (in place).</param>
    /// <param name="startingDocument">The JSON root to use when the file is missing or empty; when <see langword="null" />, a missing or empty file is an error.</param>
    /// <returns>
    /// <see langword="true" /> when the file was written; otherwise, <see langword="false" />.
    /// </returns>
    /// <remarks>
    /// The file is parsed allowing comments and trailing commas, which are lost when the file is written. An existing
    /// file is exclusively locked from read to write. When <paramref name="change" /> throws, the file is left untouched
    /// (and a missing file isn't created).
    /// </remarks>
    /// <exception cref="JsonFileException">The file can't be opened, is missing or empty without a starting document, contains invalid JSON or its root is not a <typeparamref name="TRoot" />.</exception>
    public static bool Update<TRoot>(string filePath, Action<TRoot> change, TRoot? startingDocument = null)
        where TRoot : JsonNode
    {
        if (!File.Exists(filePath))
        {
            if (startingDocument is null)
            {
                throw new JsonFileException(filePath, "The file does not exist.");
            }

            change(startingDocument);

            var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            using FileStream newFile = Open(filePath, FileMode.CreateNew);
            Write(newFile, startingDocument);

            return true;
        }

        using FileStream fs = Open(filePath, FileMode.Open);

        TRoot root;
        JsonNode? original = null;
        if (fs.Length == 0)
        {
            root = startingDocument ?? throw new JsonFileException(filePath, "The file is empty.");
        }
        else
        {
            JsonNode? node;
            try
            {
                node = JsonNode.Parse(fs, documentOptions: _documentOptions);
            }
            catch (JsonException ex)
            {
                throw new JsonFileException(filePath, $"The file contains invalid JSON: {ex.Message}", ex);
            }

            root = node as TRoot ?? throw new JsonFileException(filePath, $"The JSON root is not {Describe(typeof(TRoot))}.");
            original = root.DeepClone();
        }

        change(root);

        // Leave an existing file untouched when its content is unchanged
        if (original is not null && JsonNode.DeepEquals(original, root))
        {
            return false;
        }

        // Truncate and (re)write the file
        fs.SetLength(0);
        fs.Position = 0;
        Write(fs, root);

        return true;
    }

    private static FileStream Open(string filePath, FileMode mode)
    {
        try
        {
            return File.Open(filePath, mode, FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new JsonFileException(filePath, $"The file can't be opened: {ex.Message}", ex);
        }
    }

    private static void Write(FileStream fs, JsonNode root)
    {
        using var writer = new Utf8JsonWriter(fs, _writerOptions);
        root.WriteTo(writer);
    }

    private static string Describe(Type type)
        => type == typeof(JsonObject) ? "an object"
        : type == typeof(JsonArray) ? "an array"
        : type == typeof(JsonValue) ? "a value"
        : "a non-null value";
}
