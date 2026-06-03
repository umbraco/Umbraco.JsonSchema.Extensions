using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Namotion.Reflection;
using NJsonSchema.Generation;

namespace Umbraco.JsonSchema.Extensions;

/// <inheritdoc />
internal sealed class UmbracoJsonSchemaGenerator : JsonSchemaGenerator
{
    private static readonly object _xmlDocsCacheLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="UmbracoJsonSchemaGenerator" /> class.
    /// </summary>
    /// <param name="includeObsoleteProperties">Whether to include properties marked with <see cref="ObsoleteAttribute" />.</param>
    public UmbracoJsonSchemaGenerator(bool includeObsoleteProperties = false)
        : base(new SystemTextJsonSchemaGeneratorSettings()
        {
            AlwaysAllowAdditionalObjectProperties = true,
            FlattenInheritanceHierarchy = true,
            IgnoreObsoleteProperties = !includeObsoleteProperties,
            ReflectionService = new UmbracoSystemTextJsonReflectionService(),
            SerializerOptions = new JsonSerializerOptions()
            {
                Converters = { new JsonStringEnumConverter() },
                IgnoreReadOnlyProperties = true,
            },
        })
    { }

    /// <summary>
    /// Loads an assembly from the specified file path, resolves the type, generates the JSON schema,
    /// and unloads the assembly without locking the file.
    /// </summary>
    /// <param name="assemblyFilePath">The path to the assembly file.</param>
    /// <param name="typeName">The fully qualified type name to generate the schema for.</param>
    /// <returns>The generated JSON schema.</returns>
    public NJsonSchema.JsonSchema Generate(string assemblyFilePath, string typeName)
    {
        using var loadContext = new PluginLoadContext(assemblyFilePath);
        Assembly assembly = loadContext.LoadFromAssemblyFile(assemblyFilePath);

        Type type = assembly.GetType(typeName, throwOnError: true)!;

        // Generate the schema once to trigger loading all referenced assemblies.
        // Descriptions will be missing because Namotion.Reflection can't find XML docs
        // for stream-loaded assemblies (empty Assembly.Location).
        Generate(type);

        // The first pass cached null entries for our stream-loaded assemblies.
        // We must clear those stale entries before priming with the correct XML docs paths.
        // Namotion.Reflection only exposes a global ClearCache() (no per-assembly overload),
        // so we hold a lock to prevent concurrent tasks from losing their primed entries.
        lock (_xmlDocsCacheLock)
        {
            XmlDocs.ClearCache();
            loadContext.PrimeXmlDocsCache();

            // Generate again — now all XML docs are cached and descriptions will be included.
            return Generate(type);
        }
    }

    /// <inheritdoc />
    private sealed class UmbracoSystemTextJsonReflectionService : SystemTextJsonReflectionService
    {
        /// <inheritdoc />
        public override void GenerateProperties(global::NJsonSchema.JsonSchema schema, ContextualType contextualType, SystemTextJsonSchemaGeneratorSettings settings, JsonSchemaGenerator schemaGenerator, JsonSchemaResolver schemaResolver)
        {
            // Populate schema properties
            base.GenerateProperties(schema, contextualType, settings, schemaGenerator, schemaResolver);

            if (settings.SerializerOptions.IgnoreReadOnlyProperties)
            {
                // Remove properties without a public setter (read-only or non-public, e.g. internal), as these
                // aren't (de)serialized by System.Text.Json and read-only removal isn't implemented by the base class
                foreach (ContextualPropertyInfo property in contextualType.Properties)
                {
                    if (property.PropertyInfo.SetMethod?.IsPublic is not true)
                    {
                        string propertyName = GetPropertyName(property, settings);

                        schema.Properties.Remove(propertyName);
                    }
                }
            }
        }
    }
}
