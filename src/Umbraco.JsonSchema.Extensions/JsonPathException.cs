using System;

namespace Umbraco.JsonSchema.Extensions;

/// <summary>
/// The exception that is thrown when a JSON path is malformed or can't be used on a JSON document.
/// </summary>
internal sealed class JsonPathException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JsonPathException" /> class.
    /// </summary>
    /// <param name="path">The JSON path.</param>
    /// <param name="message">The message that describes the error.</param>
    public JsonPathException(string path, string message)
        : base(message)
        => Path = path;

    /// <summary>
    /// Gets the JSON path.
    /// </summary>
    /// <value>
    /// The JSON path.
    /// </value>
    public string Path { get; }
}
