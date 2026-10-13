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
        // Parse the path and value before opening the JSON file, so invalid input fails the build without touching the file
        JsonPath path;
        try
        {
            path = JsonPath.Parse(Path);
        }
        catch (JsonPathException ex)
        {
            Log.LogError("Invalid Path '{0}' for JSON file '{1}': {2}", Path, JsonFile, ex.Message);
            return false;
        }

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

        try
        {
            // Replace the existing value (the file is only written when the value changes)
            JsonFileEditor.Update<JsonNode>(JsonFile, json => path.TryReplace(json, value));
        }
        catch (JsonFileException ex)
        {
            Log.LogError("Could not update JSON file '{0}': {1}", ex.FilePath, ex.Message);
            return false;
        }

        return true;
    }
}
