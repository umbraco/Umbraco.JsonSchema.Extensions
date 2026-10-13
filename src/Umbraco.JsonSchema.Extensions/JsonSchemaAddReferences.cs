using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    /// Gets or sets the JSON path to the object the references are added to.
    /// </summary>
    /// <value>
    /// The JSON path (e.g. <c>$.properties.extensions.items</c>) to the object the references are added to. Defaults to
    /// the root of the schema. Intermediate objects are created when they do not exist.
    /// </value>
    /// <remarks>
    /// Only object property segments are supported (dot and quoted bracket notation, with an optional leading
    /// <c>$</c>); array indices are not. This allows composing references into a nested location (such as a single
    /// property) instead of constraining the whole schema.
    /// </remarks>
    public string TargetPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the JSON Schema keyword the references are combined under.
    /// </summary>
    /// <value>
    /// The combining keyword: <c>allOf</c> (default), <c>anyOf</c> or <c>oneOf</c>. <c>allOf</c> requires every
    /// referenced schema to match (intersection); <c>anyOf</c>/<c>oneOf</c> require any/exactly one to match (union).
    /// </value>
    public string Combinator { get; set; } = "allOf";

    /// <inheritdoc />
    public override bool Execute()
    {
        if (References.Length == 0)
        {
            // No references to add
            return true;
        }

        if (Combinator is not ("allOf" or "anyOf" or "oneOf"))
        {
            Log.LogError("Invalid Combinator '{0}' for JSON schema file '{1}': expected 'allOf', 'anyOf' or 'oneOf'.", Combinator, JsonSchemaFile);
            return false;
        }

        // Parse the target path (only allowing property names) before opening the JSON schema file, so an invalid path fails
        // the build without touching the file
        JsonPath targetPath;
        try
        {
            targetPath = JsonPath.Parse(TargetPath, allowArrayIndices: false);
        }
        catch (JsonPathException ex)
        {
            Log.LogError("Invalid TargetPath '{0}' for JSON schema file '{1}': {2}", TargetPath, JsonSchemaFile, ex.Message);
            return false;
        }

        JsonObject references = CreateReferences(References, Combinator);
        var newSchema = new JsonObject
        {
            ["$schema"] = JsonSchemaDraft.Uri
        };

        try
        {
            // Merge the references into the object at the target path (the file is only written when references are added)
            JsonFileEditor.Update(JsonSchemaFile, schema => MergeObjects(targetPath.GetOrCreateObject(schema), references), newSchema);
        }
        catch (JsonFileException ex)
        {
            Log.LogError("Could not update JSON schema file '{0}': {1}", ex.FilePath, ex.Message);
            return false;
        }
        catch (JsonPathException ex)
        {
            // The target path addresses an existing value that isn't an object
            Log.LogError("Invalid TargetPath '{0}' for JSON schema file '{1}': {2}", TargetPath, JsonSchemaFile, ex.Message);
            return false;
        }

        return true;
    }

    private static JsonObject CreateReferences(ITaskItem[] references, string combinator)
        => new JsonObject
        {
            [combinator] = new JsonArray(references
                .OrderBy(x => int.TryParse(x.GetMetadata("Weight"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var order) ? order : 0)
                .Select(x => (JsonNode)new JsonObject
                {
                    ["$ref"] = x.ItemSpec
                })
                .ToArray())
        };

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
