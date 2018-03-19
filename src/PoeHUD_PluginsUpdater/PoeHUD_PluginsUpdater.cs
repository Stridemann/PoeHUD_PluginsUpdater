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
using System.Net;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Gma.System.MouseKeyHook;
using ImGuiNET;
using ImGuiVector2 = System.Numerics.Vector2;
using ImGuiVector4 = System.Numerics.Vector4;

namespace PoeHUD_PluginsUpdater
{
    public class PoeHUD_PluginsUpdater : BaseSettingsPlugin<PoeHUD_PluginsUpdater_Settings>
    {
        public static Graphics UGraphics;

        public PoeHUD_PluginsUpdater()
        {
            PluginName = "PluginsUpdater";
            CanPluginBeEnabledInOptions = false;
        }
        private const float WindowWidth = 700;

        public const string VersionFileName = "%PluginVersion.txt";
        public const string UpdateTempDir = "%PluginUpdate%";//Do not change this value. Otherwice this value in PoeHUD should be also changed.
        private const string PoehudHashFile = "Updator_PoeHUDHash";
        private string[] PoeHUDBranches = new string[] { "x64", "Garena_x64" };


        private List<PluginToUpdate> AllPlugins = new List<PluginToUpdate>();

        private UWindowTab CurrentWindowTab = UWindowTab.InstalledPlugins;

        private List<AvailablePlugin> AllAvailablePlugins = null;

        private RectangleF DrawRect;
        private bool InitOnce;
        private bool bMouse_Drag;
        public static bool bMouse_Click;
        public static Vector2 Mouse_ClickPos;
        public static Vector2 Mouse_DragDelta;
        public static Vector2 Mouse_Pos;
        public static Vector2 Mouse_StartDragPos;

        private int RepoFilesCheckedCount;

        private float StartDragWinPosX;
        private float StartDragWinPosY;

        private float WindowHeight;

        private GitHubClient gitClient;


        private bool InitializeOnce;
        public override void OnPluginSelectedInMenu()
        {
            if (InitializeOnce) return;
            InitializeOnce = true;

            ForceInitialize();
        }

        private void ForceInitialize()
        {
            gitClient = new GitHubClient(new ProductHeaderValue("PoeHUDPluginsUpdater"));

            UGraphics = Graphics;
            AllAvailablePlugins = AvailablePluginsConfigParser.Parse(PluginDirectory);

            MenuPlugin.KeyboardMouseEvents.MouseDownExt += KeyboardMouseEvents_MouseDownExt;
            MenuPlugin.KeyboardMouseEvents.MouseUpExt += KeyboardMouseEvents_MouseUpExt;
            MenuPlugin.KeyboardMouseEvents.MouseMoveExt += KeyboardMouseEvents_MouseMove;


            AllPlugins = new List<PluginToUpdate>();

            foreach (var plugin in PluginExtensionPlugin.Plugins)
            {
                AddPlugin(plugin.PluginName, plugin.PluginDirectory);
            }

            CheckAddPluginsByConfig();

            AllPlugins = AllPlugins.OrderByDescending(x => x.UpdateVariant).ToList();

            AddPoeHudPlugin();

            CheckUpdates();
        }

        private void KeyboardMouseEvents_MouseMove(object sender, MouseEventExtArgs e)
        {
            if (!Settings.Enable) return;

            Mouse_Pos = GameController.Window.ScreenToClient(e.X, e.Y);
            var clientRect = GameController.Window.GetWindowRectangle();

            if (bMouse_Drag)
            {
                Mouse_DragDelta = Mouse_Pos - Mouse_StartDragPos;

                if (Settings.WindowPosX < 0)
                    Settings.WindowPosX = 0;
                else if (Settings.WindowPosX + WindowWidth > clientRect.Width)
                    Settings.WindowPosX = clientRect.Width - WindowWidth;
                else
                    Settings.WindowPosX = StartDragWinPosX + Mouse_DragDelta.X;

                if (Settings.WindowPosY < 0)
                    Settings.WindowPosY = 0;
                else if (Settings.WindowPosY + WindowHeight > clientRect.Height)
                    Settings.WindowPosY = clientRect.Height - WindowHeight;
                else
                    Settings.WindowPosY = StartDragWinPosY + Mouse_DragDelta.Y;
            }
        }

