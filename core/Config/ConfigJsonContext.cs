using System.Text.Json.Serialization;

namespace Diagnyx.Core.Config;

[JsonSerializable(typeof(DiagnyxConfig))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false)]
internal partial class ConfigJsonContext : JsonSerializerContext { }
