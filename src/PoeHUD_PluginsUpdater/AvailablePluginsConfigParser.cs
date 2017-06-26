using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Octokit;
using PoeHUD.Framework;
using PoeHUD.Hud;
using PoeHUD.Hud.Menu;
using PoeHUD.Hud.PluginExtension;
using PoeHUD.Plugins;
using SharpDX;
using SharpDX.Direct3D9;
using PoeHUD.Hud.UI;


namespace PoeHUD_PluginsUpdater
{
    public class AvailablePluginsConfigParser
    {
        public const string AvailablePluginsConfigFile = "AvailablePluginsConfig.txt";
        private const string KEYWORD_PLUGINNAME = "Plugin:";
        private const string KEYWORD_GITNAME = "GitName:";
        private const string KEYWORD_GITOWNER = "GitOwner:";
        private const string KEYWORD_GITURL = "ConfigURL:";
        private const string KEYWORD_DESCRIPTION = "Desctription:";


        public static List<AvailablePlugin> Parse(string pluginDirectory)
        {
            List<AvailablePlugin> AllAvailablePlugins = null;
            var allPluginsConfig = Path.Combine(pluginDirectory, AvailablePluginsConfigFile);

            if (!File.Exists(allPluginsConfig))
            {
                return AllAvailablePlugins;
            }
            AllAvailablePlugins = new List<AvailablePlugin>();

            var cfgLines = File.ReadAllLines(allPluginsConfig);

            AvailablePlugin currentPlugin = null;
            for (int i = 0; i < cfgLines.Length; i++)
            {
                var line = cfgLines[i];
                if (line.Replace(" ", "").Length == 0 || line.StartsWith("//") || line.StartsWith("#")) continue;

                TrimName(ref line);

                int pluginNameIndex = line.IndexOf(KEYWORD_PLUGINNAME);
                if (pluginNameIndex != -1)
                {
                    CheckAddPlugin(AllAvailablePlugins, currentPlugin, i);
                    currentPlugin = new AvailablePlugin();
                    currentPlugin.PluginName = line.Substring(pluginNameIndex + KEYWORD_PLUGINNAME.Length);
                    TrimName(ref currentPlugin.PluginName);
                    continue;
                }

                int gitNameIndex = line.IndexOf(KEYWORD_GITNAME);
                if (gitNameIndex != -1)
                {
                    currentPlugin.GitName = line.Substring(gitNameIndex + KEYWORD_GITNAME.Length);
                    TrimName(ref currentPlugin.GitName);
                    continue;
                }

                int gitOwnerIndex = line.IndexOf(KEYWORD_GITOWNER);
                if (gitOwnerIndex != -1)
                {
                    currentPlugin.GitOwner = line.Substring(gitOwnerIndex + KEYWORD_GITOWNER.Length);
                    TrimName(ref currentPlugin.GitOwner);
                    continue;
                }

                int gitConfigUrlIndex = line.IndexOf(KEYWORD_GITURL);
                if (gitConfigUrlIndex != -1)
                {
                    currentPlugin.GitConfigURL = line.Substring(gitConfigUrlIndex + KEYWORD_GITURL.Length);
                    TrimName(ref currentPlugin.GitConfigURL);
                    continue;
                }

                int descriptionlIndex = line.IndexOf(KEYWORD_DESCRIPTION);
                if (descriptionlIndex != -1)
                {
                    currentPlugin.Description = line.Substring(descriptionlIndex + KEYWORD_DESCRIPTION.Length);
                    TrimName(ref currentPlugin.Description);
                    continue;
                }
            }
            CheckAddPlugin(AllAvailablePlugins, currentPlugin, cfgLines.Length - 1);


            var pluginsDir = @"plugins\";
            var pluginDirs = (new DirectoryInfo(pluginsDir)).GetDirectories().Select(x => x.FullName).ToList();
            AllAvailablePlugins.ForEach(x => x.bOwned = pluginDirs.Any(y => y.Contains(pluginsDir + x.PluginName)));

            return AllAvailablePlugins;
        }

        private static void CheckAddPlugin(List<AvailablePlugin> AllAvailablePlugins, AvailablePlugin currentPlugin, int i)
        {
            if (currentPlugin != null)
            {
                if (string.IsNullOrEmpty(currentPlugin.PluginName))
                {
                    BasePlugin.LogError($"AvailablePluginsConfigParser: Empty plugin name! (Under line: {i - 1})", 10);
                }
                else if (string.IsNullOrEmpty(currentPlugin.GitName))
                {
                    BasePlugin.LogError($"AvailablePluginsConfigParser: No parameter GitName in plugin: {currentPlugin.PluginName}", 10);
                }
                else if (string.IsNullOrEmpty(currentPlugin.GitOwner))
                {
                    BasePlugin.LogError($"AvailablePluginsConfigParser: No parameter GitOwner in plugin: {currentPlugin.PluginName}", 10);
                }
                else if (string.IsNullOrEmpty(currentPlugin.GitConfigURL))
                {
                    BasePlugin.LogError($"AvailablePluginsConfigParser: No parameter GitConfigURL in plugin: {currentPlugin.PluginName}", 10);
                }
                else
                {
                    AllAvailablePlugins.Add(currentPlugin);
                }
            }
        }

        private static void TrimName(ref string name)
        {
            name = name.TrimEnd(' ');
            name = name.TrimStart(' ');
        }
    }
}