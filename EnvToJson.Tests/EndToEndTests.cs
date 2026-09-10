using Json.Schema;
using Microsoft.Extensions.Configuration;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Xunit;

namespace EnvToJson.Tests;

public class EndToEndTests
{
    // --- Pipeline composition tests (no process overhead) ---

    private static JsonNode? RunPipeline(JsonSchema schema, params KeyValuePair<string, string?>[] configPairs)
    {
        var config = InMemoryConfiguration.Build(configPairs);
        var jsonNode = Converter.ToJsonNode(config.GetChildren());

        Dictionary<string, SchemaValueType> targetTypes;
        do
        {
            targetTypes = Converter.GetTargetTypes(jsonNode, schema);
            if (!targetTypes.Any())
                break;
            jsonNode = Converter.ChangeTypes(jsonNode, targetTypes);
        }
        while (targetTypes.Any(t => t.Value is SchemaValueType.Array));

        return jsonNode;
    }

    [Fact]
    public void NumericKeysWithArraySchema_ProducesArray()
    {
        var schema = JsonSchema.FromText("""{"type":"object","properties":{"ITEMS":{"type":"array","items":{"type":"string"}}}}""");
        var result = RunPipeline(schema,
            ConfigKeyVal.From(KeyVal.Build("ITEMS__0", "a")),
            ConfigKeyVal.From(KeyVal.Build("ITEMS__1", "b"))).ShouldBeOfType<JsonObject>();

        var items = result["ITEMS"].ShouldBeOfType<JsonArray>();
        items.Count.ShouldBe(2);
        items[0].ShouldNotBeNull().GetValue<string>().ShouldBe("a");
        items[1].ShouldNotBeNull().GetValue<string>().ShouldBe("b");
    }

    [Fact]
    public void SchemaCoercesPortToNumber()
    {
        var schema = JsonSchema.FromText("""{"type":"object","properties":{"PORT":{"type":"number"}}}""");
        var result = RunPipeline(schema,
            ConfigKeyVal.From(KeyVal.Build("PORT", "8080"))).ShouldBeOfType<JsonObject>();

        result["PORT"].ShouldNotBeNull().GetValue<double>().ShouldBe(8080.0);
    }

    [Fact]
    public void IterativeCoercion_ArrayOfObjectsWithNumberProperty()
    {
        // ROWS object → array (first pass), then ROWS[0].VAL string → number (second pass)
        var schema = JsonSchema.FromText("""
            {
              "type": "object",
              "properties": {
                "ROWS": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": { "VAL": { "type": "number" } }
                  }
                }
              }
            }
            """);
        var result = RunPipeline(schema,
            ConfigKeyVal.From(KeyVal.Build("ROWS__0__VAL", "1"))).ShouldBeOfType<JsonObject>();

        var rows = result["ROWS"].ShouldBeOfType<JsonArray>();
        rows.Count.ShouldBe(1);
        rows[0].ShouldBeOfType<JsonObject>()["VAL"].ShouldNotBeNull().GetValue<double>().ShouldBe(1.0);
    }

    // --- Process-based CLI tests ---

    // AppContext.BaseDirectory = .../artifacts/bin/EnvToJson.Tests/<config>/
    // Binary lives at    .../artifacts/bin/EnvToJson/<config>/EnvToJson[.exe]
    private static readonly string BinaryPath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "EnvToJson",
        Path.GetFileName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "EnvToJson.exe" : "EnvToJson"
    ));

    private static (int ExitCode, string Stdout, string Stderr) RunCli(
        IDictionary<string, string> envVars,
        string args = "")
    {
        var psi = new ProcessStartInfo
        {
            FileName = BinaryPath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        // Clear inherited environment; set only what the test needs.
        psi.Environment.Clear();
        foreach (var (key, value) in envVars)
            psi.Environment[key] = value;

        using var process = Process.Start(psi).ShouldNotBeNull();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }

    private static string WriteSchemaFile(string schemaJson)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, schemaJson);
        return path;
    }

    [Fact]
    public void PrefixFiltersAndStripsKeys()
    {
        var (exitCode, stdout, _) = RunCli(
            new Dictionary<string, string> { ["APP_FOO"] = "x", ["OTHER"] = "y" },
            args: "--prefix APP_"
        );

        exitCode.ShouldBe(0);
        var json = JsonNode.Parse(stdout).ShouldBeOfType<JsonObject>();
        json.ContainsKey("FOO").ShouldBeTrue();
        json.ContainsKey("OTHER").ShouldBeFalse();
        json.ContainsKey("APP_FOO").ShouldBeFalse();
    }

    [Fact]
    public void NoSchema_ExitCodeZero_ValidJson()
    {
        var (exitCode, stdout, _) = RunCli(
            new Dictionary<string, string> { ["MYKEY"] = "myval" }
        );

        exitCode.ShouldBe(0);
        var json = JsonNode.Parse(stdout).ShouldBeOfType<JsonObject>();
        json["MYKEY"].ShouldNotBeNull().GetValue<string>().ShouldBe("myval");
    }

    [Fact]
    public void SchemaValid_ExitCodeZero_StderrEmpty()
    {
        var schemaPath = WriteSchemaFile("""{"type":"object","properties":{"PORT":{"type":"string"}}}""");
        try
        {
            var (exitCode, stdout, stderr) = RunCli(
                new Dictionary<string, string> { ["PORT"] = "8080" },
                args: $"--schema \"{schemaPath}\""
            );

            exitCode.ShouldBe(0);
            JsonNode.Parse(stdout).ShouldNotBeNull();
            stderr.Trim().ShouldBeEmpty();
        }
        finally { File.Delete(schemaPath); }
    }

    [Fact]
    public void SchemaInvalid_ExitCodeOne_ErrorOnStderr_JsonOnStdout()
    {
        var schemaPath = WriteSchemaFile("""{"type":"object","required":["MISSING_KEY"]}""");
        try
        {
            var (exitCode, stdout, stderr) = RunCli(
                new Dictionary<string, string> { ["PORT"] = "8080" },
                args: $"--schema \"{schemaPath}\""
            );

            exitCode.ShouldBe(1);
            stderr.ShouldContain("Schema validation errors");
            JsonNode.Parse(stdout).ShouldNotBeNull();
        }
        finally { File.Delete(schemaPath); }
    }

    [Fact]
    public void SchemaFileNotFound_TreatedAsNoSchema_ExitCodeZero()
    {
        var (exitCode, stdout, _) = RunCli(
            new Dictionary<string, string> { ["A"] = "1" },
            args: "--schema /nonexistent/path/schema.json"
        );

        exitCode.ShouldBe(0);
        JsonNode.Parse(stdout).ShouldNotBeNull();
    }

    [Fact]
    public void PrefixSet_AdditionalPropertiesNotRemoved()
    {
        var schemaPath = WriteSchemaFile("""{"type":"object","properties":{"KNOWN":{"type":"string"}},"additionalProperties":false}""");
        try
        {
            // With --prefix set, RemoveAdditionalProperties is skipped; EXTRA should survive
            var (exitCode, stdout, _) = RunCli(
                new Dictionary<string, string> { ["APP_KNOWN"] = "a", ["APP_EXTRA"] = "b" },
                args: $"--prefix APP_ --schema \"{schemaPath}\""
            );

            // Validation will fail because EXTRA is additional, but the key should still be present in stdout JSON
            var json = JsonNode.Parse(stdout).ShouldBeOfType<JsonObject>();
            json.ContainsKey("EXTRA").ShouldBeTrue();
        }
        finally { File.Delete(schemaPath); }
    }
}
