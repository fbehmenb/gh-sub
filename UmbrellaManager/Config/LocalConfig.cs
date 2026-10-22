using System.Text.Json;

namespace UmbrellaManager.Config;

public sealed record LocalConfig(string HiddenBranchPattern, string PinTitle)
{
    public const string FileName = "umbrella-manager.local.json";

    public const string DefaultPinTitle = "chore(<modul>): pin auf <sha7> (<modul-branch>)";

    public static LocalConfig Default { get; } = new("chore/pin-*", DefaultPinTitle);

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
                var pattern = ReadString(document.RootElement, "hiddenBranchPattern") ?? Default.HiddenBranchPattern;
                var title = ReadString(document.RootElement, "pinTitle") ?? Default.PinTitle;
                return (new LocalConfig(pattern, title), null);
            }
            catch (JsonException)
            {
                return (Default, $"Konfiguration {FileName} ist ungültig. Es gilt das Muster {Default.HiddenBranchPattern}.");
            }
        }

        return (Default, null);
    }

    private static string? ReadString(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            && value.GetString() is { Length: > 0 } text)
        {
            return text;
        }

        return null;
    }
}
