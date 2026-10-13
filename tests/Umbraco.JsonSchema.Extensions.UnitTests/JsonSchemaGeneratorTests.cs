using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Umbraco.JsonSchema.Extensions.UnitTests;

/// <summary>
/// Tests for <see cref="JsonSchemaGenerator" />.
/// </summary>
[TestFixture]
public class JsonSchemaGeneratorTests
{
    /// <summary>
    /// The schema of an object declares the draft, includes its writable properties and allows additional properties.
    /// </summary>
    [Test]
    public void Generate_Object_IncludesWritablePropertiesAndDraft()
    {
        JsonObject schema = Generate<ObjectModel>();

        Assert.That(schema["$schema"]?.GetValue<string>(), Is.EqualTo("http://json-schema.org/draft-04/schema#"));
        Assert.That(schema["type"]?.GetValue<string>(), Is.EqualTo("object"));
        Assert.That(GetProperties(schema).Select(x => x.Key), Is.EqualTo(new[] { "Name", "Age" }));
        Assert.That(schema.ContainsKey("additionalProperties"), Is.False);
    }

    /// <summary>
    /// Properties without a public setter (read-only, computed or non-public) are excluded, matching System.Text.Json.
    /// </summary>
    [Test]
    public void Generate_PropertiesWithoutPublicSetter_AreExcluded()
    {
        JsonObject schema = Generate<SetterModel>();

        Assert.That(GetProperties(schema).Select(x => x.Key), Is.EqualTo(new[] { "Writable" }));
    }

    /// <summary>
    /// Obsolete properties are excluded by default.
    /// </summary>
    [Test]
    public void Generate_ObsoleteProperties_AreExcludedByDefault()
    {
        JsonObject schema = Generate<ObsoleteModel>();

        Assert.That(GetProperties(schema).Select(x => x.Key), Is.EqualTo(new[] { "Current" }));
    }

    /// <summary>
    /// Obsolete properties are marked as deprecated (with the obsolete message, if any) when included.
    /// </summary>
    [Test]
    public void Generate_ObsoleteProperties_AreMarkedAsDeprecated_WhenIncluded()
    {
        JsonObject properties = GetProperties(Generate<ObsoleteModel>(includeObsoleteProperties: true));

        Assert.That(properties["WithMessage"]!["x-deprecated"]?.GetValue<bool>(), Is.True);
        Assert.That(properties["WithMessage"]!["x-deprecatedMessage"]?.GetValue<string>(), Is.EqualTo("Use Current instead."));
        Assert.That(properties["WithoutMessage"]!["x-deprecated"]?.GetValue<bool>(), Is.True);
        Assert.That(properties["WithoutMessage"]!.AsObject().ContainsKey("x-deprecatedMessage"), Is.False);
        Assert.That(properties["Current"]!.AsObject().ContainsKey("x-deprecated"), Is.False);
    }

    /// <summary>
    /// Enums use their string values.
    /// </summary>
    [Test]
    public void Generate_Enum_UsesStringValues()
    {
        JsonObject schema = GetProperties(Generate<EnumModel>())["Status"]!.AsObject();

        Assert.That(schema["enum"]!.AsArray().Select(x => x?.GetValue<string>()), Is.EqualTo(new[] { "Active", "Inactive", "Pending" }));
    }

    /// <summary>
    /// Flags enums list their names, while allowing comma-separated combinations.
    /// </summary>
    [Test]
    public void Generate_FlagsEnum_AllowsCombinations()
    {
        JsonObject schema = GetProperties(Generate<EnumModel>())["Flags"]!.AsObject();
        JsonArray anyOf = schema["anyOf"]!.AsArray();

        Assert.That(schema["type"]?.GetValue<string>(), Is.EqualTo("string"));
        Assert.That(anyOf.Single(x => x!["enum"] is not null)!["enum"]!.AsArray().Select(x => x?.GetValue<string>()), Is.EqualTo(new[] { "None", "Read", "Write", "All" }));

        var pattern = new Regex(anyOf.Single(x => x!["pattern"] is not null)!["pattern"]!.GetValue<string>());
        Assert.That(pattern.IsMatch("Read"), Is.True);
        Assert.That(pattern.IsMatch("Read, Write"), Is.True);
        Assert.That(pattern.IsMatch("Read,Write"), Is.True);
        Assert.That(pattern.IsMatch("Read, Unknown"), Is.False);
    }

    /// <summary>
    /// Time spans allow any string, as configuration binding parses other formats than the constant format as well.
    /// </summary>
    [Test]
    public void Generate_TimeSpan_AllowsAnyString()
    {
        JsonObject schema = GetProperties(Generate<TimeSpanModel>())["Timeout"]!.AsObject();

        Assert.That(schema["type"]?.GetValue<string>(), Is.EqualTo("string"));
        Assert.That(schema.ContainsKey("pattern"), Is.False);
    }

