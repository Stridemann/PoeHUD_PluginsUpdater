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

namespace PoeHUD_PluginsUpdater
{
    public class PoeHUD_PluginsUpdater : BaseSettingsPlugin<PoeHUD_PluginsUpdater_Settings>
    {
        public static Graphics UGraphics;

        public PoeHUD_PluginsUpdater()
        {
            PluginName = "PluginsUpdater";
        }
        private const float WindowWidth = 700;

        public const string VersionFileName = "%PluginVersion.txt";
        public const string UpdateTempDir = "%PluginUpdate%";//Do not change this value. Otherwice this value in PoeHUD should be also changed.

        private string[] PoeHUDBranches = new string[] { "x64", "Garena_DirectX_11" };


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

        public override void Initialise()
        {
            gitClient = new GitHubClient(new ProductHeaderValue("PoeHUDPluginsUpdater"));

            UGraphics = Graphics;
            Settings.Enable.Value = false; //OpenOrClose();
            AllAvailablePlugins = AvailablePluginsConfigParser.Parse(PluginDirectory);

            Settings.Enable.OnValueChanged += OpenOrClose;
            MenuPlugin.KeyboardMouseEvents.MouseDown += OnMouseDown;
            MenuPlugin.KeyboardMouseEvents.MouseUp += KeyboardMouseEvents_MouseUp;
            MenuPlugin.KeyboardMouseEvents.MouseMove += KeyboardMouseEvents_MouseMove;
            MenuPlugin.KeyboardMouseEvents.MouseClick += KeyboardMouseEvents_MouseClick;
        }

        private void KeyboardMouseEvents_MouseClick(object sender, MouseEventArgs e)
        {
            if (!Settings.Enable) return;

            if (e.Button == MouseButtons.Left)
            {
                var position = FixMousePos(new Vector2(e.X, e.Y));
                var hitWindow = DrawRect.Contains(position);
                if (hitWindow)
                {
                    Mouse_ClickPos = position;
                    bMouse_Click = true;
                }
            }
        }

        private void KeyboardMouseEvents_MouseMove(object sender, MouseEventArgs e)
        {
            if (!Settings.Enable) return;

            Mouse_Pos = FixMousePos(new Vector2(e.Location.X, e.Location.Y));

            if (bMouse_Drag)
            {
                Mouse_DragDelta = Mouse_Pos - Mouse_StartDragPos;

                Settings.WindowPosX = StartDragWinPosX + Mouse_DragDelta.X;
                Settings.WindowPosY = StartDragWinPosY + Mouse_DragDelta.Y;

                if (Settings.WindowPosX < 0)
                    Settings.WindowPosX = 0;

                if (Settings.WindowPosY < 0)
                    Settings.WindowPosY = 0;

                var clientRect = GameController.Window.GetWindowRectangle();

                if (Settings.WindowPosX + WindowWidth > clientRect.Width)
                    Settings.WindowPosX = clientRect.Width - WindowWidth;

                if (Settings.WindowPosY + WindowHeight > clientRect.Height)
                    Settings.WindowPosY = clientRect.Height - WindowHeight;
            }
        }

        private void KeyboardMouseEvents_MouseUp(object sender, MouseEventArgs e)
        {
            if (!Settings.Enable) return;

            if (e.Button == MouseButtons.Left)
            {
                bMouse_Drag = false;
            }
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (!Settings.Enable) return;

            if (e.Button == MouseButtons.Left)
            {
                if (DrawRect.Contains(Mouse_Pos))
                {
                    bMouse_Drag = true;
                    Mouse_StartDragPos = Mouse_Pos;
                    StartDragWinPosX = Settings.WindowPosX;
                    StartDragWinPosY = Settings.WindowPosY;
                }
            }
            return;// hitWindow;
        }

        private Vector2 FixMousePos(Vector2 rawPos)
        {
            var offset = GameController.Window.GetWindowRectangle();
            return rawPos - offset.TopLeft;
        }

