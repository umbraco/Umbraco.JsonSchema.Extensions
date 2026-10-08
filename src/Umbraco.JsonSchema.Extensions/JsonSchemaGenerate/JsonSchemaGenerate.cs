using System.IO;
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
        try
        {
            var assemblyFile = Path.GetFullPath(AssemblyPath);
            if (!File.Exists(assemblyFile))
            {
                Log.LogError("Assembly file not found: {0}", assemblyFile);

                return false;
            }

            var generator = new UmbracoJsonSchemaGenerator(IncludeObsoleteProperties);
            global::NJsonSchema.JsonSchema schema = generator.Generate(assemblyFile, TypeName);

            var outputFile = Path.GetFullPath(OutputPath);
            var outputDir = Path.GetDirectoryName(outputFile);
            if (outputDir is not null)
            {
                Directory.CreateDirectory(outputDir);
            }

            File.WriteAllText(outputFile, schema.ToJson());
            Log.LogMessage(MessageImportance.High, "Generated JSON schema: {0}", outputFile);

            return true;
        }
        catch (System.Exception ex)
        {
            Log.LogErrorFromException(ex);

            return false;
        }
#endif
    }
}
