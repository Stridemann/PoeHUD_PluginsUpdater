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
   

        private bool bMouse_Drag;

        private RectangleF DrawRect;

        private bool InitOnce;

        public static bool bMouse_Click;
        public static Vector2 Mouse_ClickPos;
        public static Vector2 Mouse_DragDelta;
        public static Vector2 Mouse_Pos;
        public static Vector2 Mouse_StartDragPos;

        private int RepoFilesCheckedCount;

        private float StartDragWinPosX;
        private float StartDragWinPosY;

        private float WindowHeight;

        public override void Initialise()
        {
            UGraphics = Graphics;
            Settings.Enable.Value = false;
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
                        AddPlugin(pluginFolderName, pluginDirectoryInfo.FullName);
                    }
                }
            }


            AllPlugins = AllPlugins.OrderByDescending(x => x.UpdateVariant).ToList();

            CheckUpdates();
        }


        private void AddPlugin(string pluginName, string pluginDirectory)
        {
            var plugVariant = new PluginToUpdate
            {
                PluginName = pluginName,
                PluginDirectory = pluginDirectory
            };
            AllPlugins.Add(plugVariant);

            GitConfigParser.Parse(plugVariant);
        }

 
        private async void CheckUpdates()
        {
            var gitClient = new GitHubClient(new ProductHeaderValue("PoeHUDPluginsUpdater"));
            gitClient.Credentials = new Credentials(Settings.GitToken); 

            foreach (var plugin in AllPlugins)
            {
                await CheckPluginUpdate(plugin, gitClient);
            }
        }


        private async Task CheckPluginUpdate(PluginToUpdate plugin, GitHubClient gitClient)
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
                if(lovalVersionLines.Length > 1)
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

            var posX = Settings.WindowPosX;
            var posY = Settings.WindowPosY;
            WindowHeight = AllPlugins.Count*30 + 55;

            DrawRect = new RectangleF(posX, posY, WindowWidth, WindowHeight);

            UpdaterUtils.DrawFrameBox(DrawRect, 2, Color.Black, Color.White);

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

            posY += 5;

            Graphics.DrawText("Plugin name", 15, new Vector2(posX + 15, posY + 5), Color.Gray);
            Graphics.DrawText("Local version", 15, new Vector2(posX + 200, posY + 5), Color.Gray);
            Graphics.DrawText("Remote version", 15, new Vector2(posX + 400, posY + 5), Color.Gray);

            posY += 30;

            foreach (var plug in AllPlugins.ToList())
            {
                var pluginFrame = new RectangleF(posX + 5, posY, WindowWidth - 10, 26);

                Graphics.DrawBox(pluginFrame, Color.Black);
                Graphics.DrawFrame(pluginFrame, 2, Color.Gray);

                pluginFrame.X += 10;
                //pluginRect.Y += 5;

                Graphics.DrawText(plug.PluginName, 20, new Vector2(pluginFrame.X, pluginFrame.Y));
                Graphics.DrawText(plug.LocalVersion + (plug.LocalTag.Length > 0 ? $" ({plug.LocalTag})" : ""), 15, new Vector2(pluginFrame.X + 200, pluginFrame.Y + 5), Color.Gray);

                var color = Color.Gray;

                if (plug.UpdateState == ePluginUpdateState.HasUpdate)
                    color = Color.Green;
                else if (plug.UpdateState == ePluginUpdateState.HasLowerUpdate)
                    color = Color.Red;

                Graphics.DrawText(plug.RemoteVersion + (plug.RemoteTag.Length > 0 ? $" ({plug.RemoteTag})" : ""), 15, new Vector2(pluginFrame.X + 400, pluginFrame.Y + 5), color);


                var buttonRect = new RectangleF(pluginFrame.X + pluginFrame.Width - 75, posY + 4, 60, 20);
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

                posY += 30;
            }

            Graphics.DrawText("Notes: Move window by mouse drag. Close window key: Space", 15,
                new Vector2(posX + 10, posY), Color.Gray, FontDrawFlags.Left);

            bMouse_Click = false;
        }


    
    }
}