        private void KeyboardMouseEvents_MouseUpExt(object sender, MouseEventExtArgs e)
        {
            if (!Settings.Enable) return;

            if (e.Button == MouseButtons.Left)
            {
                if (bMouse_Drag)
                {
                    bMouse_Drag = false;
                    e.Handled = true;
                }
            }
        }

        private void KeyboardMouseEvents_MouseDownExt(object sender, MouseEventExtArgs e)
        {
            if (!Settings.Enable) return;

            if (e.Button == MouseButtons.Left)
            {
                var position = GameController.Window.ScreenToClient(e.X, e.Y);
                if (DrawRect.Contains(position))
                {
                    Mouse_ClickPos = position;
                    bMouse_Click = true;

                    bMouse_Drag = true;
                    Mouse_StartDragPos = position;
                    StartDragWinPosX = Settings.WindowPosX;
                    StartDragWinPosY = Settings.WindowPosY;
                    e.Handled = true;
                }
            }
        }



        private void AddPoeHudPlugin()
        {
            var poehudExePath = Assembly.GetEntryAssembly().Location;
            var poehudExeLocation = Path.GetDirectoryName(poehudExePath);
            var poehudExeName = Path.GetFileName(poehudExePath);

            var plugVariant = new PluginToUpdate
            {
                RepoOwner = "TehCheat",
                RepoName = "PoEHUD",
                PluginName = "PoeHUD",
                PluginDirectory = poehudExeLocation,//\src\bin\x64\Debug\plugins\PoeHUD
                bAllowCheckUpdate = true,
                bHasGitConfig = true,
                BranchName = Settings.PoeHUDBranch,
                UpdateVariant = ePluginSourceOfUpdate.RepoBranch,
                UpdateState = ePluginUpdateState.ReadyToInstal,
                IgnoredEntities = new List<string>() { "src", ".gitattributes", ".gitignore", "README.md", "shortcut.bat" },
                IsPoeHUD = true,
                PoehudExeRealName = poehudExeName
            };

            plugVariant.DoAfterUpdate += ProcessPoehudUpdate;

            AllPlugins.Insert(0, plugVariant);
        }

        private void ProcessPoehudUpdate(PluginToUpdate plugin)
        {
            var poehudExePath = Assembly.GetEntryAssembly().Location;
            var poehudExeName = Path.GetFileName(poehudExePath);
            var poehudExeLocation = Path.GetDirectoryName(poehudExePath);
            string exeToDelete = "-";

            string exeToStart = poehudExeName;

            var newExe = plugin.FilesToDownload.Find(x => x.Path.EndsWith("PoeHUD.exe"));
            if (newExe != null)
            {
                exeToStart = Path.GetFileName(newExe.Path);
                exeToDelete = poehudExePath;
            }

            var poeHudUpdateFilesDir = Path.Combine(poehudExeLocation, UpdateTempDir);

            var poeProcess = Process.GetCurrentProcess();

            string updaterFilePath = Path.Combine(PluginDirectory, "PoeHUDUpdater.exe");

            if (!File.Exists(updaterFilePath))
            {
                LogError("Can't find PoeHUDUpdater.exe in PoeHUD_PluginsUpdater folder to update PoeHUD!", 10);
                return;
            }

            if (newExe != null)
            {
                var hudHashFilePath = Path.Combine(poehudExeLocation, PoehudHashFile);
                File.WriteAllText(hudHashFilePath, newExe.Sha);
            }

            var psi = new ProcessStartInfo();
            psi.CreateNoWindow = true; //This hides the dos-style black window that the command prompt usually shows
            psi.FileName = @"cmd.exe";
            psi.Verb = "runas"; //this is what actually runs the command as administrator
            psi.Arguments = $"/C {updaterFilePath} {poeHudUpdateFilesDir} {poehudExeLocation} {poeProcess.Id} {Path.Combine(poehudExeLocation, exeToStart)} {exeToDelete}";
            try
            {
                var process = new Process();
                process.StartInfo = psi;
                process.Start();
                process.WaitForExit();
            }
            catch (Exception)
            {
                LogError("PoeHUD Updater: Can't start PoeUpdater.exe with admin rights", 10);
                //If you are here the user clicked declined to grant admin privileges (or he's not administrator)
            }

      
        }


