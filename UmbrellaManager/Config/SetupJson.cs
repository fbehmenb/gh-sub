using System.Text.Json.Serialization;

namespace UmbrellaManager.Config;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(UserSetup))]
internal sealed partial class SetupJson : JsonSerializerContext;
