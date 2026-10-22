using System.Text.Json;

namespace UmbrellaManager.Config;

public sealed record UserSetup(string RepositoryUrl, string LocalPath)
{
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
}
