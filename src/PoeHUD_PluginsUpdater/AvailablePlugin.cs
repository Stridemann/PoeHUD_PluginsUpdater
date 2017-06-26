using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PoeHUD_PluginsUpdater
{
    public class AvailablePlugin
    {
        public string PluginName;
        public string GitName;
        public string GitOwner;
        public string GitConfigURL;
        public string Description = "No description to this plugin";
        public bool bOwned;
        public bool bInstaled;
        public PluginToUpdate InstalledPlugin;
    }
}
