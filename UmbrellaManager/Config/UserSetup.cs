using System.Text.Json;

namespace UmbrellaManager.Config;

public sealed record UserSetup(string RepositoryUrl, string LocalPath, int? PollIntervalSeconds = null)
{
    public const int DefaultPollSeconds = 120;

    public static readonly int[] PollSteps = [0, 30, 60, 120, 300, 900];

    public int EffectivePollSeconds => Normalize(PollIntervalSeconds);

    public static string DefaultFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "umbrella-manager-local",
        "setup.json");

    public static UserSetup? Load(string filePath, out string? warning)
    {
        warning = null;
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var setup = JsonSerializer.Deserialize(File.ReadAllText(filePath), SetupJson.Default.UserSetup);
            if (setup is null
                || string.IsNullOrWhiteSpace(setup.RepositoryUrl)
                || string.IsNullOrWhiteSpace(setup.LocalPath))
            {
                warning = "Die gespeicherte Einrichtung ist ungültig.";
                return null;
            }

            return setup;
        }
        catch (JsonException)
        {
            warning = "Die gespeicherte Einrichtung ist ungültig.";
            return null;
        }
    }

    public void Save(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(filePath, JsonSerializer.Serialize(this, SetupJson.Default.UserSetup));
    }

    public static int Normalize(int? seconds)
    {
        if (seconds is null)
        {
            return DefaultPollSeconds;
        }

        return PollSteps.Contains(seconds.Value) ? seconds.Value : DefaultPollSeconds;
    }

    public static int PollIndex(int? seconds)
    {
        var index = Array.IndexOf(PollSteps, Normalize(seconds));
        return index < 0 ? Array.IndexOf(PollSteps, DefaultPollSeconds) : index;
    }

    public static string PollLabel(int seconds)
    {
        return seconds switch
        {
            0 => "Aus",
            30 => "30 Sekunden",
            60 => "1 Minute",
            120 => "2 Minuten",
            300 => "5 Minuten",
            900 => "15 Minuten",
            _ => seconds + " Sekunden",
        };
    }
}
