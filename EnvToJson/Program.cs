using Json.Schema;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

var prefixOption = new Option<string>("--prefix")
{
    Description = "The prefix that environment variable names must start with. The prefix will be removed from the environment variable names"
};

var schemaOption = new Option<FileInfo>("--schema");

var rootCommand = new RootCommand()
{
    prefixOption,
    schemaOption,
};
rootCommand.SetAction((ParseResult parseResult, CancellationToken cancellationToken) =>
{
    var environmentVariablesPrefix = parseResult.GetValue(prefixOption);
    var jsonSchemaFile = parseResult.GetValue(schemaOption);
    return Main(environmentVariablesPrefix, jsonSchemaFile);
});

return await rootCommand.Parse(args).InvokeAsync();

Task<int> Main(string? environmentVariablesPrefix, FileInfo? jsonSchemaFile)
{
    var configFromEnvironmentVariables = new ConfigurationBuilder()
        .AddEnvironmentVariables(prefix: string.IsNullOrWhiteSpace(environmentVariablesPrefix) ? null : environmentVariablesPrefix)
        .Build();

    var jsonNode = Converter.ToJsonNode(configFromEnvironmentVariables.GetChildren());

    if (jsonSchemaFile?.Exists is true)
    {
        var jsonSchema = JsonSchema.FromFile(jsonSchemaFile.FullName);

        if (string.IsNullOrWhiteSpace(environmentVariablesPrefix))
            Converter.RemoveAdditionalProperties(jsonNode, jsonSchema);

        Dictionary<string, SchemaValueType> targetTypes;
        do
        {
            targetTypes = Converter.GetTargetTypes(jsonNode, jsonSchema);
            if (!targetTypes.Any())
                break;

            jsonNode = Converter.ChangeTypes(jsonNode, targetTypes);
        }
        while (targetTypes.Any(t => t.Value is SchemaValueType.Array));

        var evaluationResults = jsonSchema.Evaluate(Converter.NodeToElement(jsonNode), options: new()
        {
            OutputFormat = OutputFormat.List,
        });

        if (!evaluationResults.IsValid)
        {
            ConsoleWriteJson(jsonNode);

            var errorDetails = (evaluationResults.Details ?? [])
                .Where(d => !d.IsValid && d.Errors?.Any() is true)
                .Select(d => $"{d.InstanceLocation}: {string.Join(", ", (IEnumerable<string>?)d.Errors?.Values ?? Array.Empty<string>())} [{d.EvaluationPath}]")
                .ToList();
            Console.Error.WriteLine($"Schema validation errors:{Environment.NewLine}{string.Join(Environment.NewLine, errorDetails)}");

            return Task.FromResult(1);
        }
    }

    ConsoleWriteJson(jsonNode);

    return Task.FromResult(0);

    void ConsoleWriteJson(JsonNode? node)
    {
        var json = node?.ToJsonString(options: new() { WriteIndented = true });
        Console.WriteLine(json);
    }
}

[JsonSerializable(typeof(SchemaValueType))]
partial class JsonSchemaSerializerContext : JsonSerializerContext;