        private void OpenOrClose()
        {
            if (!Settings.Enable) return;

            //RootButton.Instance.CloseRootMenu();

            if (InitOnce) return;
            InitOnce = true;

            AllPlugins = new List<PluginToUpdate>();

            foreach (var plugin in PluginExtensionPlugin.Plugins)
            {
                AddPlugin(plugin.PluginName, plugin.PluginDirectory);
            }

            CheckAddPluginsByConfig();

            AllPlugins = AllPlugins.OrderByDescending(x => x.UpdateVariant).ToList();

            AddPoeHudPlugin();

            CheckUpdates();

            //AllAvailablePlugins = AvailablePluginsConfigParser.Parse(PluginDirectory);
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

            foreach (var plugin in AllPlugins)
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

                        var localPath = Path.Combine(plugin.PluginDirectory + @"\", plugin.PoehudExeRealName);
                        var poeHudFInfo = new FileInfo(localPath);

                        if (poeHudFInfo.Exists)
                        {
                            if (poeHudFInfo.Length != contentEntity.Size)
                            {
                                download = true;
                            }
                        }
                    }
                    else
                    {
                        var localPath = Path.Combine(plugin.PluginDirectory + @"\" + path, contentEntity.Name);
                        if (plugin.IsPoeHUD && path.Contains("Release"))
                            localPath = Path.Combine(plugin.PluginDirectory + @"\" + path.Remove(path.IndexOf("Release"),7), contentEntity.Name);

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
                            Path = updateFilePath
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

        public override void Render()
        {
            if (WinApi.IsKeyDown(Keys.Space))
                Settings.Enable.Value = false;

            var drawPosX = Settings.WindowPosX;
            var drawPosY = Settings.WindowPosY;

            DrawRect = new RectangleF(drawPosX, drawPosY, WindowWidth + 10, WindowHeight + 55);
            UpdaterUtils.DrawFrameBox(DrawRect, 2, Color.Black, Color.White);

            #region Close Button
            var closeRect = DrawRect;
            closeRect.X += closeRect.Width - 25;
            closeRect.Y += 5;
            closeRect.Width = 20;
            closeRect.Height = 20;

            if (UpdaterUtils.DrawButton(closeRect, 1, new Color(20, 20, 20, 255), Color.White))
            {
                Settings.Enable.Value = false;
            }
            Graphics.DrawText("X", 20, new Vector2(closeRect.X + 4, closeRect.Y - 2), Color.White);
            #endregion

            #region Tabs buttons
            var installedButtonRect = new RectangleF(DrawRect.X + 5, DrawRect.Y + 5, 120, 25);

            if (UpdaterUtils.DrawTextButton(installedButtonRect, "Installed Plugins", 15, 1, CurrentWindowTab == UWindowTab.InstalledPlugins ? Color.Gray : new Color(60, 60, 60, 255),
                Color.White, Color.White))
                CurrentWindowTab = UWindowTab.InstalledPlugins;

            installedButtonRect.X += installedButtonRect.Width + 10;

            if (UpdaterUtils.DrawTextButton(installedButtonRect, "Available Plugins", 15, 1, CurrentWindowTab == UWindowTab.AvailablePlugins ? Color.Gray : new Color(60, 60, 60, 255),
                 Color.White, Color.White))
                CurrentWindowTab = UWindowTab.AvailablePlugins;

            #endregion


            var subWindowRect = new RectangleF(drawPosX + 5, drawPosY + 30, WindowWidth, WindowHeight);
            UpdaterUtils.DrawFrameBox(subWindowRect, 2, Color.Black, Color.White);

            if (CurrentWindowTab == UWindowTab.InstalledPlugins)
                DrawWindow_InstalledPlugins(subWindowRect.X, subWindowRect.Y, subWindowRect.Width);
            else if (CurrentWindowTab == UWindowTab.AvailablePlugins)
                DrawWindow_AllPlugins(subWindowRect.X, subWindowRect.Y, subWindowRect.Width);

            Graphics.DrawText("Notes: Move window by mouse drag. Close window key: Space", 15,
                new Vector2(subWindowRect.X + 10, subWindowRect.Y + subWindowRect.Height + 5), Color.Gray, FontDrawFlags.Left);


            bMouse_Click = false;
        }

        private void DrawWindow_AllPlugins(float drawPosX, float drawPosY, float width)
        {
            drawPosY += 5;

            if (AllAvailablePlugins == null)
            {
                Graphics.DrawText($"File {AvailablePluginsConfigParser.AvailablePluginsConfigFile} is not found!", 20, new Vector2(drawPosX + 15, drawPosY + 5), Color.Red);
                WindowHeight = 40;
                return;
            }

            WindowHeight = AllAvailablePlugins.Count * 45 + 5;

            foreach (var availPlug in AllAvailablePlugins)
            {
                var pluginFrame = new RectangleF(drawPosX + 5, drawPosY, width - 10, 40);
                Graphics.DrawBox(pluginFrame, Color.Black);
                Graphics.DrawFrame(pluginFrame, 2, Color.Gray);

                Graphics.DrawText(availPlug.PluginName, 20, new Vector2(pluginFrame.X + 5, pluginFrame.Y));
                Graphics.DrawText($"Owner: {availPlug.GitOwner}", 14, new Vector2(pluginFrame.X + 300, pluginFrame.Y + 3), Color.Gray);

                Graphics.DrawText(availPlug.Description, 14, new Vector2(pluginFrame.X + 10, pluginFrame.Y + 23), Color.Gray);


                var buttonRect = new RectangleF((pluginFrame.X + pluginFrame.Width) - 240, drawPosY + 4, 100, 30);

                if (UpdaterUtils.DrawTextButton(buttonRect, "Open URL", 20, 1, new Color(50, 50, 50, 220), Color.White, Color.Yellow))
                {
                    System.Diagnostics.Process.Start($"https://github.com/{availPlug.GitOwner}/{availPlug.GitName}");
                }

                buttonRect.X += 105;
                buttonRect.Width += 30;

                if (availPlug.bOwned)
                {
                    Graphics.DrawText("Owned", 20, buttonRect.Center, Color.Green, FontDrawFlags.VerticalCenter | FontDrawFlags.Center);
                }
                else if (availPlug.InstalledPlugin != null)
                {
                    if (availPlug.InstalledPlugin.InstallProgress.Length > 0)
                    {
                        Graphics.DrawText(availPlug.InstalledPlugin.InstallProgress, 15, buttonRect.Center, Color.Green, FontDrawFlags.VerticalCenter | FontDrawFlags.Center);
                    }
                    else if (availPlug.bInstaled)
                    {
                        Graphics.DrawText("Restart PoeHUD", 20, buttonRect.Center, Color.Green, FontDrawFlags.VerticalCenter | FontDrawFlags.Center);
                    }
                    else
                    {
                        Graphics.DrawText("Downloading...", 20, buttonRect.Center, Color.Yellow, FontDrawFlags.VerticalCenter | FontDrawFlags.Center);
                    }
                }
                else if (UpdaterUtils.DrawTextButton(buttonRect, "Install", 20, 1, new Color(50, 50, 50, 220), Color.White, Color.Yellow))
                {
                    var newPluginDir = Path.Combine(@"plugins\", availPlug.PluginName);
                    Directory.CreateDirectory(newPluginDir);

                    var newConfigPath = Path.Combine(newPluginDir, GitConfigParser.ConfigFileName);

                    DownloadConfigForPlugin(availPlug, newConfigPath);
                }

                drawPosY += 45;
            }
        }

        private async Task DownloadConfigForPlugin(AvailablePlugin plugin, string configPath)
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
                File.WriteAllLines(configPath, plugin.GitConfigURL.Split('$'));
            }

            var result = CheckAddPluginsByConfig();
            if (result != null)
            {
                plugin.InstalledPlugin = result;

                await CheckPluginUpdate(result);

                result.UpdatePlugin();
                plugin.bInstaled = true;
            }
        }

        private void DrawWindow_InstalledPlugins(float drawPosX, float drawPosY, float width)
        {
            WindowHeight = AllPlugins.Count * 30 + 35;

            drawPosY += 5;

            var allPlugins = AllPlugins.ToList();

            if(allPlugins.Count > 0 && allPlugins[0].IsPoeHUD)
            {
                drawPosY = DrawPlugin(drawPosX, drawPosY, width, allPlugins[0]);
            }

            Graphics.DrawText("Plugin name", 15, new Vector2(drawPosX + 15, drawPosY + 5), Color.Gray);
            Graphics.DrawText("Local version", 15, new Vector2(drawPosX + 200, drawPosY + 5), Color.Gray);
            Graphics.DrawText("Remote version", 15, new Vector2(drawPosX + 400, drawPosY + 5), Color.Gray);

            drawPosY += 30;

       
            foreach (var plug in allPlugins)
            {
                if (plug.IsPoeHUD) continue;
                drawPosY = DrawPlugin(drawPosX, drawPosY, width, plug);
            }

        }

        private float DrawPlugin(float drawPosX, float drawPosY, float width, PluginToUpdate plug)
        {
            var pluginFrame = new RectangleF(drawPosX + 5, drawPosY, width - 10, 26);

            int frameBorderWidth = 2;
            if (plug.IsPoeHUD)
            {
                pluginFrame.Height += 50;
                frameBorderWidth = 4;
            }

            Graphics.DrawBox(pluginFrame, Color.Black);
            Graphics.DrawFrame(pluginFrame, frameBorderWidth, Color.Gray);

            pluginFrame.X += 10;

            Graphics.DrawText(plug.PluginName, 20, new Vector2(pluginFrame.X, pluginFrame.Y));
            Graphics.DrawText(plug.LocalVersion + (plug.LocalTag.Length > 0 ? $" ({plug.LocalTag})" : ""), 15, new Vector2(pluginFrame.X + 200, pluginFrame.Y + 5), Color.Gray);

            var color = Color.Gray;

            if (plug.UpdateState == ePluginUpdateState.HasUpdate)
                color = Color.Green;
            else if (plug.UpdateState == ePluginUpdateState.HasLowerUpdate)
                color = Color.Red;

            Graphics.DrawText(plug.RemoteVersion + (plug.RemoteTag.Length > 0 ? $" ({plug.RemoteTag})" : ""), 15, new Vector2(pluginFrame.X + 400, pluginFrame.Y + 5), color);


            var buttonRect = new RectangleF(pluginFrame.X + pluginFrame.Width - 75, drawPosY + 4, 60, 20);
            var buttonTextPos = buttonRect.TopRight;
            buttonTextPos.X -= 5;
            buttonTextPos.Y += 2;

            if (!string.IsNullOrEmpty(plug.InstallProgress))
            {
                Graphics.DrawText(plug.InstallProgress, 15, buttonTextPos, Color.White, FontDrawFlags.Right);
            }
            else if (!plug.bHasGitConfig)
            {
                Graphics.DrawText("No git config", 15, buttonTextPos, Color.Gray, FontDrawFlags.Right);
            }
            else if (plug.UpdateState == ePluginUpdateState.HasUpdate ||
                     plug.UpdateState == ePluginUpdateState.HasLowerUpdate ||
                     plug.UpdateState == ePluginUpdateState.UnknownUpdate)
            {
                if (UpdaterUtils.DrawButton(buttonRect, 1, new Color(50, 50, 50, 220), Color.White))
                {
                    plug.UpdatePlugin();
                }
                if (plug.UpdateState == ePluginUpdateState.UnknownUpdate)
                    Graphics.DrawText("Unknown update", 15, buttonTextPos, Color.Gray, FontDrawFlags.Right);
                else if (plug.UpdateState == ePluginUpdateState.HasLowerUpdate)
                    Graphics.DrawText("Update", 15, buttonTextPos, Color.Red, FontDrawFlags.Right);
                else
                    Graphics.DrawText("Update", 15, buttonTextPos, Color.Yellow, FontDrawFlags.Right);
            }
            else if (plug.UpdateState == ePluginUpdateState.NoUpdate)
            {
                Graphics.DrawText("Updated", 15, buttonTextPos, Color.Green, FontDrawFlags.Right);
            }
            else if (plug.UpdateState == ePluginUpdateState.ReadyToInstal)
            {
                Graphics.DrawText("(Restart PoeHUD)", 15, buttonTextPos, Color.Green, FontDrawFlags.Right);
            }
            else
            {
                Graphics.DrawText("Wrong git config", 15, buttonTextPos, Color.Gray, FontDrawFlags.Right);
            }


            if (plug.IsPoeHUD)
            {
                if (string.IsNullOrEmpty(plug.InstallProgress))
                {
                    var selectBranchRect = new RectangleF(pluginFrame.X + 10, drawPosY + 30, 150, 20);
                    selectBranchRect.Y += 2;
                    Graphics.DrawText("Select PoeHUD branch: ", 15, selectBranchRect.TopLeft);
                    selectBranchRect.Y -= 2;

                    selectBranchRect.X += 150;

                    for (int i = 0; i < PoeHUDBranches.Length; i++)
                    {
                        bool selected = Settings.PoeHUDBranch == PoeHUDBranches[i];

                        var selColor = selected ? Color.White : Color.Gray;

                        if (UpdaterUtils.DrawTextButton(selectBranchRect, PoeHUDBranches[i], 15, selected ? 3 : 1, new Color(50, 50, 50, 220), selColor, selColor))
                        {
                            if (Settings.PoeHUDBranch != PoeHUDBranches[i])
                            {
                                Settings.PoeHUDBranch = PoeHUDBranches[i];
                                plug.InstallProgress = "Restart PoeHUD. It will check updates from " + Settings.PoeHUDBranch + " branch.";
                                plug.RemoteVersion = "";
                                plug.LocalVersion = "";
                            }
                        }
                        selectBranchRect.X += 170;
                    }

                    selectBranchRect = new RectangleF(pluginFrame.X + 10, drawPosY + 55, 150, 20);

                    Graphics.DrawText("Note: PoeHUD will be automatically restarted.", 15, selectBranchRect.TopLeft, Color.Gray);
                }

                drawPosY += 55;
                WindowHeight += 55;
            }

            drawPosY += 30;
            return drawPosY;
        }

        public enum UWindowTab
        {
            InstalledPlugins,
            AvailablePlugins
        }
    }
}