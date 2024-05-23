using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EnvToJson.Tests;

public static class KeyVal
{
    public static KeyValuePair<string, T?> Build<T>(string key, T? value) =>
        new KeyValuePair<string, T?>(key, value);
}

public static class ConfigKeyVal
{
    public static KeyValuePair<string, string?> From<T>(KeyValuePair<string, T?> keyVal) =>
        new KeyValuePair<string, string?>(keyVal.Key.Replace("__", ":"), keyVal.Value?.ToString());
}

public static class InMemoryConfiguration
{
    public static IConfigurationRoot Build(params KeyValuePair<string, string?>[] keyVals)
    {
        var configurationKeyVals = keyVals
            .OrderBy(_ => Random.Shared.Next())
            .ToList();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationKeyVals)
            .Build();

        return configuration;
    }
}