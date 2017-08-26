using System;
using System.IO;
using System.Diagnostics;
using System.Threading;

namespace PoeHUDUpdater
{
    public class PoeHUDUpdater
    {
        public const string UpdateBackupDir = "%Backup%";

        public static void Main(string[] args)
        {
            if (args.Length != 5)
            {
                Console.WriteLine("This program should be launched by PoeHUD_PluginsUpdater. Exiting...");
                Console.ReadLine();
                return;
            }

            var poehudUpdateDir = args[0];
            var poehudDir = args[1];
            var poeHudProcessId = int.Parse(args[2]);
            var exeToStart = args[3];
            var exeToDelete = args[4];


            Process poeProcess;
            try
            {
                poeProcess = Process.GetProcessById(poeHudProcessId);
                poeProcess.Kill();

                do
                {
                    Thread.Sleep(200);
                }
                while (!poeProcess.HasExited);

                Console.WriteLine("PoeHUD closed...");
            }
            catch
            {
                Console.WriteLine("PoeHUD process not found...");
            }


            if (exeToDelete != "-" && File.Exists(exeToDelete))
            {
                Console.WriteLine("Deleting old PoeHUD.exe: " + exeToDelete);
                File.Delete(exeToDelete);
            }

            DirectoryInfo updateDirInfo = new DirectoryInfo(poehudUpdateDir);
            if (!updateDirInfo.Exists) return;

            var backupDir = Path.Combine(updateDirInfo.FullName, UpdateBackupDir);

            if (Directory.Exists(backupDir))
                FileOperationAPIWrapper.MoveToRecycleBin(backupDir);

            if (MoveDirectoryFiles(poehudDir, updateDirInfo.FullName, poehudDir))
            {
                Console.WriteLine("Deleting temp dir:\t" + updateDirInfo.FullName);
                Directory.Delete(updateDirInfo.FullName, true);
            }
            else
            {
                Console.WriteLine("PoeHUD Updater: some files wasn't moved or replaced while update. You can move them manually: " + updateDirInfo.FullName, 20);
            }

            Console.WriteLine();
            Console.WriteLine("Update completed!");
            Console.WriteLine();

            Console.WriteLine("Starting poehud: " + exeToStart);

            var processInfo = new ProcessStartInfo("cmd.exe", "/c " + exeToStart);
            processInfo.CreateNoWindow = true;
            processInfo.UseShellExecute = false;
            // *** Redirect the output ***
            //processInfo.RedirectStandardError = true;
            //processInfo.RedirectStandardOutput = true;
            processInfo.WorkingDirectory = poehudDir;

            processInfo.Verb = "runas"; //this is what actually runs the command as administrator

            try
            {
                var process = Process.Start(processInfo);
                process.WaitForExit();
                process.Close();
            }
            catch (Exception)
            {
                Console.WriteLine($"PoeHUD Updater: Can't start {exeToStart} with admin rights", 10);
                //If you are here the user clicked declined to grant admin privileges (or he's not administrator)
            }


            Console.WriteLine("PoeHUD launched! Press any key to close this window");
         
            //Console.ReadLine();
        }

        private static bool MoveDirectoryFiles(string origDirectory, string sourceDirectory, string targetDirectory)
        {
            bool noErrors = true;
            var sourceDirectoryInfo = new DirectoryInfo(sourceDirectory);

            foreach (var file in sourceDirectoryInfo.GetFiles())
            {
                var destFile = Path.Combine(targetDirectory, file.Name);
                bool fileExist = File.Exists(destFile);

                try
                {
                    var fileLocalPath = destFile.Replace(origDirectory, "");

                    if (fileExist)
                    {
                        var backupPath = origDirectory + @"\" + UpdateBackupDir + fileLocalPath;//Do not use Path.Combine due to Path.IsPathRooted checks
                        var backupDirPath = Path.GetDirectoryName(backupPath);

                        if (!Directory.Exists(backupDirPath))
                            Directory.CreateDirectory(backupDirPath);

                        File.Copy(destFile, backupPath, true);
                    }

                    File.Copy(file.FullName, destFile, true);
                    File.Delete(file.FullName);//Delete from temp update dir

                    if (fileExist)
                        Console.WriteLine("File Replaced:\t\t" + destFile);
                    else
                        Console.WriteLine("File Added:\t\t" + destFile);
                }
                catch (Exception ex)
                {
                    noErrors = false;
                    if (fileExist)
                    {
                        Console.WriteLine("PoeHUD Updater: can't replace file: " + destFile + ", Error: " + ex.Message, 10);
                        Console.WriteLine("Error replacing file: \t" + destFile);
                    }
                    else
                    {
                        Console.WriteLine("PoeHUD Updater: can't move file: " + destFile + ", Error: " + ex.Message, 10);
                        Console.WriteLine("Error moving file: \t" + destFile);
                    }
                }
            }

            foreach (var directory in sourceDirectoryInfo.GetDirectories())
            {
                var destDir = Path.Combine(targetDirectory, directory.Name);

                if (Directory.Exists(destDir))
                {
                    Console.WriteLine("Merging directory: \t" + destDir);
                    var curDirProcessNoErrors = MoveDirectoryFiles(origDirectory, directory.FullName, destDir);

                    if (curDirProcessNoErrors)
                        Directory.Delete(directory.FullName, true);

                    noErrors = curDirProcessNoErrors || noErrors;
                }
                else
                {
                    Directory.Move(directory.FullName, destDir);
                    Console.WriteLine("Moving directory: \t" + destDir);
                }
            }
            return noErrors;
        }
    }
}
