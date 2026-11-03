namespace UmbrellaManager.Config;

public static class SessionLaunch
{
    public static bool IsExtension(string? ghExtension) => ghExtension == "1";

    public static string? ArgumentPath(IReadOnlyList<string> args)
    {
        foreach (var arg in args)
        {
            if (arg.Length == 0 || arg.StartsWith('-'))
            {
                continue;
            }

            try
            {
                return Path.GetFullPath(arg);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                return null;
            }
        }

        return null;
    }
}
