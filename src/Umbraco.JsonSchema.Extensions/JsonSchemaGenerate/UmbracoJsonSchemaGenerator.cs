using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;

namespace Umbraco.JsonSchema.Extensions;

/// <summary>
/// Generates JSON schemas using the System.Text.Json <see cref="JsonSchemaExporter" />, including XML documentation
/// comments as descriptions.
/// </summary>
internal sealed class UmbracoJsonSchemaGenerator
{
    private readonly bool _includeObsoleteProperties;
    private readonly Action<string>? _logMessage;

    /// <summary>
    /// Initializes a new instance of the <see cref="UmbracoJsonSchemaGenerator" /> class.
    /// </summary>
    /// <param name="includeObsoleteProperties">Whether to include properties marked with <see cref="ObsoleteAttribute" />.</param>
    /// <param name="logMessage">Logs a message, e.g. when an XML documentation file can't be read and is ignored.</param>
    public UmbracoJsonSchemaGenerator(bool includeObsoleteProperties = false, Action<string>? logMessage = null)
    {
        _includeObsoleteProperties = includeObsoleteProperties;
        _logMessage = logMessage;
    }

    /// <summary>
    /// Loads an assembly from the specified file path, resolves the type, generates the JSON schema,
    /// and unloads the assembly without locking the file.
    /// </summary>
    /// <param name="assemblyFilePath">The path to the assembly file.</param>
    /// <param name="typeName">The fully qualified type name to generate the schema for.</param>
    /// <returns>The generated JSON schema.</returns>
    public JsonObject Generate(string assemblyFilePath, string typeName)
    {
        using var loadContext = new PluginLoadContext(assemblyFilePath);
        Assembly assembly = loadContext.LoadFromAssemblyFile(assemblyFilePath);

        Type type = assembly.GetType(typeName, throwOnError: true)!;

        return Generate(type, new XmlDocumentation(loadContext.GetAssemblyPath, _logMessage));
    }

    private static void TransformSchemaNode(JsonSchemaExporterContext context, JsonObject schema, XmlDocumentation xmlDocumentation, JsonSerializerOptions options)
    {
        ICustomAttributeProvider? attributeProvider = context.PropertyInfo?.AttributeProvider;
        Type type = Nullable.GetUnderlyingType(context.TypeInfo.Type) ?? context.TypeInfo.Type;

        // Use the property description, falling back to the type description
        string? description = (attributeProvider is MemberInfo member ? GetDescription(member, xmlDocumentation) : null)
            ?? GetDescription(type, xmlDocumentation);
        if (description is not null && !schema.ContainsKey("description"))
        {
            schema.Insert(0, "description", description);
        }

        if (type == typeof(TimeSpan))
        {
            // The exporter only allows the constant ("c") format System.Text.Json uses, but configuration binding parses
            // other formats as well (e.g. 0:20:00 or 00:20), so don't report those as invalid
            schema.Remove("pattern");
        }

        if (type.IsEnum && type.IsDefined(typeof(FlagsAttribute), inherit: false) && schema["enum"] is null
            && GetEnumNames(type, options) is { Count: > 0 } names)
        {
            // Flags enums are serialized as comma-separated names, which the exporter only describes as a string: allow
            // the combinations using a pattern, while still listing the names for editor completion
            var namePattern = "(" + string.Join("|", names.Select(Regex.Escape)) + ")";

            schema["anyOf"] = new JsonArray(
                new JsonObject { ["enum"] = new JsonArray(names.Select(x => (JsonNode)x).ToArray()) },
                new JsonObject { ["pattern"] = $"^\\s*{namePattern}(\\s*,\\s*{namePattern})*\\s*$" });
        }

        // A null default value for a non-nullable value type means the value couldn't be converted (as the attribute ignores
        // conversion errors), so leave it out instead of reporting an invalid default
        if (attributeProvider?.GetCustomAttributes(typeof(DefaultValueAttribute), inherit: true) is [DefaultValueAttribute defaultValue, ..]
            && (defaultValue.Value is not null || !context.TypeInfo.Type.IsValueType || Nullable.GetUnderlyingType(context.TypeInfo.Type) is not null))
        {
            schema["default"] = defaultValue.Value is null ? null : JsonSerializer.SerializeToNode(defaultValue.Value, defaultValue.Value.GetType(), options);
        }

        if (attributeProvider?.GetCustomAttributes(typeof(ObsoleteAttribute), inherit: true) is [ObsoleteAttribute obsolete, ..])
        {
            schema["x-deprecated"] = true;
            if (!string.IsNullOrEmpty(obsolete.Message))
            {
                schema["x-deprecatedMessage"] = obsolete.Message;
            }
        }
    }

