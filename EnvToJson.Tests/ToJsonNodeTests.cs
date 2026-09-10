using Microsoft.Extensions.Configuration;
using Shouldly;
using System.Text.Json.Nodes;
using Xunit;

namespace EnvToJson.Tests;

public class ToJsonNodeTests
{
    private static System.Collections.Generic.IEnumerable<IConfigurationSection> Sections(
        params System.Collections.Generic.KeyValuePair<string, string?>[] pairs) =>
        InMemoryConfiguration.Build(pairs).GetChildren();

    [Fact]
    public void FlatKeys_ProduceFlatObject()
    {
        var sections = Sections(
            ConfigKeyVal.From(KeyVal.Build("FOO", "bar")),
            ConfigKeyVal.From(KeyVal.Build("BAZ", "qux"))
        );

        var result = (JsonObject)Converter.ToJsonNode(sections);

        result["FOO"].ShouldNotBeNull().GetValue<string>().ShouldBe("bar");
        result["BAZ"].ShouldNotBeNull().GetValue<string>().ShouldBe("qux");
        result.Count.ShouldBe(2);
    }

    [Fact]
    public void DoubleUnderscore_ProducesNestedObject()
    {
        var sections = Sections(
            ConfigKeyVal.From(KeyVal.Build("DB__HOST", "localhost"))
        );

        var result = (JsonObject)Converter.ToJsonNode(sections);

        var db = result["DB"].ShouldBeOfType<JsonObject>();
        db["HOST"].ShouldNotBeNull().GetValue<string>().ShouldBe("localhost");
    }

    [Fact]
    public void DeepNesting_ProducesDeepObject()
    {
        var sections = Sections(
            ConfigKeyVal.From(KeyVal.Build("A__B__C__D", "val"))
        );

        var result = (JsonObject)Converter.ToJsonNode(sections);

        var a = result["A"].ShouldBeOfType<JsonObject>();
        var b = a["B"].ShouldBeOfType<JsonObject>();
        var c = b["C"].ShouldBeOfType<JsonObject>();
        c["D"].ShouldNotBeNull().GetValue<string>().ShouldBe("val");
    }

    [Fact]
    public void NullStringLiteral_ProducesJsonNull()
    {
        var sections = Sections(
            ConfigKeyVal.From(KeyVal.Build("KEY", "null"))
        );

        var result = (JsonObject)Converter.ToJsonNode(sections);

        result.ContainsKey("KEY").ShouldBeTrue();
        result["KEY"].ShouldBeNull();
    }

    [Theory]
    [InlineData("NULL")]
    [InlineData("Null")]
    [InlineData("nUlL")]
    public void NullStringLiteral_CaseInsensitive_ProducesJsonNull(string nullVariant)
    {
        var sections = Sections(
            ConfigKeyVal.From(KeyVal.Build("KEY", nullVariant))
        );

        var result = (JsonObject)Converter.ToJsonNode(sections);

        result["KEY"].ShouldBeNull();
    }

    [Fact]
    public void EmptyStringValue_StaysEmptyString()
    {
        var sections = Sections(
            ConfigKeyVal.From(KeyVal.Build("KEY", ""))
        );

        var result = (JsonObject)Converter.ToJsonNode(sections);

        result["KEY"].ShouldNotBeNull().GetValue<string>().ShouldBe("");
    }

    [Fact]
    public void NumericKeys_WithoutSchema_ProduceObjectNotArray()
    {
        var sections = Sections(
            ConfigKeyVal.From(KeyVal.Build("ITEMS__0", "a")),
            ConfigKeyVal.From(KeyVal.Build("ITEMS__1", "b"))
        );

        var result = (JsonObject)Converter.ToJsonNode(sections);

        var items = result["ITEMS"].ShouldBeOfType<JsonObject>();
        items["0"].ShouldNotBeNull().GetValue<string>().ShouldBe("a");
        items["1"].ShouldNotBeNull().GetValue<string>().ShouldBe("b");
    }
}
