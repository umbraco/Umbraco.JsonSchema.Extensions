using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Build.Framework;

namespace Umbraco.JsonSchema.Extensions
{
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
        public string JsonSchemaFile { get; set; } = null!;

        /// <summary>
        /// Gets or sets the references to add.
        /// </summary>
        /// <value>
        /// The references to add.
        /// </value>
        [Required]
        public ITaskItem[] References { get; set; } = Array.Empty<ITaskItem>();

        /// <inheritdoc />
        public override bool Execute()
        {
            if (References.Length == 0)
            {
                // No references to add
                return true;
            }

            using (FileStream fs = File.Open(JsonSchemaFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
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

                    // Truncate file
                    fs.SetLength(0);
                    fs.Position = 0;
                }

                // Merge schema with references
                MergeObjects(schema, CreateReferences(References));

                // Write schema file
                using (var writer = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = true }))
                {
                    schema.WriteTo(writer);
                }
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
}
