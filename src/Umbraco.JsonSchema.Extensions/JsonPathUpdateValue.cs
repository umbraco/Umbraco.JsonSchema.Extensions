using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Build.Framework;

namespace Umbraco.JsonSchema.Extensions;

/// <summary>
/// Updates the value of a property in a JSON file using a JSON path expression.
/// </summary>
/// <seealso cref="Microsoft.Build.Utilities.Task" />
public class JsonPathUpdateValue : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// Gets or sets the JSON file.
    /// </summary>
    /// <value>
    /// The JSON file.
    /// </value>
    [Required]
    public string JsonFile { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the JSON path expression that specifies the property to update.
    /// </summary>
    /// <value>
    /// The JSON path expression that specifies the property to update.
    /// </value>
    [Required]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the JSON value to set.
    /// </summary>
    /// <value>
    /// The JSON value to set.
    /// </value>
    [Required]
    public string Value { get; set; } = string.Empty;

    /// <inheritdoc />
    public override bool Execute()
    {
        // Parse the value before opening the JSON file, so an invalid value fails the build without touching the file
        JsonNode? value;
        try
        {
            value = JsonNode.Parse(Value);
        }
        catch (JsonException ex)
        {
            Log.LogError("Invalid Value '{0}' for JSON file '{1}': {2}", Value, JsonFile, ex.Message);
            return false;
        }

        List<PathSegment> segments = ParsePath(Path);

        try
        {
            // Replace the existing value (the file is only written when the value changes)
            JsonFileEditor.Update<JsonNode>(JsonFile, json => Replace(json, segments, value));
        }
        catch (JsonFileException ex)
        {
            Log.LogError("Could not update JSON file '{0}': {1}", ex.FilePath, ex.Message);
            return false;
        }

        return true;
    }

    private static void Replace(JsonNode json, List<PathSegment> segments, JsonNode? value)
    {
        if (segments.Count == 0)
        {
            return;
        }

        // Navigate to parent of the target token
        JsonNode? parent = json;
        for (int i = 0; i < segments.Count - 1; i++)
        {
            parent = Navigate(parent, segments[i]);
            if (parent is null)
            {
                return;
            }
        }

        // Replace value at the last segment (only when it already exists)
        PathSegment lastSegment = segments[segments.Count - 1];
        if (lastSegment.IsArrayIndex && parent is JsonArray arr)
        {
            if (lastSegment.ArrayIndex < arr.Count)
            {
                arr[lastSegment.ArrayIndex] = value;
            }
        }
        else if (lastSegment.PropertyName is not null && parent is JsonObject obj)
        {
            if (obj.ContainsKey(lastSegment.PropertyName))
            {
                obj[lastSegment.PropertyName] = value;
            }
        }
    }

    private static JsonNode? Navigate(JsonNode? node, PathSegment segment)
    {
        if (node is null)
        {
            return null;
        }

        if (segment.IsArrayIndex && node is JsonArray arr)
        {
            return segment.ArrayIndex < arr.Count ? arr[segment.ArrayIndex] : null;
        }

        if (segment.PropertyName is not null && node is JsonObject obj)
        {
            return obj.TryGetPropertyValue(segment.PropertyName, out JsonNode? child) ? child : null;
        }

        return null;
    }

    private static List<PathSegment> ParsePath(string path)
    {
        var segments = new List<PathSegment>();

        int i = 0;

        // Skip leading '$'
        if (i < path.Length && path[i] == '$')
        {
            i++;
        }

        while (i < path.Length)
        {
            if (path[i] == '.')
            {
                i++;

                int start = i;
                while (i < path.Length && path[i] != '.' && path[i] != '[')
                {
                    i++;
                }

                if (i > start)
                {
                    segments.Add(PathSegment.Property(path.Substring(start, i - start)));
                }
            }
            else if (path[i] == '[')
            {
                i++;

                int start = i;
                while (i < path.Length && path[i] != ']')
                {
                    i++;
                }

                if (i > start)
                {
                    string content = path.Substring(start, i - start).Trim('\'', '"');
                    if (int.TryParse(content, out int index))
                    {
                        segments.Add(PathSegment.Index(index));
                    }
                    else
                    {
                        segments.Add(PathSegment.Property(content));
                    }
                }

                if (i < path.Length)
                {
                    i++; // skip ']'
                }
            }
            else
            {
                int start = i;
                while (i < path.Length && path[i] != '.' && path[i] != '[')
                {
                    i++;
                }

                if (i > start)
                {
                    segments.Add(PathSegment.Property(path.Substring(start, i - start)));
                }
            }
        }

        return segments;
    }

    private readonly struct PathSegment
    {
        public string? PropertyName { get; }

        public int ArrayIndex { get; }

        public bool IsArrayIndex { get; }

        private PathSegment(string? propertyName, int arrayIndex, bool isArrayIndex)
        {
            PropertyName = propertyName;
            ArrayIndex = arrayIndex;
            IsArrayIndex = isArrayIndex;
        }

        public static PathSegment Property(string name) => new PathSegment(name, 0, false);

        public static PathSegment Index(int index) => new PathSegment(null, index, true);
    }
}
