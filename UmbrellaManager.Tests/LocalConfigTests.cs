using UmbrellaManager.Config;

namespace UmbrellaManager.Tests;

public class LocalConfigTests
{
    [Fact]
    public void Retired_pin_title_becomes_the_current_default()
    {
        var root = Path.Combine(Path.GetTempPath(), "umbrella-manager-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, LocalConfig.FileName);
        try
        {
            File.WriteAllText(path, """{ "pinTitle": "[REL] <modul>: <version>" }""");
            var (config, warning) = LocalConfig.Load(root, "", "");
            Assert.Null(warning);
            Assert.Equal(LocalConfig.DefaultPinTitle, config.PinTitle);
            Assert.Equal("chore/pin-*", config.HiddenBranchPattern);
            Assert.Contains("[REL] <modul>: <version>", File.ReadAllText(path));

            File.WriteAllText(path, """{ "pinTitle": "chore(<modul>): pin" }""");
            var (custom, customWarning) = LocalConfig.Load(root, "", "");
            Assert.Null(customWarning);
            Assert.Equal("chore(<modul>): pin", custom.PinTitle);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
