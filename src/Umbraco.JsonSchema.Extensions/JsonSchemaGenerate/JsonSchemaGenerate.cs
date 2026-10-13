using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Microsoft.Build.Framework;

namespace Umbraco.JsonSchema.Extensions;

/// <summary>
/// MSBuild task that generates a JSON schema from a C# type in an assembly.
/// </summary>
public sealed class JsonSchemaGenerate : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// Gets or sets the path to the assembly file containing the type.
    /// </summary>
    [Required]
    public string AssemblyPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the fully qualified type name to generate the schema for.
    /// </summary>
    [Required]
    public string TypeName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the output file path for the generated JSON schema.
    /// </summary>
    [Required]
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether to include properties marked with <see cref="T:System.ObsoleteAttribute" />.
    /// </summary>
    public bool IncludeObsoleteProperties { get; set; }

    /// <inheritdoc />
    public override bool Execute()
    {
#if NETSTANDARD2_0
        // Only registered on MSBuild versions that can't run the net10.0 task, so fail with an actionable error instead of an assembly load failure
        Log.LogError("The JsonSchemaGenerate task requires .NET 10: build using the .NET 10 SDK or later (dotnet build) or Visual Studio 2026+ (MSBuild 18.0+).");

        return false;
#else
        var assemblyFile = Path.GetFullPath(AssemblyPath);
        if (!File.Exists(assemblyFile))
        {
            Log.LogError("Assembly file not found: {0}", assemblyFile);

            return false;
        }

        JsonObject schema;
        try
        {
            // Generate the schema while the assembly is loaded (types can't be used after it's unloaded)
            using var assembly = new IsolatedAssembly(assemblyFile, message => Log.LogMessage(MessageImportance.Low, "{0}", message));
            if (!assembly.TryGetType(TypeName, out Type? type))
            {
                Log.LogError("Type '{0}' not found in assembly '{1}'.", TypeName, assemblyFile);

                return false;
            }

            schema = JsonSchemaGenerator.Generate(type, assembly.GetSummary, IncludeObsoleteProperties);
        }
        catch (Exception ex)
        {
            // Include the messages of inner exceptions, as these are often the most relevant (e.g. for type initialization or invocation errors)
            Log.LogError("Could not generate JSON schema for type '{0}' from assembly '{1}': {2}", TypeName, assemblyFile, GetMessages(ex));

            return false;
        }

        // Always write the output (even when unchanged), as its timestamp lets MSBuild skip generation when the assembly
        // hasn't changed since the last generation (incremental builds using Inputs/Outputs)
        var outputFile = Path.GetFullPath(OutputPath);
        try
        {
            JsonFileEditor.Write(outputFile, schema);
        }
        catch (JsonFileException ex)
        {
            Log.LogError("Could not write JSON schema file '{0}': {1}", ex.FilePath, ex.Message);

            return false;
        }

        Log.LogMessage(MessageImportance.High, "Generated JSON schema: {0}", outputFile);

        return true;
#endif
    }

#if !NETSTANDARD2_0
    private static string GetMessages(Exception exception)
    {
        var messages = new List<string>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" ---> ", messages);
    }
#endif
}
