using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Build.Framework;

namespace Umbraco.JsonSchema.Extensions;

/// <summary>
/// Adds references to a JSON schema file.
/// </summary>
/// <seealso cref="Microsoft.Build.Utilities.Task" />
public class JsonSchemaAddReferences : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// Gets or sets the JSON schema file.
    /// </summary>
    /// <value>
    /// The JSON schema file.
    /// </value>
    [Required]
    public string JsonSchemaFile { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the references to add.
    /// </summary>
    /// <value>
    /// The references to add.
    /// </value>
    [Required]
    public ITaskItem[] References { get; set; } = Array.Empty<ITaskItem>();

    /// <summary>
    /// Gets or sets the JSON path to the object the <c>allOf</c> references are added to.
    /// </summary>
    /// <value>
    /// The JSON path (e.g. <c>$.properties.extensions.items</c>) to the object the references are added to. Defaults to
    /// the root of the schema. Intermediate objects are created when they do not exist.
    /// </value>
    /// <remarks>
    /// Only object property segments are supported (dot and bracket notation, with an optional leading <c>$</c>); array
    /// indices are not. This allows composing references into a nested location (such as a single property) instead of
    /// constraining the whole schema.
    /// </remarks>
    public string TargetPath { get; set; } = string.Empty;

    /// <inheritdoc />
    public override bool Execute()
    {
        if (References.Length == 0)
        {
            // No references to add
            return true;
        }

        using FileStream fs = File.Open(JsonSchemaFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        JsonObject schema;
        if (fs.Length == 0)
        {
            // Create new schema
            schema = new JsonObject
            {
                ["$schema"] = "http://json-schema.org/draft-04/schema#"
            };
        }
        else
        {
            // Read existing schema file
            schema = (JsonObject)JsonNode.Parse(fs, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            })!;
        }

        // Resolve the object at the target path (the root when no path is specified) before modifying the file, so an
        // invalid path fails the build cleanly without truncating the existing schema
        JsonObject target;
        try
        {
            target = ResolveOrCreateObject(schema, TargetPath);
        }
        catch (InvalidOperationException ex)
        {
            Log.LogError("Invalid TargetPath '{0}' for JSON schema file '{1}': {2}", TargetPath, JsonSchemaFile, ex.Message);
            return false;
        }

        // Merge the references into the resolved target object
        MergeObjects(target, CreateReferences(References));

        // Truncate and (re)write the schema file
        fs.SetLength(0);
        fs.Position = 0;
        using (var writer = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = true }))
        {
            schema.WriteTo(writer);
        }

        return true;
    }

    private static JsonObject CreateReferences(ITaskItem[] references)
        => new JsonObject
        {
            ["allOf"] = new JsonArray(references
                .OrderBy(x => int.TryParse(x.GetMetadata("Weight"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var order) ? order : 0)
                .Select(x => (JsonNode)new JsonObject
                {
                    ["$ref"] = x.ItemSpec
                })
                .ToArray())
        };

    /// <summary>
    /// Resolves the object at <paramref name="path"/> within <paramref name="root"/>, creating intermediate objects as
    /// needed. Returns <paramref name="root"/> itself when <paramref name="path"/> is empty.
    /// </summary>
    private static JsonObject ResolveOrCreateObject(JsonObject root, string path)
    {
        JsonObject current = root;
        foreach (var name in ParsePropertyPath(path))
        {
            if (current.TryGetPropertyValue(name, out JsonNode? child))
            {
                current = child as JsonObject
                    ?? throw new InvalidOperationException($"Cannot resolve target path '{path}': segment '{name}' exists but is not a JSON object.");
            }
            else
            {
                var created = new JsonObject();
                current[name] = created;
                current = created;
            }
        }

        return current;
    }

    /// <summary>
    /// Parses a JSON path into its property name segments, supporting an optional leading <c>$</c>, dot notation and
    /// bracket notation (<c>['name']</c>). Array indices are rejected.
    /// </summary>
    private static IEnumerable<string> ParsePropertyPath(string path)
    {
        var segments = new List<string>();

        int i = 0;

        // Skip leading '$'
        if (i < path.Length && path[i] == '$')
        {
            i++;
        }

        while (i < path.Length)
        {
            if (path[i] == '[')
            {
                i++;

                int start = i;
                while (i < path.Length && path[i] != ']')
                {
                    i++;
                }

                var content = path.Substring(start, i - start).Trim('\'', '"');
                if (content.Length > 0)
                {
                    if (int.TryParse(content, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    {
                        throw new InvalidOperationException($"Array indices are not supported in target path '{path}'.");
                    }

                    segments.Add(content);
                }

                if (i < path.Length)
                {
                    i++; // Skip ']'
                }
            }
            else
            {
                if (path[i] == '.')
                {
                    i++;
                }

                int start = i;
                while (i < path.Length && path[i] != '.' && path[i] != '[')
                {
                    i++;
                }

                if (i > start)
                {
                    segments.Add(path.Substring(start, i - start));
                }
            }
        }

        return segments;
    }

    /// <summary>
    /// Merges <paramref name="source"/> into <paramref name="target"/> using union semantics for arrays.
    /// </summary>
    private static void MergeObjects(JsonObject target, JsonObject source)
    {
        foreach (var property in source.ToList())
        {
            if (target.TryGetPropertyValue(property.Key, out JsonNode? targetValue))
            {
                if (targetValue is JsonArray targetArray && property.Value is JsonArray sourceArray)
                {
                    // Union: add items from source that don't already exist in target
                    var existingItems = new HashSet<string>();
                    foreach (JsonNode? item in targetArray)
                    {
                        existingItems.Add(item?.ToJsonString() ?? "null");
                    }

                    foreach (JsonNode? item in sourceArray.ToList())
                    {
                        if (!existingItems.Contains(item?.ToJsonString() ?? "null"))
                        {
                            targetArray.Add(item?.DeepClone());
                        }
                    }
                }
                else if (targetValue is JsonObject targetObj && property.Value is JsonObject sourceObj)
                {
                    MergeObjects(targetObj, sourceObj);
                }
                else
                {
                    target[property.Key] = property.Value?.DeepClone();
                }
            }
            else
            {
                target[property.Key] = property.Value?.DeepClone();
            }
        }
    }
}