    /// <summary>
    /// Nullable time spans and flags enums get the same adjustments, while allowing <c>null</c>.
    /// </summary>
    [Test]
    public void Generate_NullableProperties_UseUnderlyingTypeAdjustments()
    {
        JsonObject properties = GetProperties(Generate<NullableModel>());

        JsonObject timeout = properties["Timeout"]!.AsObject();
        Assert.That(timeout["type"]!.ToJsonString(), Does.Contain("null"));
        Assert.That(timeout.ContainsKey("pattern"), Is.False);

        JsonObject flags = properties["Flags"]!.AsObject();
        Assert.That(flags["type"]!.ToJsonString(), Does.Contain("null"));
        Assert.That(flags.ContainsKey("anyOf"), Is.True);
    }

    /// <summary>
    /// Nested types are included inline.
    /// </summary>
    [Test]
    public void Generate_NestedType_IsIncludedInline()
    {
        JsonObject address = GetProperties(Generate<NestedModel>())["Address"]!.AsObject();

        Assert.That(GetProperties(address).Select(x => x.Key), Is.EqualTo(new[] { "Street", "City" }));
    }

    /// <summary>
    /// Recursive types reference the enclosing schema of the same type instead of expanding indefinitely.
    /// </summary>
    [Test]
    public void Generate_RecursiveType_ReferencesEnclosingSchema()
    {
        JsonObject schema = Generate<RecursiveModel>();
        JsonObject child = GetProperties(GetProperties(GetProperties(schema)["Tree"]!.AsObject())["Child"]!.AsObject())["Child"]!.AsObject();

        JsonObject referenced = Resolve(schema, child["$ref"]!.GetValue<string>());
        Assert.That(GetProperties(referenced).Select(x => x.Key), Is.EqualTo(new[] { "Name", "Child" }));
    }

    /// <summary>
    /// The property names and ignores from System.Text.Json attributes are used.
    /// </summary>
    [Test]
    public void Generate_UsesJsonAttributes()
    {
        JsonObject schema = Generate<JsonAttributeModel>();

        Assert.That(GetProperties(schema).Select(x => x.Key), Is.EqualTo(new[] { "renamed" }));
    }

    /// <summary>
    /// The value of <see cref="DefaultValueAttribute" /> is used as default, leaving out values that couldn't be converted
    /// to a non-nullable value type.
    /// </summary>
    [Test]
    public void Generate_DefaultValues_AreIncluded()
    {
        JsonObject properties = GetProperties(Generate<DefaultValueModel>());

        Assert.That(properties["Age"]!["default"]?.GetValue<int>(), Is.EqualTo(18));
        Assert.That(properties["Status"]!["default"]?.GetValue<string>(), Is.EqualTo("Pending"));
        Assert.That(properties["Timeout"]!["default"]?.GetValue<string>(), Is.EqualTo("00:20:00"));
        Assert.That(properties["Null"]!.AsObject().ContainsKey("default"), Is.True);
        Assert.That(properties["Null"]!["default"], Is.Null);
        Assert.That(properties["Unconvertible"]!.AsObject().ContainsKey("default"), Is.False);
    }

    /// <summary>
    /// Only properties marked with <see cref="RequiredAttribute" /> are required, not those using the <c>required</c>
    /// modifier or <see cref="JsonRequiredAttribute" />.
    /// </summary>
    [Test]
    public void Generate_RequiredProperties_OnlyUseRequiredAttribute()
    {
        JsonObject schema = Generate<RequiredModel>();

        Assert.That(schema["required"]!.AsArray().Select(x => x?.GetValue<string>()), Is.EqualTo(new[] { "Attribute", "ModifierAndAttribute" }));
    }

    /// <summary>
    /// Boolean schemas (e.g. <c>true</c> for any value) are converted to objects, as draft-04 doesn't support them.
    /// </summary>
    [Test]
    public void Generate_BooleanSchemas_AreConvertedToObjects()
    {
        JsonNode? anything = GetProperties(Generate<AnyModel>())["Anything"];

        Assert.That(anything, Is.InstanceOf<JsonObject>());
        Assert.That(anything!.AsObject(), Is.Empty);
    }

    /// <summary>
    /// Descriptions use the summary or else the <see cref="System.ComponentModel.DescriptionAttribute" /> of the property,
    /// falling back to those of its type.
    /// </summary>
    /// <param name="propertyName">The property name.</param>
    /// <param name="expected">The expected description.</param>
    [TestCase(nameof(DescribedModel.DocumentedProperty), "DocumentedProperty summary")]
    [TestCase(nameof(DescribedModel.AttributeProperty), "Property attribute description")]
    [TestCase(nameof(DescribedModel.DocumentedAttributeProperty), "DocumentedAttributeProperty summary")]
    [TestCase(nameof(DescribedModel.TypeDocumentedProperty), "DocumentedType summary")]
    [TestCase(nameof(DescribedModel.TypeAttributeProperty), "Type attribute description")]
    [TestCase(nameof(DescribedModel.UndescribedProperty), null)]
    public void Generate_Descriptions_FollowFallbackOrder(string propertyName, string? expected)
    {
        JsonObject properties = GetProperties(Generate<DescribedModel>(GetDocumentedSummary));

        Assert.That(properties[propertyName]!["description"]?.GetValue<string>(), Is.EqualTo(expected));
    }

