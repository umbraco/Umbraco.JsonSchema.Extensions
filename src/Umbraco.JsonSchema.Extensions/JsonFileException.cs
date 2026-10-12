using System;

namespace Umbraco.JsonSchema.Extensions;

/// <summary>
/// The exception that is thrown when a JSON file can't be updated.
/// </summary>
internal sealed class JsonFileException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JsonFileException" /> class.
    /// </summary>
    /// <param name="filePath">The path to the JSON file.</param>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of this exception.</param>
    public JsonFileException(string filePath, string message, Exception? innerException = null)
        : base(message, innerException)
        => FilePath = filePath;

    /// <summary>
    /// Gets the path to the JSON file.
    /// </summary>
    /// <value>
    /// The path to the JSON file.
    /// </value>
    public string FilePath { get; }
}
