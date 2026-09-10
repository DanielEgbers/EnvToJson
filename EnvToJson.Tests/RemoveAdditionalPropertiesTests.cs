using Json.Schema;
using Shouldly;
using System.Text.Json.Nodes;
using Xunit;

namespace EnvToJson.Tests;

public class RemoveAdditionalPropertiesTests
{
    private static JsonSchema Schema(string json) => JsonSchema.FromText(json);

    private const string StrictSchema = """{"type":"object","properties":{"A":{"type":"string"}},"additionalProperties":false}""";

    [Fact]
    public void UnknownKey_RemovedWhenAdditionalPropertiesFalse()
    {
        var node = JsonNode.Parse("""{"A":"val","EXTRA":"other"}""").ShouldBeOfType<JsonObject>();
        Converter.RemoveAdditionalProperties(node, Schema(StrictSchema));
        node.ContainsKey("A").ShouldBeTrue();
        node.ContainsKey("EXTRA").ShouldBeFalse();
    }

    [Fact]
    public void KnownKey_AlwaysPreserved()
    {
        var node = JsonNode.Parse("""{"A":"val"}""").ShouldBeOfType<JsonObject>();
        Converter.RemoveAdditionalProperties(node, Schema(StrictSchema));
        node["A"].ShouldNotBeNull().GetValue<string>().ShouldBe("val");
    }

    [Fact]
    public void NoAdditionalPropertiesConstraint_NothingRemoved()
    {
        var schema = Schema("""{"type":"object","properties":{"A":{"type":"string"}}}""");
        var node = JsonNode.Parse("""{"A":"val","EXTRA":"other"}""").ShouldBeOfType<JsonObject>();
        Converter.RemoveAdditionalProperties(node, schema);
        node.Count.ShouldBe(2);
    }

    [Fact]
    public void NonObjectSchema_NodeUntouched()
    {
        var schema = Schema("""{"type":"array"}""");
        var node = JsonNode.Parse("""{"A":"val","EXTRA":"other"}""").ShouldBeOfType<JsonObject>();
        Converter.RemoveAdditionalProperties(node, schema);
        node.Count.ShouldBe(2);
    }

    [Fact]
    public void OnlyTopLevel_NestedUnknownKeysNotRemoved()
    {
        var schema = Schema("""{"type":"object","properties":{"X":{"type":"object"}},"additionalProperties":false}""");
        var node = JsonNode.Parse("""{"X":{"NESTED_UNKNOWN":"val"}}""").ShouldBeOfType<JsonObject>();
        Converter.RemoveAdditionalProperties(node, schema);
        // Top-level key X is known — kept. Its child is NOT removed (only depth 1 is cleaned).
        node.ContainsKey("X").ShouldBeTrue();
        var x = node["X"].ShouldBeOfType<JsonObject>();
        x.ContainsKey("NESTED_UNKNOWN").ShouldBeTrue();
    }

    [Fact]
    public void NullNode_DoesNotThrow()
    {
        Should.NotThrow(() => Converter.RemoveAdditionalProperties(null, Schema(StrictSchema)));
    }
}