        private PluginToUpdate CheckAddPluginsByConfig()
        {
            PluginToUpdate result = null;

            var PluginsDir = new DirectoryInfo("plugins");
            foreach (var pluginDirectoryInfo in PluginsDir.GetDirectories())
            {
                var gitConfigFilePath = Path.Combine(pluginDirectoryInfo.FullName, "GitUpdateConfig.txt");

                if (File.Exists(gitConfigFilePath))
                {
                    if (
                        !AllPlugins.Any(
                            x =>
                                string.Equals(x.PluginDirectory, pluginDirectoryInfo.FullName,
                                    StringComparison.OrdinalIgnoreCase)))
                    {
                        var pluginFolderName = Path.GetFileName(pluginDirectoryInfo.FullName);
                        result = AddPlugin(pluginFolderName, pluginDirectoryInfo.FullName);
                    }
                }
            }
            return result;
        }

        private PluginToUpdate AddPlugin(string pluginName, string pluginDirectory)
        {
            var plugVariant = new PluginToUpdate
            {
                PluginName = pluginName,
                PluginDirectory = pluginDirectory
            };
            AllPlugins.Add(plugVariant);
            GitConfigParser.Parse(plugVariant);
            return plugVariant;
        }


        private async void CheckUpdates()
        {
            gitClient.Credentials = new Credentials(Settings.GitToken);

            foreach (var plugin in AllPlugins.ToList())
            {
                await CheckPluginUpdate(plugin);
            }
        }


