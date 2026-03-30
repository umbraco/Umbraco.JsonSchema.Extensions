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

        // Prime XML docs cache for all loaded assemblies and clear the stale cache entries
        // that Namotion.Reflection created with null values during the first pass.
        XmlDocs.ClearCache();
        loadContext.PrimeXmlDocsCache();

        // Generate again — now all XML docs are cached and descriptions will be included.
        return Generate(type);
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
                // Remove read-only properties (because this is not implemented by the base class)
                foreach (ContextualPropertyInfo property in contextualType.Properties)
                {
                    if (property.CanWrite is false)
                    {
                        string propertyName = GetPropertyName(property, settings);

                        schema.Properties.Remove(propertyName);
                    }
                }
            }
        }
    }
}
