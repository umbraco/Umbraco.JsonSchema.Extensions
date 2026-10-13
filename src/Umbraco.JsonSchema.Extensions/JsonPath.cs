using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Umbraco.JsonSchema.Extensions;

/// <summary>
/// A parsed JSON path that addresses a value inside a JSON document.
/// </summary>
/// <remarks>
/// A JSON path consists of an optional leading <c>$</c>, followed by property names in dot notation (<c>.name</c>) or
/// quoted bracket notation (<c>['name']</c> or <c>["name"]</c>) and array indices in unquoted bracket notation
/// (<c>[0]</c>). A quoted segment is always a property name, even when it looks like a number. The first property name
/// may omit the leading dot (e.g. <c>properties.extensions</c>). An empty path or <c>$</c> addresses the root.
/// </remarks>
internal sealed class JsonPath
{
    private readonly string _path;
    private readonly Segment[] _segments;

    private JsonPath(string path, Segment[] segments)
    {
        _path = path;
        _segments = segments;
    }

    /// <summary>
    /// Parses the specified JSON path.
    /// </summary>
    /// <param name="path">The JSON path.</param>
    /// <param name="allowArrayIndices">Whether array indices are allowed (e.g. not when only property names can be used).</param>
    /// <returns>
    /// The parsed JSON path.
    /// </returns>
    /// <exception cref="JsonPathException">The JSON path is malformed or contains an array index that isn't allowed.</exception>
    public static JsonPath Parse(string path, bool allowArrayIndices = true)
    {
        var segments = new List<Segment>();

        int i = 0;
        if (path.Length > 0 && path[0] == '$')
        {
            i++;
        }
        else if (path.Length > 0 && path[0] is not ('.' or '['))
        {
            i = ReadPropertyName(path, i, segments);
        }

        while (i < path.Length)
        {
            i = path[i] switch
            {
                '.' => ReadPropertyName(path, i + 1, segments),
                '[' => ReadBracket(path, i + 1, segments),
                _ => throw new JsonPathException(path, $"Expected '.' or '[' at index {i}.")
            };
        }

        if (!allowArrayIndices && segments.Exists(x => x.PropertyName is null))
        {
            throw new JsonPathException(path, "Array indices are not supported.");
        }

        return new JsonPath(path, segments.ToArray());
    }

    /// <summary>
    /// Replaces the existing value this path addresses within <paramref name="root" />.
    /// </summary>
    /// <param name="root">The JSON root.</param>
    /// <param name="value">The value to replace the existing value with.</param>
    /// <returns>
    /// <see langword="true" /> when an existing value was replaced; otherwise, <see langword="false" /> (when this path
    /// doesn't address an existing value or addresses the root itself).
    /// </returns>
    public bool TryReplace(JsonNode root, JsonNode? value)
    {
        if (_segments.Length == 0)
        {
            return false;
        }

        // Navigate to the parent of the value
        JsonNode? parent = root;
        for (int i = 0; i < _segments.Length - 1 && parent is not null; i++)
        {
            parent = GetChild(parent, _segments[i]);
        }

        // Replace the value (only when it already exists)
        Segment last = _segments[_segments.Length - 1];
        if (last.PropertyName is null)
        {
            if (parent is JsonArray array && last.ArrayIndex < array.Count)
            {
                array[last.ArrayIndex] = value;
                return true;
            }
        }
        else if (parent is JsonObject obj && obj.ContainsKey(last.PropertyName))
        {
            obj[last.PropertyName] = value;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the object this path addresses within <paramref name="root" />, creating intermediate objects as needed.
    /// </summary>
    /// <param name="root">The JSON root.</param>
    /// <returns>
    /// The object this path addresses, or <paramref name="root" /> itself when this path addresses the root.
    /// </returns>
    /// <exception cref="JsonPathException">This path contains an array index or addresses an existing value that is not a JSON object.</exception>
    public JsonObject GetOrCreateObject(JsonObject root)
    {
        JsonObject current = root;
        foreach (Segment segment in _segments)
        {
            if (segment.PropertyName is null)
            {
                throw new JsonPathException(_path, "Array indices are not supported.");
            }

            if (current.TryGetPropertyValue(segment.PropertyName, out JsonNode? child))
            {
                current = child as JsonObject
                    ?? throw new JsonPathException(_path, $"Segment '{segment.PropertyName}' exists but is not a JSON object.");
            }
            else
            {
                var created = new JsonObject();
                current[segment.PropertyName] = created;
                current = created;
            }
        }

        return current;
    }

    /// <inheritdoc />
    public override string ToString() => _path;

    private static int ReadPropertyName(string path, int start, List<Segment> segments)
    {
        int end = start;
        while (end < path.Length && path[end] is not ('.' or '[' or ']'))
        {
            end++;
        }

        if (end == start)
        {
            throw new JsonPathException(path, $"Expected a property name at index {start}.");
        }

        segments.Add(Segment.Property(path.Substring(start, end - start)));

        return end;
    }

    private static int ReadBracket(string path, int start, List<Segment> segments)
    {
        // Quoted property name
        if (start < path.Length && path[start] is ('\'' or '"'))
        {
            char quote = path[start];
            int close = path.IndexOf(quote, start + 1);
            if (close < 0 || close + 1 >= path.Length || path[close + 1] != ']')
            {
                throw new JsonPathException(path, $"Expected {quote}] to close the property name at index {start}.");
            }

            segments.Add(Segment.Property(path.Substring(start + 1, close - start - 1)));

            return close + 2;
        }

        // Array index
        int end = path.IndexOf(']', start);
        if (end < 0)
        {
            throw new JsonPathException(path, $"Expected ] to close the bracket at index {start - 1}.");
        }

        if (!int.TryParse(path.Substring(start, end - start), NumberStyles.None, CultureInfo.InvariantCulture, out int index))
        {
            throw new JsonPathException(path, $"Expected a non-negative array index or a quoted property name at index {start}.");
        }

        segments.Add(Segment.Index(index));

        return end + 1;
    }

    private static JsonNode? GetChild(JsonNode node, Segment segment)
        => segment.PropertyName is null
            ? node is JsonArray array && segment.ArrayIndex < array.Count ? array[segment.ArrayIndex] : null
            : node is JsonObject obj && obj.TryGetPropertyValue(segment.PropertyName, out JsonNode? child) ? child : null;

    private readonly struct Segment
    {
        private Segment(string? propertyName, int arrayIndex)
        {
            PropertyName = propertyName;
            ArrayIndex = arrayIndex;
        }

        /// <summary>
        /// Gets the property name, or <see langword="null" /> when this segment is an array index.
        /// </summary>
        public string? PropertyName { get; }

        public int ArrayIndex { get; }

        public static Segment Property(string name) => new(name, 0);

        public static Segment Index(int index) => new(null, index);
    }
}
