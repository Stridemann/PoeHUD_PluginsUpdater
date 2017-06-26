using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using PoeHUD.Plugins;

namespace PoeHUD_PluginsUpdater
{
    public enum ePluginUpdateState
    {
        Undefined,
        NoUpdate,
        HasUpdate,
        HasLowerUpdate,
        UnknownUpdate,
        WrongConfig,
        ReadyToInstal
    }

    public enum ePluginSourceOfUpdate
    {
        Undefined = 0,
        RepoBranch = 1,
        Release = 2
    }

    public class PluginToUpdate
    {
        public bool bAllowCheckUpdate;
        public bool bCustomTag;
        public string BranchName = "";
        //public bool bHasDownloadInfo;
        public List<FileToDownload> FilesToDownload = new List<FileToDownload>();
        public List<string> IgnoredEntities = new List<string>();

        public string InstallProgress = "";

        public string LocalVersion = "Checking...";
        public string LocalTag = "";
        public string PluginDirectory;
        public string PluginName = "%PluginName%";
        public string ReleaseRegexTag = "";
        public string RemoteVersion = "Undefined";
        public string RemoteTag = "";
        public string RepoName = "";
        public string RepoOwner = "";

        public bool bHasGitConfig;

        public ePluginUpdateState UpdateState = ePluginUpdateState.Undefined;
        public ePluginSourceOfUpdate UpdateVariant = ePluginSourceOfUpdate.Undefined;

        public async void UpdatePlugin()
        {
            if (FilesToDownload.Count == 0)
            {
                BasePlugin.LogMessage(
                    "Plugin don't have download information (changed files or release zip is not found)", 10);
                return;
            }

            var updateDirectory = Path.Combine(PluginDirectory, PoeHUD_PluginsUpdater.UpdateTempDir);

            if (Directory.Exists(updateDirectory))
                Directory.Delete(updateDirectory, true);

            Directory.CreateDirectory(updateDirectory);

            if (UpdateVariant == ePluginSourceOfUpdate.Release)
            {
                #region Release

                InstallProgress = "Preparing to download...";

                try
                {
                    using (var webClient = new WebClient())
                    {
                        webClient.DownloadProgressChanged +=
                            (s, e) => { InstallProgress = "Downloading: " + e.ProgressPercentage + "%"; };

                        var downloadFile = FilesToDownload[0];

                        var filename = downloadFile.Path;
                        await webClient.DownloadFileTaskAsync(downloadFile.Url, downloadFile.Path);

                        InstallProgress = "Extracting zip...";
                        ZipFile.ExtractToDirectory(filename, updateDirectory);
                        InstallProgress = "Extracting: Ok";

                        File.Delete(filename);


                        var dirInfo = new DirectoryInfo(updateDirectory);
                        var dirInfoDirs = dirInfo.GetDirectories();
                        var dirInfoFiles = dirInfo.GetFiles();

                        if (dirInfoDirs.Length == 1 && dirInfoFiles.Length == 0)
                        {
                            InstallProgress = "Fixing files hierarchy..";
                            var subDir = dirInfoDirs[0];

                            foreach (var file in subDir.GetFiles())
                            {
                                var destFile = Path.Combine(updateDirectory, file.Name);
                                File.Move(file.FullName, destFile);
                            }
                            foreach (var directory in subDir.GetDirectories())
                            {
                                var destDir = Path.Combine(updateDirectory, directory.Name);
                                Directory.Move(directory.FullName, destDir);
                            }
                            Directory.Delete(subDir.FullName);
                        }


                        foreach (var ignoreFile in IgnoredEntities)
                        {
                            var ignorePath = Path.Combine(updateDirectory, ignoreFile);

                            if (File.Exists(ignorePath))
                            {
                                File.Delete(ignorePath);
                                BasePlugin.LogMessage("Ignore File: " + ignorePath, 5);
                            }
                            else if (Directory.Exists(ignorePath))
                            {
                                Directory.Delete(ignorePath, true);
                                BasePlugin.LogMessage("Ignore Directory: " + ignorePath, 5);
                            }
                        }

                        InstallProgress = "";

                        var versionFilePath = Path.Combine(updateDirectory, PoeHUD_PluginsUpdater.VersionFileName);
                        File.WriteAllText(versionFilePath, RemoteVersion + Environment.NewLine + RemoteTag);

                        UpdateState = ePluginUpdateState.ReadyToInstal;
                    }
                }
                catch (Exception ex)
                {
                    InstallProgress = "Error updating";
                    BasePlugin.LogError(
                        "Plugins Updater: Error while updating plugin: " + PluginName + ", Error: " + ex.Message, 10);
                }

                #endregion
            }
            else if (UpdateVariant == ePluginSourceOfUpdate.RepoBranch)
            {
                InstallProgress = "Preparing to download...";

                try
                {
                    var downloadCount = FilesToDownload.Count;
                    var downloadedCount = 0;

                    using (var webClient = new WebClient())
                    {
                        webClient.DownloadProgressChanged +=
                            (s, e) =>
                            {
                                InstallProgress = "Downloading: " + downloadedCount + "/" + downloadCount + " (" +
                                                  e.ProgressPercentage + "%)";
                            };

                        foreach (var downloadFile in FilesToDownload)
                        {
                            var downloadDir = Path.GetDirectoryName(downloadFile.Path);
                            if (!Directory.Exists(downloadDir))
                                Directory.CreateDirectory(downloadDir);

                            await webClient.DownloadFileTaskAsync(downloadFile.Url, downloadFile.Path);
                            InstallProgress = "Downloading: " + downloadedCount + "/" + downloadCount;
                            downloadedCount++;
                        }

                        InstallProgress = "";

                        UpdateState = ePluginUpdateState.ReadyToInstal;
                    }
                }
                catch (Exception ex)
                {
                    InstallProgress = "Error updating";
                    BasePlugin.LogError(
                        "Plugins Updater: Error while updating plugin: " + PluginName + ", Error: " + ex.Message, 10);
                }
            }
            else
            {
                BasePlugin.LogMessage("Update type is not supported in code: " + UpdateVariant, 5);
            }
        }
    }

    public class FileToDownload
    {
        public string Name;
        public string Path;
        public string Url;
    }
}