using Json.Schema;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Xunit;

namespace EnvToJson.Tests;

public class TypeCoercionTests
{
    // JSON Pointer format: root = "" (empty), property = "/key". Verified by GetTargetTypesTests.JsonPointerFormat_Baseline.
    private const string RootPointer = "";

    private static Dictionary<string, SchemaValueType> Target(SchemaValueType type, string pointer = RootPointer) =>
        new() { [pointer] = type };

    [Fact]
    public void StringToNumber_Valid_CoercesToDouble()
    {
        var node = JsonNode.Parse("\"42\"");
        var result = Converter.ChangeTypes(node, Target(SchemaValueType.Number));
        result.ShouldNotBeNull().GetValue<double>().ShouldBe(42.0);
    }

    [Fact]
    public void StringToInteger_Valid_CoercesToDouble()
    {
        var node = JsonNode.Parse("\"7\"");
        var result = Converter.ChangeTypes(node, Target(SchemaValueType.Integer));
        result.ShouldNotBeNull().GetValue<double>().ShouldBe(7.0);
    }

    [Fact]
    public void StringToBoolean_True_CoercesToTrue()
    {
        var node = JsonNode.Parse("\"true\"");
        var result = Converter.ChangeTypes(node, Target(SchemaValueType.Boolean));
        result.ShouldNotBeNull().GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public void StringToBoolean_False_CoercesToFalse()
    {
        var node = JsonNode.Parse("\"false\"");
        var result = Converter.ChangeTypes(node, Target(SchemaValueType.Boolean));
        result.ShouldNotBeNull().GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public void StringToNumber_NonParseable_StaysString()
    {
        var node = JsonNode.Parse("\"abc\"");
        var result = Converter.ChangeTypes(node, Target(SchemaValueType.Number));
        result.ShouldNotBeNull().GetValue<string>().ShouldBe("abc");
    }

    [Fact]
    public void StringToBoolean_NonParseable_StaysString()
    {
        var node = JsonNode.Parse("\"maybe\"");
        var result = Converter.ChangeTypes(node, Target(SchemaValueType.Boolean));
        result.ShouldNotBeNull().GetValue<string>().ShouldBe("maybe");
    }

    [Fact]
    public void ObjectToArray_CoercesAllValues()
    {
        var node = JsonNode.Parse("""{"0":"a","1":"b"}""");
        var result = Converter.ChangeTypes(node, Target(SchemaValueType.Array));
        var arr = result.ShouldBeOfType<JsonArray>();
        arr.Count.ShouldBe(2);
        arr[0].ShouldNotBeNull().GetValue<string>().ShouldBe("a");
        arr[1].ShouldNotBeNull().GetValue<string>().ShouldBe("b");
    }

    [Fact]
    public void NullNode_PassesThrough()
    {
        var result = Converter.ChangeTypes(null, new Dictionary<string, SchemaValueType>());
        result.ShouldBeNull();
    }

    [Fact]
    public void NestedCoercion_OtherKeysPreserved()
    {
        var node = JsonNode.Parse("""{"port":"5432","name":"Alice"}""");
        var result = Converter.ChangeTypes(node, Target(SchemaValueType.Number, "/port")).ShouldBeOfType<JsonObject>();
        result["port"].ShouldNotBeNull().GetValue<double>().ShouldBe(5432.0);
        result["name"].ShouldNotBeNull().GetValue<string>().ShouldBe("Alice");
    }

    [Fact]
    public void UnsupportedObjectTarget_Throws()
    {
        var node = JsonNode.Parse("""{"key":"val"}""");
        Should.Throw<NotSupportedException>(() =>
            Converter.ChangeTypes(node, Target(SchemaValueType.String)));
    }

    [Fact]
    public void UnsupportedStringTarget_Throws()
    {
        var node = JsonNode.Parse("\"x\"");
        Should.Throw<NotSupportedException>(() =>
            Converter.ChangeTypes(node, Target(SchemaValueType.Object)));
    }
}
