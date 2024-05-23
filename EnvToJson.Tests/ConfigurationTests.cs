using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace EnvToJson.Tests;

public class ConfigurationTests
{
    private ITestOutputHelper _output;

    public ConfigurationTests(ITestOutputHelper output) =>
        _output = output;

    [Fact]
    public void PrimitiveTypes()
    {
        var valueA = KeyVal.Build("a", "av");
        var valueB = KeyVal.Build("b", 42);
        var valueC = KeyVal.Build("c", true);

        var configuration = InMemoryConfiguration.Build(
            ConfigKeyVal.From(valueA),
            ConfigKeyVal.From(valueB),
            ConfigKeyVal.From(valueC)
        );

        _output.WriteLine(configuration.GetDebugView());

        configuration.GetValue<string>(valueA.Key).Should().Be(valueA.Value);
        configuration.GetValue<int>(valueB.Key).Should().Be(valueB.Value);
        configuration.GetValue<bool>(valueC.Key).Should().Be(valueC.Value);
    }

    [Fact]
    public void StringArray()
    {
        var baseKey = "items";

        var valueA = KeyVal.Build($"{baseKey}__0", "av");
        var valueB = KeyVal.Build($"{baseKey}__1", "bv");
        var valueC = KeyVal.Build($"{baseKey}__2", "cv");
        var valueD = KeyVal.Build($"{baseKey}__abc", (object?)null);
        var valueE = KeyVal.Build($"{baseKey}__xyz", "abcv");

        var configuration = InMemoryConfiguration.Build(
            ConfigKeyVal.From(valueA),
            ConfigKeyVal.From(valueB),
            ConfigKeyVal.From(valueC),
            ConfigKeyVal.From(valueD),
            ConfigKeyVal.From(valueE)
        );

        _output.WriteLine(configuration.GetDebugView());

        var values = configuration.GetSection(baseKey).Get<string[]>()!;

        values.Should().NotBeNull();
        values.Should().HaveCount(4);

        values[0].Should().Be(valueA.Value);
        values[1].Should().Be(valueB.Value);
        values[2].Should().Be(valueC.Value);
        values[3].Should().Be(valueE.Value);
    }

    [Fact]
    public void IntegerArray()
    {
        var baseKey = "items";

        var valueA = KeyVal.Build($"{baseKey}__0", 0);
        var valueB = KeyVal.Build($"{baseKey}__1", 2);
        var valueC = KeyVal.Build($"{baseKey}__2", 4);
        var valueD = KeyVal.Build($"{baseKey}__abc", "number");
        var valueE = KeyVal.Build($"{baseKey}__xyz", 8);

        var configuration = InMemoryConfiguration.Build(
            ConfigKeyVal.From(valueA),
            ConfigKeyVal.From(valueB),
            ConfigKeyVal.From(valueC),
            ConfigKeyVal.From(valueD),
            ConfigKeyVal.From(valueE)
        );

        _output.WriteLine(configuration.GetDebugView());

        var values = configuration.GetSection(baseKey).Get<int[]>()!;

        values.Should().NotBeNull();
        values.Should().HaveCount(4);

        values[0].Should().Be(valueA.Value);
        values[1].Should().Be(valueB.Value);
        values[2].Should().Be(valueC.Value);
        values[3].Should().Be(valueE.Value);
    }

    private record TestObject()
    {
        public int A { get; init; } = 0;
        public string B { get; init; } = string.Empty;
        public bool Q { get; init; } = false;
    }

    [Fact]
    public void ObjectArray()
    {
        var baseKey = "items";

        var valueAA = KeyVal.Build($"{baseKey}__0__A", 0);
        var valueAB = KeyVal.Build($"{baseKey}__0__B", "some");
        var valueAQ = KeyVal.Build($"{baseKey}__0__Q", true);
        var valueB = KeyVal.Build($"{baseKey}__1__A", 2);
        var valueC = KeyVal.Build($"{baseKey}__2__A", 4);
        var valueD = KeyVal.Build($"{baseKey}__abc__B", "number");
        var valueEA = KeyVal.Build($"{baseKey}__xyz__A", 8);
        var valueEB = KeyVal.Build($"{baseKey}__xyz__B", "bla");

        var configuration = InMemoryConfiguration.Build(
            ConfigKeyVal.From(valueAA),
            ConfigKeyVal.From(valueAB),
            ConfigKeyVal.From(valueAQ),
            ConfigKeyVal.From(valueB),
            ConfigKeyVal.From(valueC),
            ConfigKeyVal.From(valueD),
            ConfigKeyVal.From(valueEA),
            ConfigKeyVal.From(valueEB)
        );

        _output.WriteLine(configuration.GetDebugView());

        var values = configuration.GetSection(baseKey).Get<TestObject[]>()!;

        values.Should().NotBeNull();
        values.Should().HaveCount(5);

        values[0].A.Should().Be(valueAA.Value);
        values[0].B.Should().Be(valueAB.Value);
        values[0].Q.Should().Be(valueAQ.Value);
        values[1].A.Should().Be(valueB.Value);
        values[2].A.Should().Be(valueC.Value);
        values[3].B.Should().Be(valueD.Value);
        values[4].A.Should().Be(valueEA.Value);
        values[4].B.Should().Be(valueEB.Value);
    }
}