    private static List<string> GetEnumNames(Type enumType, JsonSerializerOptions options)
        => Enum.GetValues(enumType).Cast<object>()
            .Select(x => JsonSerializer.SerializeToNode(x, enumType, options) is JsonValue value && value.TryGetValue(out string? name) ? name : null)
            .OfType<string>()
            .Distinct()
            .ToList();

    private static string? GetDescription(MemberInfo member, XmlDocumentation xmlDocumentation)
        => xmlDocumentation.GetSummary(member)
            ?? (member.GetCustomAttributes(typeof(DescriptionAttribute), inherit: true) is [DescriptionAttribute { Description.Length: > 0 } description, ..] ? description.Description : null);

    private static JsonObject ToJsonObject(JsonNode schema)
        => schema switch
        {
            JsonObject jsonObject => jsonObject,

            // Boolean schemas (true accepts any value, false none) aren't supported by draft-04, so convert them to their
            // equivalent objects (which also allows adding keywords)
            _ when schema.GetValueKind() is JsonValueKind.False => new JsonObject { ["not"] = new JsonObject() },
            _ => new JsonObject(),
        };

    private JsonObject Generate(Type type, XmlDocumentation xmlDocumentation)
    {
        var options = new JsonSerializerOptions()
        {
            Converters = { new JsonStringEnumConverter() },
            IgnoreReadOnlyProperties = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
            {
                Modifiers = { RemoveIgnoredProperties },
            },
        };

        var exporterOptions = new JsonSchemaExporterOptions()
        {
            TransformSchemaNode = (context, schema) =>
            {
                JsonObject schemaObject = ToJsonObject(schema);
                TransformSchemaNode(context, schemaObject, xmlDocumentation, options);

                return schemaObject;
            },
        };

        var schema = ToJsonObject(options.GetJsonSchemaAsNode(type, exporterOptions));

        // The root object itself can't be null
        if (schema["type"] is JsonArray types && types.Count == 2 && types.Any(x => x?.GetValue<string>() == "null"))
        {
            schema["type"] = types.First(x => x?.GetValue<string>() != "null")!.GetValue<string>();
        }

        schema.Insert(0, "$schema", JsonSchemaDraft.Uri);

        return schema;
    }

    private void RemoveIgnoredProperties(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        for (int i = typeInfo.Properties.Count - 1; i >= 0; i--)
        {
            ICustomAttributeProvider? attributeProvider = typeInfo.Properties[i].AttributeProvider;

            // Remove properties without a public setter (read-only or non-public, e.g. internal), as these aren't
            // (de)serialized by System.Text.Json and read-only removal isn't implemented by the exporter
            var isReadOnly = typeInfo.Options.IgnoreReadOnlyProperties && attributeProvider is PropertyInfo property && property.SetMethod?.IsPublic is not true;
            var isObsolete = !_includeObsoleteProperties && attributeProvider?.IsDefined(typeof(ObsoleteAttribute), inherit: true) is true;
            if (isReadOnly || isObsolete)
            {
                typeInfo.Properties.RemoveAt(i);
            }
        }
    }
}
