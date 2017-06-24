using PoeHUD.Hud.Settings;
using PoeHUD.Plugins;

namespace PoeHUD_PluginsUpdater
{
    public class PoeHUD_PluginsUpdater_Settings : SettingsBase
    {
        public PoeHUD_PluginsUpdater_Settings()
        {
            Enable = false;
        }

        public float WindowPosX = 300;
        public float WindowPosY = 400;
        public string GitToken = "e09ffe5a61019d8726e700bb4dafaf546c36a939";
    }
}
