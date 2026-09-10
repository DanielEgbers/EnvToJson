# EnvToJson

A CLI tool that reads environment variables and writes them as JSON to stdout.

Nesting is expressed with `__` as the separator (the same convention as [ASP.NET Core configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/#environment-variables)). Keys that are entirely numeric become array indices.

## Usage

```
EnvToJson [--prefix <prefix>] [--schema <schema.json>]
```

| Option | Description |
|--------|-------------|
| `--prefix` | Only include variables whose name starts with this prefix. The prefix is stripped from the key names in the output. |
| `--schema` | Path to a JSON Schema file. Used for type coercion, additional-property removal, and validation. |

## Examples

### Basic

```sh
FOO=hello BAR=world EnvToJson
```
```json
{
  "FOO": "hello",
  "BAR": "world"
}
```

### Nested objects

Use `__` to separate levels:

```sh
APP__DB__HOST=localhost APP__DB__PORT=5432 EnvToJson
```
```json
{
  "APP": {
    "DB": {
      "HOST": "localhost",
      "PORT": "5432"
    }
  }
}
```

### Arrays

Numeric keys become array indices. Non-numeric keys at the same level become additional elements at the end:

```sh
ITEMS__0=a ITEMS__1=b ITEMS__2=c EnvToJson
```
```json
{
  "ITEMS": ["a", "b", "c"]
}
```

### Prefix filter

Use `--prefix` to scope to a set of variables and strip the prefix from the output:

```sh
APP__HOST=localhost APP__PORT=5432 OTHER=ignored EnvToJson --prefix APP__
```
```json
{
  "HOST": "localhost",
  "PORT": "5432"
}
```

### JSON Schema

Pass a JSON Schema with `--schema` to:

- **Coerce types** — environment variables are always strings; the schema tells EnvToJson to convert them to numbers, booleans, or arrays.
- **Remove additional properties** — when the schema root has `"additionalProperties": false` and no prefix is used, unrecognised keys are silently dropped before validation.
- **Validate** — if the resulting JSON does not satisfy the schema, EnvToJson exits with code `1` and prints the validation errors to stderr.

```sh
PORT=8080 DEBUG=true EnvToJson --schema schema.json
```

`schema.json`:
```json
{
  "type": "object",
  "properties": {
    "PORT":  { "type": "integer" },
    "DEBUG": { "type": "boolean" }
  }
}
```

Output:
```json
{
  "PORT": 8080,
  "DEBUG": true
}
```

### Null values

Set a variable to the literal string `null` to emit a JSON `null`:

```sh
OPTIONAL=null EnvToJson
```
```json
{
  "OPTIONAL": null
}
```

## Download

```sh
ARCH=$(uname -m)
if [ -f /etc/alpine-release ] || ldd --version 2>&1 | grep -q musl; then
    VARIANT="${ARCH}-musl"
else
    VARIANT="${ARCH}"
fi

VERSION=$(curl -fsSL https://api.github.com/repos/DanielEgbers/EnvToJson/releases/latest | grep '"tag_name"' | cut -d '"' -f 4)
curl -fsSL -o EnvToJson.zip "https://github.com/DanielEgbers/EnvToJson/releases/download/${VERSION}/EnvToJson-linux-${VARIANT}.zip"
unzip -o EnvToJson.zip
```

Supported variants: `x86_64`, `x86_64-musl`, `aarch64`, `aarch64-musl`

## License

[MIT](LICENSE.txt)
