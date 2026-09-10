using Json.Schema;
using Shouldly;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;

namespace EnvToJson.Tests;

public class GetTargetTypesTests
{
    private readonly ITestOutputHelper _output;

    public GetTargetTypesTests(ITestOutputHelper output) => _output = output;

    private static JsonSchema Schema(string json) => JsonSchema.FromText(json);

    [Fact]
    public void JsonPointerFormat_Baseline()
    {
        // Pins the exact pointer string format used as dictionary keys so a package upgrade that changes it is immediately visible.
        var schema = Schema("""{"type":"object","properties":{"port":{"type":"number"}}}""");
        var node = JsonNode.Parse("""{"port":"5432"}""");

        var result = Converter.GetTargetTypes(node, schema);

        _output.WriteLine("Pointer keys: " + string.Join(", ", result.Keys));
        result.Count.ShouldBe(1);
        // The key must contain "port"; exact prefix (# vs empty) is what we're documenting here.
        result.Keys.ShouldContain(k => k.Contains("port"));
    }

    [Fact]
    public void StringWhereNumberExpected_ReturnsNumberCoercion()
    {
        var schema = Schema("""{"type":"object","properties":{"port":{"type":"number"}}}""");
        var node = JsonNode.Parse("""{"port":"5432"}""");

        var result = Converter.GetTargetTypes(node, schema);

        result.Count.ShouldBe(1);
        result.Values.ShouldContain(SchemaValueType.Number);
    }

    [Fact]
    public void StringWhereBooleanExpected_ReturnsBooleanCoercion()
    {
        var schema = Schema("""{"type":"object","properties":{"debug":{"type":"boolean"}}}""");
        var node = JsonNode.Parse("""{"debug":"true"}""");

        var result = Converter.GetTargetTypes(node, schema);

        result.Count.ShouldBe(1);
        result.Values.ShouldContain(SchemaValueType.Boolean);
    }

    [Fact]
    public void ObjectWhereArrayExpected_ReturnsArrayCoercion()
    {
        var schema = Schema("""{"type":"object","properties":{"items":{"type":"array"}}}""");
        var node = JsonNode.Parse("""{"items":{"0":"a","1":"b"}}""");

        var result = Converter.GetTargetTypes(node, schema);

        result.Count.ShouldBe(1);
        result.Values.ShouldContain(SchemaValueType.Array);
    }

    [Fact]
    public void AllTypesMatch_ReturnsEmpty()
    {
        var schema = Schema("""{"type":"object","properties":{"name":{"type":"string"}}}""");
        var node = JsonNode.Parse("""{"name":"Alice"}""");

        var result = Converter.GetTargetTypes(node, schema);

        result.ShouldBeEmpty();
    }

    [Fact]
    public void NonCoercibleMismatch_Ignored()
    {
        // bool where string expected — not a coercible pair, must not appear in result
        var schema = Schema("""{"type":"object","properties":{"name":{"type":"string"}}}""");
        var node = JsonNode.Parse("""{"name":true}""");

        var result = Converter.GetTargetTypes(node, schema);

        result.ShouldBeEmpty();
    }
}