        private async Task CheckPluginUpdate(PluginToUpdate plugin)
        {
            plugin.LocalVersion = "Undefined";
            plugin.RemoteVersion = "Undefined";

            if (!plugin.bAllowCheckUpdate)
                return;

            var versionFilePath = Path.Combine(plugin.PluginDirectory, VersionFileName);

            if (File.Exists(versionFilePath))
            {
                var lovalVersionLines = File.ReadAllLines(versionFilePath);
                plugin.LocalVersion = lovalVersionLines[0];
                if (lovalVersionLines.Length > 1)
                    plugin.LocalTag = lovalVersionLines[1];
            }



            if (plugin.UpdateVariant == ePluginSourceOfUpdate.Release) //Release
            {
                #region ReleaseCheck

                IReadOnlyList<Release> releases = null;

                try
                {
                    releases = await gitClient.Repository.Release.GetAll(plugin.RepoOwner, plugin.RepoName);
                }
                catch (NotFoundException notFoundEx)
                {
                    plugin.UpdateState = ePluginUpdateState.WrongConfig;
                    LogError($"Plugin '" + plugin.PluginName + "' check update error: " + notFoundEx.Message, 10);
                    return;
                }
                catch (Exception ex)
                {
                    plugin.UpdateState = ePluginUpdateState.WrongConfig;
                    LogError($"Plugin '" + plugin.PluginName + "' check update unhandled error: " + ex.Message, 10);
                    return;
                }

                foreach (var release in releases)
                {
                    if (plugin.bCustomTag && !Regex.Match(release.TagName, plugin.ReleaseRegexTag).Success)
                    {
                        //LogMessage("Fail match:  " + plugin.ReleaseRegexTag + "  " + release.TagName, 10);
                        continue;
                    }
                    //LogMessage("Pass match:  " + plugin.ReleaseRegexTag + "  " + release.TagName, 10);

                    foreach (var asset in release.Assets)
                    {
                        if (asset.Name.EndsWith(".zip"))
                        {
                            plugin.RemoteVersion = release.PublishedAt?.ToString("dd.MM.yy, hh:mm");
                            plugin.RemoteTag = release.TagName;


                            if (plugin.LocalVersion == plugin.RemoteVersion)
                                plugin.UpdateState = ePluginUpdateState.NoUpdate;
                            else if (plugin.LocalVersion == "Undefined")
                            {
                                plugin.UpdateState = ePluginUpdateState.UnknownUpdate;
                            }
                            else
                            {
                                plugin.UpdateState = ePluginUpdateState.Undefined;

                                DateTimeOffset localParseResult;
                                if (DateTimeOffset.TryParse(plugin.LocalVersion, out localParseResult))
                                {
                                    DateTimeOffset remoteParseResult;
                                    if (DateTimeOffset.TryParse(plugin.RemoteVersion, out remoteParseResult))
                                    {
                                        if (remoteParseResult > localParseResult)
                                            plugin.UpdateState = ePluginUpdateState.HasUpdate;
                                        else if (remoteParseResult < localParseResult)
                                            plugin.UpdateState = ePluginUpdateState.HasLowerUpdate;
                                    }
                                }
                            }

                            var updateDirectory = Path.Combine(plugin.PluginDirectory, UpdateTempDir, asset.Name);
                            plugin.FilesToDownload.Add(new FileToDownload
                            {
                                Name = asset.Name,
                                Url = asset.BrowserDownloadUrl,
                                Path = updateDirectory
                            });
                            break;
                        }
                    }
                    if (plugin.FilesToDownload.Count > 0)
                        break;
                }

                #endregion
            }
            else if (plugin.UpdateVariant == ePluginSourceOfUpdate.RepoBranch) //Branch
            {
                plugin.UpdateState = ePluginUpdateState.NoUpdate;
                RepoFilesCheckedCount = 0;

                await CheckFiles(plugin, plugin.BranchName, "");

                if (plugin.FilesToDownload.Count > 0)
                {
                    plugin.UpdateState = ePluginUpdateState.HasUpdate;
                    plugin.RemoteVersion = plugin.FilesToDownload.Count + " files changed";
                }
                else
                {
                    plugin.RemoteVersion = "No changes";
                }
                plugin.LocalVersion = RepoFilesCheckedCount + " files checked";
            }
        }

