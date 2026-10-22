using System.Text.Json;

namespace UmbrellaManager.Config;

public sealed record LocalConfig(string HiddenBranchPattern)
{
    public const string FileName = "umbrella-manager.local.json";

    public static LocalConfig Default { get; } = new("chore/pin-*");

    public static (LocalConfig Config, string? Warning) Load(string umbrellaPath, string baseDirectory, string workingDirectory)
    {
        foreach (var directory in new[] { umbrellaPath, workingDirectory, baseDirectory })
        {
            if (string.IsNullOrEmpty(directory))
            {
                continue;
            }

            var path = Path.Combine(directory, FileName);
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                if (document.RootElement.TryGetProperty("hiddenBranchPattern", out var value)
                    && value.ValueKind == JsonValueKind.String
                    && value.GetString() is { Length: > 0 } pattern)
                {
                    return (new LocalConfig(pattern), null);
                }

                return (Default, null);
            }
            catch (JsonException)
            {
                return (Default, $"Konfiguration {FileName} ist ungültig. Es gilt das Muster {Default.HiddenBranchPattern}.");
            }
        }

        return (Default, null);
    }
}
