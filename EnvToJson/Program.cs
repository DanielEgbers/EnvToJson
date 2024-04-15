using CommunityToolkit.Diagnostics;
using Json.More;
using Json.Schema;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

var prefixOption = new Option<string?>(
    name: "--prefix",
    description: "The prefix that environment variable names must start with. The prefix will be removed from the environment variable names"
);

var schemaOption = new Option<FileInfo?>(
    name: "--schema"
);

var rootCommand = new RootCommand()
{
    prefixOption,
    schemaOption,
};
rootCommand.SetHandler(Main, prefixOption, schemaOption);

return await rootCommand.InvokeAsync(args);

Task<int> Main(string? environmentVariablesPrefix, FileInfo? jsonSchemaFile)
{
    var configFromEnvironmentVariables = new ConfigurationBuilder()
        .AddEnvironmentVariables(prefix: string.IsNullOrWhiteSpace(environmentVariablesPrefix) ? null : environmentVariablesPrefix)
        .Build();

    var jsonNode = ToJsonNode(configFromEnvironmentVariables.GetChildren());

    if (jsonSchemaFile?.Exists is true)
    {
        var jsonSchema = JsonSchema.FromFile(jsonSchemaFile.FullName);

        if (string.IsNullOrWhiteSpace(environmentVariablesPrefix))
            RemoveAdditionalProperties(jsonNode, jsonSchema);

        Dictionary<string, SchemaValueType> targetTypes;
        do
        {
            targetTypes = GetTargetTypes(jsonNode, jsonSchema);
            if (!targetTypes.Any())
                break;

            jsonNode = ChangeTypes(jsonNode, targetTypes);
        }
        while (targetTypes.Any(t => t.Value is SchemaValueType.Array));

        var evaluationResults = jsonSchema.Evaluate(jsonNode, options: new()
        {
            OutputFormat = OutputFormat.List,
        });

        if (!evaluationResults.IsValid)
        {
            ConsoleWriteJson(jsonNode);

            var errorDetails = evaluationResults.Details
                .Where(d => !d.IsValid && d.Errors?.Any() is true)
                .Select(d => $"{d.InstanceLocation}: {string.Join(", ", d.Errors?.Values ?? Array.Empty<string>())} [{d.EvaluationPath}]")
                .ToList();
            Console.Error.WriteLine($"Schema validation errors:{Environment.NewLine}{string.Join(Environment.NewLine, errorDetails)}");

            return Task.FromResult(1);
        }
    }

    ConsoleWriteJson(jsonNode);

    return Task.FromResult(0);

    void ConsoleWriteJson(JsonNode? jsonNode)
    {
        var json = jsonNode?.ToJsonString(options: new() { WriteIndented = true });
        Console.WriteLine(json);
    }
}

JsonNode ToJsonNode(IEnumerable<IConfigurationSection> sections)
{
    var obj = new JsonObject();

    foreach (var section in sections)
    {
        if (section.GetChildren().Any())
            obj.Add(section.Key, ToJsonNode(section.GetChildren()));
        else
            obj.Add(section.Key, section.Value?.Equals("null", StringComparison.InvariantCultureIgnoreCase) is true ? null : section.Value);
    }

    return obj;
}