        private async Task CheckFiles(PluginToUpdate plugin, string branch, string path)
        {
            IReadOnlyList<RepositoryContent> allContent = null;

            try
            {
                if (!string.IsNullOrEmpty(branch))
                {
                    if (!string.IsNullOrEmpty(path))
                        allContent = await gitClient.Repository.Content.GetAllContentsByRef(plugin.RepoOwner, plugin.RepoName, path, branch);
                    else
                        allContent = await gitClient.Repository.Content.GetAllContentsByRef(plugin.RepoOwner, plugin.RepoName, branch);
                }
                else
                {
                    if (!string.IsNullOrEmpty(path))
                        allContent = await gitClient.Repository.Content.GetAllContents(plugin.RepoOwner, plugin.RepoName, path);
                    else
                        allContent = await gitClient.Repository.Content.GetAllContents(plugin.RepoOwner, plugin.RepoName);
                }
            }
            catch (Exception ex)
            {
                LogError($"Plugin '" + plugin.PluginName + "' check update unhandled error: " + ex.Message, 10);
                return;
            }


            foreach (var contentEntity in allContent)
            {
                if (plugin.IgnoredEntities.Contains(contentEntity.Path)) continue;

                if (contentEntity.Type == ContentType.File)
                {
                    RepoFilesCheckedCount++;
                    var download = false;


                    if (plugin.IsPoeHUD && contentEntity.Name.Contains("PoeHUD.exe"))
                    {
                        var hudHashFile = Path.Combine(plugin.PluginDirectory + @"\", PoehudHashFile);//plugin.PoehudExeRealName

                        download = true;
                        if (File.Exists(hudHashFile))
                        {
                            var hashStr = File.ReadAllText(hudHashFile);
                            if (contentEntity.Sha == hashStr)
                            {
                                download = false;
                            }
                        }
                    }
                    else
                    {
                        var localPath = Path.Combine(plugin.PluginDirectory + @"\" + path, contentEntity.Name);
                        if (plugin.IsPoeHUD && path.Contains("Release"))
                            localPath = Path.Combine(plugin.PluginDirectory + @"\" + path.Remove(path.IndexOf("Release"), 7), contentEntity.Name);

                        if (File.Exists(localPath))
                        {
                            var fileSha = UpdaterUtils.GetGitObjectChecksum(localPath);

                            if (fileSha != contentEntity.Sha)
                            {
                                download = true;
                            }
                        }
                        else
                        {
                            download = true;
                        }
                    }

                    if (download)
                    {
                        var updateFilePath = Path.Combine(plugin.PluginDirectory + @"\" + UpdateTempDir + @"\" + path, contentEntity.Name);
                        if (plugin.IsPoeHUD && path.Contains("Release"))
                            updateFilePath = Path.Combine(plugin.PluginDirectory + @"\" + UpdateTempDir + @"\" + path.Remove(path.IndexOf("Release"), 7), contentEntity.Name);

                        plugin.FilesToDownload.Add(new FileToDownload
                        {
                            Name = contentEntity.Name,
                            Url = contentEntity.DownloadUrl.AbsoluteUri,
                            Path = updateFilePath,
                            Sha = contentEntity.Sha
                        });
                    }
                }
                else if (contentEntity.Type == ContentType.Dir)
                {
                    var newPath = Path.Combine(path, contentEntity.Name);
                    await CheckFiles(plugin, branch, newPath);
                }
                else
                {
                    LogMessage("Ignore update content type: " + contentEntity.Type, 10);
                }
            }
        }

        public override void InitializeSettingsMenu() { }

        private int UniqIdCounter;
        private string UniqId => $"##{UniqIdCounter++}";

        private string testText = "";
        public override void DrawSettingsMenu()
        {
            UniqIdCounter = 0;

            #region Tabs buttons
            ImGuiNative.igGetContentRegionAvail(out var newcontentRegionArea);
            ImGui.BeginChild($"{UniqId}", new ImGuiVector2(newcontentRegionArea.X, 40), true, WindowFlags.Default);

            if (ImGuiExtension.Button($"Installed Plugins{UniqId}"))
                CurrentWindowTab = UWindowTab.InstalledPlugins;
            ImGui.SameLine();
            if (ImGuiExtension.Button($"Available Plugins{UniqId}"))
                CurrentWindowTab = UWindowTab.AvailablePlugins;
            ImGui.EndChild();
            #endregion

            var style = ImGui.GetStyle();
            var textColorBack = style.GetColor(ColorTarget.Text);

            if (CurrentWindowTab == UWindowTab.InstalledPlugins)
            {
                #region InstalledPlugins
                ImGuiNative.igGetContentRegionAvail(out newcontentRegionArea);
                ImGui.BeginChild($"{UniqId}", new ImGuiVector2(newcontentRegionArea.X, newcontentRegionArea.Y), true, WindowFlags.Default);
            
                ImGui.Columns(4, UniqId, true);
                ImGui.SetColumnWidth(0, 180);
                ImGui.SetColumnOffset(2, newcontentRegionArea.X - 400);
                ImGui.SetColumnOffset(3, newcontentRegionArea.X - 200);

                ImGui.Text($"Plugin name");
                ImGui.NextColumn();
                ImGui.Text($"Local version");
                ImGui.NextColumn();
                ImGui.Text($"Remote version");
                ImGui.NextColumn();
                ImGui.Text($"State");

                ImGui.Separator();
                ImGui.NextColumn();
                
                foreach (var plug in AllPlugins.ToList())
                {
                    ImGui.Text(plug.PluginName);

                    if(plug.IsPoeHUD)
                    {
                        var x64 = PoeHUDBranches[0];
                        if (ImGui.RadioButtonBool(x64, Settings.PoeHUDBranch == x64))
                        {
                            if(Settings.PoeHUDBranch != x64)
                            {
                                Settings.PoeHUDBranch = x64;
                                ForceInitialize();
                            }
                        }

                        var garena = PoeHUDBranches[1];
                        if (ImGui.RadioButtonBool(garena, Settings.PoeHUDBranch == garena))
                        {
                            if(Settings.PoeHUDBranch != garena)
                            {
                                Settings.PoeHUDBranch = garena;
                                ForceInitialize();
                            }
                        }
                    }

                    ImGui.NextColumn();

                    style.SetColor(ColorTarget.Text, new ImGuiVector4(0.5f, 0.5f, 0.5f, 1));

                    ImGui.Text(plug.LocalVersion + (plug.LocalTag.Length > 0 ? $" ({plug.LocalTag})" : ""));
                    ImGui.NextColumn();

                    if (plug.UpdateState == ePluginUpdateState.HasUpdate)
                        style.SetColor(ColorTarget.Text, new ImGuiVector4(0, 1, 0, 1));
                    else if (plug.UpdateState == ePluginUpdateState.HasLowerUpdate)
                        style.SetColor(ColorTarget.Text, new ImGuiVector4(1, 0, 0, 1));

                    if(plug.RemoteVersion == "No changes" || plug.RemoteVersion == "Undefined")
                    {
                        ImGui.Text(plug.RemoteVersion);
                    }
                    else if (ImGui.CollapsingHeader(plug.RemoteVersion + (plug.RemoteTag.Length > 0 ? $" ({plug.RemoteTag})" : "") + UniqId, UniqId, true, false))
                    {
                        foreach(var file in plug.FilesToDownload)
                        {
                            ImGui.Text(file.Name);
                        }
                    }

                    style.SetColor(ColorTarget.Text, textColorBack);

                    ImGui.NextColumn();

                    if (!string.IsNullOrEmpty(plug.InstallProgress))
                    {
                        ImGui.Text($"{plug.InstallProgress}");
                    }
                    else if (!plug.bHasGitConfig)
                    {
                        ImGui.Text($"No git config");
                    }
                    else if (plug.UpdateState == ePluginUpdateState.HasUpdate ||
                             plug.UpdateState == ePluginUpdateState.HasLowerUpdate ||
                             plug.UpdateState == ePluginUpdateState.UnknownUpdate)
                    {

                        string buttonLabel = "";
                        if (plug.UpdateState == ePluginUpdateState.UnknownUpdate)
                            buttonLabel = "Unknown update";
                        else if (plug.UpdateState == ePluginUpdateState.HasLowerUpdate)
                            buttonLabel = "Update";
                        else
                            buttonLabel = "Update";

                        if (ImGui.SmallButton($"{buttonLabel}{UniqId}"))
                        {
                            plug.UpdatePlugin();
                        }
                    }
                    else if (plug.UpdateState == ePluginUpdateState.NoUpdate)
                    {
                        style.SetColor(ColorTarget.Text, new ImGuiVector4(0, 1, 0, 1));
                        ImGui.Text($"Updated");
                      
                    }
                    else if (plug.UpdateState == ePluginUpdateState.ReadyToInstal)
                    {
                        style.SetColor(ColorTarget.Text, new ImGuiVector4(1, 1, 0, 1));
                        ImGui.Text($"Restart PoeHUD");
                    }
                    else
                    {
                        ImGui.Text($"Wrong git config");
                    }

                    style.SetColor(ColorTarget.Text, textColorBack);

                    ImGui.NextColumn();
                    ImGui.Separator();

                    if (plug.IsPoeHUD)
                    {
                        ImGui.NextColumn();
                        ImGui.Text($"");
                        ImGui.NextColumn();
                        ImGui.NextColumn();
                        ImGui.NextColumn();
                        ImGui.Separator();
                    }

                }

                ImGui.EndChild();
                #endregion
            }
            else if (CurrentWindowTab == UWindowTab.AvailablePlugins)
            {

                #region AvailablePlugins

                if (AllAvailablePlugins == null)
                {
                    ImGui.Text($"File {AvailablePluginsConfigParser.AvailablePluginsConfigFile} is not found!{UniqId}");
                    WindowHeight = 40;
                    return;
                }

                ImGuiNative.igGetContentRegionAvail(out newcontentRegionArea);
                ImGui.BeginChild($"{UniqId}", new ImGuiVector2(newcontentRegionArea.X, newcontentRegionArea.Y), true, WindowFlags.Default);
                
                ImGui.Columns(4, UniqId, true);
                ImGui.SetColumnWidth(0, 200);
                ImGui.SetColumnWidth(2, 80);
                ImGui.SetColumnOffset(2, newcontentRegionArea.X - 200);
                ImGui.Text($"Plugin name");
                ImGui.NextColumn();
                ImGui.Text($"Description");
                ImGui.NextColumn();
                ImGui.Text($"URL");
                ImGui.NextColumn();
                ImGui.Text($"State");
                ImGui.Separator();
                ImGui.NextColumn();

                foreach (var availPlug in AllAvailablePlugins.ToList())
                {
                    ImGui.Text($"{availPlug.PluginName}");
                    ImGui.NextColumn();
                    ImGui.Text($"{availPlug.Description}");
                    ImGui.NextColumn();

                    if (ImGui.Button($"Open URL{UniqId}"))
                    {
                        System.Diagnostics.Process.Start($"https://github.com/{availPlug.GitOwner}/{availPlug.GitName}");
                    }
                    ImGui.NextColumn();

                    if (availPlug.bOwned)
                    {
                        style.SetColor(ColorTarget.Text, new ImGuiVector4(0, 1, 0, 1));
                        ImGui.Text($"Owned");
                    }
                    else if (availPlug.InstalledPlugin != null)
                    {
                        if (availPlug.InstalledPlugin.InstallProgress.Length > 0)
                        {
                            ImGui.Text($"{availPlug.InstalledPlugin.InstallProgress}");
                        }
                        else if (availPlug.bInstaled)
                        {
                            style.SetColor(ColorTarget.Text, new ImGuiVector4(1, 1, 0, 1));
                            ImGui.Text($"Restart PoeHUD");
                        }
                        else
                        {
                            ImGui.Text($"Downloading...");
                        }
                    }
                    else if (ImGui.Button($"Install{UniqId}"))
                    {
                        var newPluginDir = Path.Combine("plugins", availPlug.PluginName);
                        Directory.CreateDirectory(newPluginDir);

                        var newConfigPath = Path.Combine(newPluginDir, GitConfigParser.ConfigFileName);
                        DownloadConfigForPlugin(availPlug, newConfigPath, newPluginDir);
                    }
                    style.SetColor(ColorTarget.Text, textColorBack);

                    ImGui.NextColumn();
                    ImGui.Separator();
                }

                ImGui.EndChild();

                #endregion

            }
        }

        private async Task DownloadConfigForPlugin(AvailablePlugin plugin, string configPath, string pluginDir)
        {
            if (!plugin.GitConfigURL.Contains("https://") && !plugin.GitConfigURL.Contains("$"))
            {
                LogMessage("Wrong config url: " + plugin.GitConfigURL, 10);
                return;
            }

            if (plugin.GitConfigURL.Contains("https://"))
            {
                using (var webClient = new WebClient())
                {
                    await webClient.DownloadFileTaskAsync(plugin.GitConfigURL, configPath);
                }
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(configPath));
              
                File.WriteAllLines(configPath, plugin.GitConfigURL.Split('$'));
            }

            var result = CheckAddPluginsByConfig();
            if (result != null)
            {
                plugin.InstalledPlugin = result;

                await CheckPluginUpdate(result);

                result.UpdatePlugin();
                plugin.bInstaled = true;
                LogMessage(pluginDir, 10);
                PoeHUD.Hud.PluginExtension.PluginExtensionPlugin.LoadPluginFromDirectory(pluginDir);
            }
        }

        public enum UWindowTab
        {
            InstalledPlugins,
            AvailablePlugins
        }
    }
}