    /// <summary>
    /// The summary of the root type is used as its description.
    /// </summary>
    [Test]
    public void Generate_RootType_UsesSummaryAsDescription()
    {
        JsonObject schema = Generate<DocumentedType>(GetDocumentedSummary);

        Assert.That(schema["description"]?.GetValue<string>(), Is.EqualTo("DocumentedType summary"));
    }

    private static JsonObject Generate<T>(Func<MemberInfo, string?>? getSummary = null, bool includeObsoleteProperties = false)
        => JsonSchemaGenerator.Generate(typeof(T), getSummary ?? (_ => null), includeObsoleteProperties);

    private static string? GetDocumentedSummary(MemberInfo member)
        => member.Name.StartsWith("Documented", StringComparison.Ordinal) ? $"{member.Name} summary" : null;

    private static JsonObject GetProperties(JsonObject schema)
        => schema["properties"]!.AsObject();

    private static JsonObject Resolve(JsonObject schema, string reference)
    {
        Assert.That(reference, Does.StartWith("#"));

        JsonObject current = schema;
        foreach (var segment in reference.Substring(1).Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = current[segment]!.AsObject();
        }

        return current;
    }

    [Flags]
    private enum TestFlags
    {
        None = 0,
        Read = 1,
        Write = 2,
        All = Read | Write,
    }

    private enum TestStatus
    {
        Active,
        Inactive,
        Pending,
    }

    private sealed class ObjectModel
    {
        public string Name { get; set; } = string.Empty;

        public int Age { get; set; }
    }

    private sealed class SetterModel
    {
        public string Writable { get; set; } = string.Empty;

        public string ReadOnly { get; } = string.Empty;

        public string Computed => Writable;

        public string PrivateSetter { get; private set; } = string.Empty;

        internal string Internal { get; set; } = string.Empty;
    }

    private sealed class ObsoleteModel
    {
        public string Current { get; set; } = string.Empty;

        [Obsolete("Use Current instead.")]
        public string WithMessage { get; set; } = string.Empty;

        [Obsolete]
        public string WithoutMessage { get; set; } = string.Empty;
    }

    private sealed class EnumModel
    {
        public TestStatus Status { get; set; }

        public TestFlags Flags { get; set; }
    }

    private sealed class TimeSpanModel
    {
        public TimeSpan Timeout { get; set; }
    }

    private sealed class NullableModel
    {
        public TimeSpan? Timeout { get; set; }

        public TestFlags? Flags { get; set; }
    }

    private sealed class Address
    {
        public string Street { get; set; } = string.Empty;

        public string City { get; set; } = string.Empty;
    }

    private sealed class NestedModel
    {
        public Address? Address { get; set; }
    }

    private sealed class Node
    {
        public string Name { get; set; } = string.Empty;

        public Node? Child { get; set; }
    }

    private sealed class RecursiveModel
    {
        public Node? Tree { get; set; }
    }

    private sealed class JsonAttributeModel
    {
        [JsonPropertyName("renamed")]
        public string Renamed { get; set; } = string.Empty;

        [JsonIgnore]
        public string Ignored { get; set; } = string.Empty;
    }

    private sealed class DefaultValueModel
    {
        [DefaultValue(18)]
        public int Age { get; set; } = 18;

        [DefaultValue(TestStatus.Pending)]
        public TestStatus Status { get; set; } = TestStatus.Pending;

        [DefaultValue(typeof(TimeSpan), "00:20:00")]
        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(20);

        [DefaultValue(null)]
        public string? Null { get; set; }

        [DefaultValue(typeof(int), "not-a-number")]
        public int Unconvertible { get; set; }
    }

    private sealed class RequiredModel
    {
        [Required]
        public string? Attribute { get; set; }

        public required string Modifier { get; set; }

        [Required]
        public required string ModifierAndAttribute { get; set; }

        [JsonRequired]
        public string? JsonAttribute { get; set; }

        public string? Optional { get; set; }
    }

    private sealed class AnyModel
    {
        public object? Anything { get; set; }
    }

    private sealed class DocumentedType
    {
        public string? Value { get; set; }
    }

    [System.ComponentModel.Description("Type attribute description")]
    private sealed class AttributeType
    {
        public string? Value { get; set; }
    }

    private sealed class DescribedModel
    {
        public string? DocumentedProperty { get; set; }

        [System.ComponentModel.Description("Property attribute description")]
        public string? AttributeProperty { get; set; }

        [System.ComponentModel.Description("Property attribute description")]
        public string? DocumentedAttributeProperty { get; set; }

        public DocumentedType? TypeDocumentedProperty { get; set; }

        public AttributeType? TypeAttributeProperty { get; set; }

        public string? UndescribedProperty { get; set; }
    }
}
