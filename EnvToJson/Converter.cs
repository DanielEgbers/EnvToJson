using CommunityToolkit.Diagnostics;
using Json.More;
using Json.Schema;
using Json.Schema.Keywords;
using Microsoft.Extensions.Configuration;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal static class Converter
{
    internal static JsonNode ToJsonNode(IEnumerable<IConfigurationSection> sections)
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

    internal static JsonNode? ChangeTypes(JsonNode? jsonNode, IReadOnlyDictionary<string, SchemaValueType> targetTypes)
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

    internal static Dictionary<string, SchemaValueType> GetTargetTypes(JsonNode? jsonNode, JsonSchema jsonSchema)
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

        var evaluationResults = jsonSchema.Evaluate(NodeToElement(jsonNode), options: evaluationOptions);

        return (evaluationResults.Details ?? [])
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

    internal static void RemoveAdditionalProperties(JsonNode? jsonNode, JsonSchema jsonSchema)
    {
        if (jsonNode is not JsonObject jsonObject)
            return;

        var schemaRoot = jsonSchema.Root;
        if (schemaRoot is null)
            return;

        var typeRawValue = schemaRoot.Keywords.FirstOrDefault(k => k.Handler is TypeKeyword)?.RawValue;
        if (typeRawValue?.ValueKind is not JsonValueKind.String || typeRawValue.Value.GetString() != "object")
            return;

        if (schemaRoot.Keywords.FirstOrDefault(k => k.Handler is AdditionalPropertiesKeyword)?.RawValue.ValueKind is not JsonValueKind.False)
            return;

        var evaluationResults = jsonSchema.Evaluate(NodeToElement(jsonNode), options: new()
        {
            OutputFormat = OutputFormat.List,
        });

        var propertyNames = (evaluationResults.Details ?? [])
            .Where(d => !d.IsValid && Enumerable.Range(0, d.EvaluationPath.SegmentCount).Any(i => d.EvaluationPath.GetSegment(i).Equals("additionalProperties")))
            .Where(d => d.InstanceLocation.SegmentCount is 1)
            .Select(d => new string(d.InstanceLocation.GetSegment(0).AsSpan()))
            .ToList();

        foreach (var propertyName in propertyNames)
            jsonObject.Remove(propertyName);
    }

    internal static JsonElement NodeToElement(JsonNode? node)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        if (node is null)
            writer.WriteNullValue();
        else
            node.WriteTo(writer);
        writer.Flush();
        using var doc = JsonDocument.Parse(buffer.WrittenMemory);
        return doc.RootElement.Clone();
    }
}
