using PoeHUD.Hud.Settings;
using PoeHUD.Plugins;
using System;
using System.Text;

namespace PoeHUD_PluginsUpdater
{
    public class PoeHUD_PluginsUpdater_Settings : SettingsBase
    {
        public PoeHUD_PluginsUpdater_Settings()
        {
            Enable = true;

            byte[] data = FromHex("35653761386136303863656464303033343263333435323430306236636434373965323638373630");
            GitToken = Encoding.ASCII.GetString(data);
        }
        public static byte[] FromHex(string hex)
        {
            byte[] raw = new byte[hex.Length / 2];
            for (int i = 0; i < raw.Length; i++)
            {
                raw[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return raw;
        }

        public float WindowPosX = 300;
        public float WindowPosY = 400;
        public string GitToken;//User can use his own personal token
        public string PoeHUDBranch = "x64";
    }
}
