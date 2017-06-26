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
            MenuPlugin.ExternalMouseClick = OnMouseEvent;
        }

        private bool OnMouseEvent(MouseEventID id, Vector2 position)
        {
            if (!Settings.Enable) return false;
            Mouse_Pos = position;

            if (id == MouseEventID.LeftButtonDown)
            {
                if (DrawRect.Contains(Mouse_Pos))
                {
                    bMouse_Drag = true;
                    Mouse_StartDragPos = position;
                    StartDragWinPosX = Settings.WindowPosX;
                    StartDragWinPosY = Settings.WindowPosY;
                }
            }
            else if (id == MouseEventID.LeftButtonUp)
            {
                bMouse_Drag = false;
            }
            else if (bMouse_Drag && id == MouseEventID.MouseMove)
            {
                Mouse_DragDelta = position - Mouse_StartDragPos;

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


            if (id != MouseEventID.LeftButtonUp && id != MouseEventID.LeftButtonDown) return false;

            var hitWindow = DrawRect.Contains(position);

            bMouse_Click = hitWindow && id == MouseEventID.LeftButtonUp;
            if (bMouse_Click)
                Mouse_ClickPos = position;

            return hitWindow;
        }

        private void OpenOrClose()
        {
            if (!Settings.Enable) return;

            RootButton.Instance.CloseRootMenu();

            if (InitOnce) return;
            InitOnce = true;

            AllPlugins = new List<PluginToUpdate>();

            foreach (var plugin in PluginExtensionPlugin.Plugins)
            {
                AddPlugin(plugin.PluginName, plugin.PluginDirectory);
            }

            CheckAddPluginsByConfig();

            AllPlugins = AllPlugins.OrderByDescending(x => x.UpdateVariant).ToList();

            CheckUpdates();

            //AllAvailablePlugins = AvailablePluginsConfigParser.Parse(PluginDirectory);
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
                    LogError($"Plugin '" + plugin.PluginName + "' check update error: " + notFoundEx.Message, 10);
                    return;
                }
                catch (Exception ex)
                {
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
                            /*//Select this first release
                            if (plugin.FilesToDownload.Count > 0)
                            {
                                LogMessage("Multiple .zip content is not allowed (Updating from Release). Current zip: " + asset.Name + ", Selected zip: " + plugin.FilesToDownload[0].Name, 10);
                                continue;
                            }
                            */

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

                await CheckFiles(plugin, gitClient, plugin.BranchName, "");

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

        private async Task CheckFiles(PluginToUpdate plugin, GitHubClient gitClient, string branch, string path)
        {
            IReadOnlyList<RepositoryContent> allContent = null;

            var gitFullPath = branch + path;

            try
            {
                if (!string.IsNullOrEmpty(gitFullPath))
                    allContent =
                        await
                            gitClient.Repository.Content.GetAllContents(plugin.RepoOwner, plugin.RepoName, gitFullPath);
                else
                    allContent = await gitClient.Repository.Content.GetAllContents(plugin.RepoOwner, plugin.RepoName);
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
                    var localPath = Path.Combine(plugin.PluginDirectory + @"\" + path, contentEntity.Name);

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

                    if (download)
                    {
                        var updateFilePath = Path.Combine(plugin.PluginDirectory + @"\" + UpdateTempDir + @"\" + path,
                            contentEntity.Name);

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
                    await CheckFiles(plugin, gitClient, branch, newPath);
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

            if(AllAvailablePlugins == null)
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
                    if(availPlug.InstalledPlugin.InstallProgress.Length > 0)
                    {
                        Graphics.DrawText(availPlug.InstalledPlugin.InstallProgress, 15, buttonRect.Center, Color.Green, FontDrawFlags.VerticalCenter | FontDrawFlags.Center);
                    }
                    else if(availPlug.bInstaled)
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

            Graphics.DrawText("Plugin name", 15, new Vector2(drawPosX + 15, drawPosY + 5), Color.Gray);
            Graphics.DrawText("Local version", 15, new Vector2(drawPosX + 200, drawPosY + 5), Color.Gray);
            Graphics.DrawText("Remote version", 15, new Vector2(drawPosX + 400, drawPosY + 5), Color.Gray);

            drawPosY += 30;

            foreach (var plug in AllPlugins.ToList())
            {
                var pluginFrame = new RectangleF(drawPosX + 5, drawPosY, width - 10, 26);

                Graphics.DrawBox(pluginFrame, Color.Black);
                Graphics.DrawFrame(pluginFrame, 2, Color.Gray);

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
                else if (plug.UpdateVariant == ePluginSourceOfUpdate.Undefined)
                {
                    Graphics.DrawText("No git config", 15, buttonTextPos, Color.Gray, FontDrawFlags.Right);
                }
                else
                {
                    Graphics.DrawText("Wrong git config?", 15, buttonTextPos, Color.Gray, FontDrawFlags.Right);
                }

                drawPosY += 30;
            }

        }

        public enum UWindowTab
        {
            InstalledPlugins,
            AvailablePlugins
        }
    }
}