JsonNode? ChangeTypes(JsonNode? jsonNode, IReadOnlyDictionary<string, SchemaValueType> targetTypes)
{
    if (jsonNode is JsonObject jsonObject)
    {
        if (targetTypes.TryGetValue(jsonObject.GetPointerFromRoot(), out var targetType))
        {
            if (targetType is SchemaValueType.Array)
            {
                var arr = new JsonArray();
                foreach (var property in jsonObject)
                    arr.Add(ChangeTypes(property.Value, targetTypes));
                return arr;
            }
            else
            {
                return ThrowHelper.ThrowNotSupportedException<JsonNode>($"{nameof(targetType)}: {targetType}");
            }
        }
        else
        {
            var obj = new JsonObject();
            foreach (var property in jsonObject)
                obj[property.Key] = ChangeTypes(property.Value, targetTypes);
            return obj;
        }
    }
    else if (jsonNode is JsonArray jsonArray)
    {
        var arr = new JsonArray();
        foreach (var node in jsonArray)
            arr.Add(ChangeTypes(node, targetTypes));
        return arr;
    }
    else if (jsonNode is JsonValue jsonValue)
    {
        switch (jsonValue.GetValueKind())
        {
            case JsonValueKind.String:
                var value = jsonValue.GetValue<string>();

                if (targetTypes.TryGetValue(jsonValue.GetPointerFromRoot(), out var targetType))
                {
                    return targetType switch
                    {
                        SchemaValueType.Number or SchemaValueType.Integer =>
                            double.TryParse(value, CultureInfo.InvariantCulture, out var v) ? v : value,
                        SchemaValueType.Boolean =>
                            bool.TryParse(value, out var v) ? v : value,
                        _ =>
                            ThrowHelper.ThrowNotSupportedException<string>($"{nameof(targetType)}: {targetType}"),
                    };
                }
                else
                {
                    return value;
                }
            case JsonValueKind.Number:
                return jsonValue.GetValue<double>();
            case JsonValueKind.True or JsonValueKind.False:
                return jsonValue.GetValue<bool>();
            default:
                return ThrowHelper.ThrowNotSupportedException<string>(nameof(jsonValue));

        }
    }
    else if (jsonNode is null)
    {
        return null;
    }
    else
    {
        return ThrowHelper.ThrowNotSupportedException<JsonNode>(nameof(jsonNode));
    }
}

Dictionary<string, SchemaValueType> GetTargetTypes(JsonNode? jsonNode, JsonSchema jsonSchema)
{
    var evaluationOptions = new EvaluationOptions
    {
        OutputFormat = OutputFormat.List,
        Culture = CultureInfo.InvariantCulture,
    };

    var typeErrorMessageRegex = new Regex(Regex.Replace(
        input: Regex.Escape(ErrorMessages.GetType(evaluationOptions.Culture)),
        pattern: @"\\\[\\\[\w+\]\]",
        replacement: @"(""\w+"")"
    ));

    var evaluationResults = jsonSchema.Evaluate(jsonNode, options: evaluationOptions);

    return evaluationResults.Details
        .Select(d =>
        {
            if (d.Errors?.TryGetValue("type", out var typeErrorMessage) is not true)
                return default;

            var typeErrorMessageMatch = typeErrorMessageRegex.Match(typeErrorMessage);
            Guard.IsTrue(typeErrorMessageMatch.Success);

            var received = JsonSerializer.Deserialize(typeErrorMessageMatch.Groups[1].Value, jsonTypeInfo: JsonSchemaSerializerContext.Default.SchemaValueType);
            var expected = JsonSerializer.Deserialize(typeErrorMessageMatch.Groups[2].Value, jsonTypeInfo: JsonSchemaSerializerContext.Default.SchemaValueType);

            if (received is SchemaValueType.String && expected is SchemaValueType.Number or SchemaValueType.Integer or SchemaValueType.Boolean)
                return KeyValuePair.Create(d.InstanceLocation.ToString(), expected);
            else if (received is SchemaValueType.Object && expected is SchemaValueType.Array)
                return KeyValuePair.Create(d.InstanceLocation.ToString(), expected);
            else
                return default;
        })
        .Where(t => t.Key is not null)
        .ToDictionary(t => t.Key, t => t.Value);
}

void RemoveAdditionalProperties(JsonNode? jsonNode, JsonSchema jsonSchema)
{
    if (jsonNode is not JsonObject jsonObject)
        return;

    if (jsonSchema.GetJsonType() is not SchemaValueType.Object)
        return;

    if (jsonSchema.GetAdditionalProperties()?.BoolValue is not false)
        return;

    var evaluationResults = jsonSchema.Evaluate(jsonNode, options: new()
    {
        OutputFormat = OutputFormat.List,
    });

    var propertyNames = evaluationResults.Details
        .Where(d => !d.IsValid && d.EvaluationPath.Segments.Any(s => s.Value is "additionalProperties"))
        .Where(d => d.InstanceLocation.Segments.Length is 1)
        .Select(d => d.InstanceLocation.Segments.Single().Value)
        .ToList();

    foreach (var propertyName in propertyNames)
        jsonObject.Remove(propertyName);
}

[JsonSerializable(typeof(SchemaValueType))]
partial class JsonSchemaSerializerContext : JsonSerializerContext;