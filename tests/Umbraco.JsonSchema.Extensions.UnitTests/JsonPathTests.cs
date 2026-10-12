using System;
using System.Text.Json.Nodes;
using NUnit.Framework;

namespace Umbraco.JsonSchema.Extensions.UnitTests;

/// <summary>
/// Tests for <see cref="JsonPath" />.
/// </summary>
[TestFixture]
public class JsonPathTests
{
    private const string Json = """
        {
          "foo": "foo",
          "a": { "b": { "c": "c" } },
          "items": [ "item0", { "name": "item1" } ],
          "dotted.name": "dotted",
          "0": "zero",
          "": "empty"
        }
        """;

    /// <summary>
    /// A valid path replaces the existing value it addresses.
    /// </summary>
    /// <param name="path">The JSON path.</param>
    /// <param name="expectedJson">The expected JSON after replacing the value.</param>
    [TestCase("$.foo", """{"foo":"new"}""")]
    [TestCase(".foo", """{"foo":"new"}""")]
    [TestCase("foo", """{"foo":"new"}""")]
    [TestCase("$['foo']", """{"foo":"new"}""")]
    [TestCase("$[\"foo\"]", """{"foo":"new"}""")]
    [TestCase("$.a.b.c", """{"a":{"b":{"c":"new"}}}""")]
    [TestCase("a['b'].c", """{"a":{"b":{"c":"new"}}}""")]
    [TestCase("$.items[0]", """{"items":["new",{"name":"item1"}]}""")]
    [TestCase("$.items[1].name", """{"items":["item0",{"name":"new"}]}""")]
    [TestCase("$['dotted.name']", """{"dotted.name":"new"}""")]
    [TestCase("$['0']", """{"0":"new"}""")]
    [TestCase("$['']", """{"":"new"}""")]
    public void TryReplace_ExistingValue_ReplacesValue(string path, string expectedJson)
    {
        JsonObject root = JsonNode.Parse(Json)!.AsObject();

        var replaced = JsonPath.Parse(path).TryReplace(root, "new");

        Assert.That(replaced, Is.True);
        JsonObject expected = JsonNode.Parse(Json)!.AsObject();
        foreach (var property in JsonNode.Parse(expectedJson)!.AsObject())
        {
            expected[property.Key] = property.Value?.DeepClone();
        }

        Assert.That(JsonNode.DeepEquals(root, expected), Is.True, root.ToJsonString());
    }

    /// <summary>
    /// A valid path that doesn't address an existing value (or addresses the root) leaves the JSON unchanged.
    /// </summary>
    /// <param name="path">The JSON path.</param>
    [TestCase("")]
    [TestCase("$")]
    [TestCase("$.missing")]
    [TestCase("$.missing.foo")]
    [TestCase("$.foo.bar")]
    [TestCase("$.items[2]")]
    [TestCase("$.items[99].name")]
    [TestCase("$[0]")]
    [TestCase("$.a[0]")]
    [TestCase("$.items['0']")]
    public void TryReplace_NoExistingValue_ReturnsFalse(string path)
    {
        JsonObject root = JsonNode.Parse(Json)!.AsObject();

        var replaced = JsonPath.Parse(path).TryReplace(root, "new");

        Assert.That(replaced, Is.False);
        Assert.That(JsonNode.DeepEquals(root, JsonNode.Parse(Json)), Is.True);
    }

    /// <summary>
    /// A malformed path can't be parsed.
    /// </summary>
    /// <param name="path">The JSON path.</param>
    [TestCase("$foo")]
    [TestCase("$.")]
    [TestCase("$..foo")]
    [TestCase("$.foo.")]
    [TestCase("$[")]
    [TestCase("$[]")]
    [TestCase("$[-1]")]
    [TestCase("$[+1]")]
    [TestCase("$[ 1 ]")]
    [TestCase("$[1.5]")]
    [TestCase("$[99999999999]")]
    [TestCase("$[foo]")]
    [TestCase("$['foo'")]
    [TestCase("$['foo]")]
    [TestCase("$['foo'x]")]
    [TestCase("$[\"foo']")]
    [TestCase("$.foo]")]
    public void Parse_MalformedPath_Throws(string path)
        => Assert.Throws<FormatException>(() => JsonPath.Parse(path));

    /// <summary>
    /// Getting an object creates the intermediate objects that don't exist.
    /// </summary>
    [Test]
    public void GetOrCreateObject_CreatesIntermediateObjects()
    {
        var root = new JsonObject { ["existing"] = new JsonObject { ["keep"] = true } };

        JsonObject target = JsonPath.Parse("$.existing['new'].nested").GetOrCreateObject(root);
        target["added"] = true;

        Assert.That(root.ToJsonString(), Is.EqualTo("""{"existing":{"keep":true,"new":{"nested":{"added":true}}}}"""));
    }

    /// <summary>
    /// Getting an object at the root path returns the root itself.
    /// </summary>
    /// <param name="path">The JSON path.</param>
    [TestCase("")]
    [TestCase("$")]
    public void GetOrCreateObject_RootPath_ReturnsRoot(string path)
    {
        var root = new JsonObject();

        Assert.That(JsonPath.Parse(path).GetOrCreateObject(root), Is.SameAs(root));
    }

    /// <summary>
    /// Getting an object through an existing value that isn't an object, or an array index, is an error.
    /// </summary>
    /// <param name="path">The JSON path.</param>
    [TestCase("$.foo")]
    [TestCase("$.foo.bar")]
    [TestCase("$.items")]
    [TestCase("$.a[0]")]
    [TestCase("$.missing[0]")]
    public void GetOrCreateObject_NonObjectOrArrayIndex_Throws(string path)
    {
        JsonObject root = JsonNode.Parse(Json)!.AsObject();

        Assert.Throws<InvalidOperationException>(() => JsonPath.Parse(path).GetOrCreateObject(root));
    }
}
