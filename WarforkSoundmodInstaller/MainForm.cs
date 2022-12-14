using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO.Compression;
using System.Threading;
using System.Diagnostics;
using Microsoft.Win32;
using System.Security.Cryptography;

namespace WarforkSoundmod
{
    public partial class MainForm : Form
    {
        const string EXPECTED_WF_MD5 = "3c685bcb3677a3075750030c6c13d42a";
        const string PATCHED_MD5 = "a804c13705add85f29cd39146f6e6acd";
        const string EXPECTED_EXENAME = "warfork_x64.exe";
        static string CalculateMD5(string filename)
        {
            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(filename))
                {
                    var hash = md5.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
        }

        public MainForm()
        {
            InitializeComponent();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {

        }

        private void InvokeUI(Action a)
        {
            try
            {
                this.BeginInvoke(new MethodInvoker(a));
            }
            catch (System.InvalidOperationException)
            {
                // swallow InvalidOperationException:
                // happens when closing window and InvokeUI is called.
            }

        }


        private void AddLogLine(string line)
        {
            InvokeUI(() =>
            {
                tbLogs.Text += line + "\r\n";
                tbLogs.SelectionStart = tbLogs.Text.Length;
                tbLogs.ScrollToCaret();

            });
        }

        private bool IsWarforkRunning()
        {
            return Process.GetProcesses().Where(p => p.ProcessName == "warfork_x64").Count() > 0;
        }

        private string getBasewfUserDir()
        {
            var userHome = Environment.GetEnvironmentVariable("USERPROFILE");
            var basewfDir = Path.Combine(userHome, @"Documents\My Games\Warfork 2.1\basewf");
            return basewfDir;
        }

        private void Install()
        {
            try
            {

                var basewfDir = getBasewfUserDir();

                if (!Directory.Exists(basewfDir))
                {

                    Directory.CreateDirectory(basewfDir);
                    AddLogLine($"Created ${basewfDir}");
                }

                var zipPath = Path.Combine(basewfDir, "sounds.zip");

                File.WriteAllBytes(zipPath, Data.sounds);
                using (var zipfile = ZipFile.OpenRead(zipPath))
                {
                    foreach (var entry in zipfile.Entries)
                    {
                        var path = Path.Combine(basewfDir, entry.FullName.Replace("/", "\\"));
                        if (path.EndsWith("\\")
                            && !Directory.Exists(path))
                        {
                            Directory.CreateDirectory(path);
                        }

                        if (!path.EndsWith("\\"))
                        {
                            AddLogLine($"Extrtact: {path}");
                            entry.ExtractToFile(path, overwrite: true);
                        }
                    }
                }

                try { File.Delete(zipPath); } catch { };

                var origBackupPath = $"{wfExePath}.original";
                if (!File.Exists(origBackupPath))
                {
                    File.Copy(wfExePath, origBackupPath);
                    AddLogLine($"Created backup of original exe: {origBackupPath}");
                }

                File.WriteAllBytes(wfExePath, Data.warfork_x64);
                AddLogLine($"Installed patched exe: {wfExePath}");


            }
            catch (Exception ex)
            {
                AddLogLine(ex.ToString());
            }
        }

        string steamPath;
        string wfExePath;
        Thread installThread;

        bool prereqChecks(bool isInstall)
        {
            if (IsWarforkRunning())
            {
                AddLogLine("Exit Warfork before installing.");
                return false;
            }

            steamPath = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", "")?.ToString();
            if (steamPath != null)
            {
                wfExePath = Path.Combine(steamPath, @"steamapps\common\fvi\fvi-launcher\applications\warfork\Warfork.app\Contents\Resources\warfork_x64.exe");
                if (!File.Exists(wfExePath))
                {
                    AddLogLine($"Could not find warfork_x64.exe at: {wfExePath}");
                    wfExePath = null;
                }
            }

            if (wfExePath == null)
            {
                AddLogLine("Could not find steam install of warfork_x64.exe. Querying warfork_x64.exe.");
                var ofd = new OpenFileDialog()
                {
                    FileName = "",
                    Filter = "Exe files (*.exe)|*.exe",
                    Title = "Select warfork_x64.exe"
                };
                if (ofd.ShowDialog() != DialogResult.OK)
                {
                    AddLogLine("User cancelled query for warfork_x64.exe.");
                    return false;
                }

                if (!File.Exists(ofd.FileName))
                {
                    AddLogLine("Selected file does not exist");
                    return false;
                }

                wfExePath = ofd.FileName;
            }

            var wfExeName = Path.GetFileName(wfExePath);
            if (wfExeName != EXPECTED_EXENAME)
            {
                AddLogLine($"Warning: selected exe is {wfExeName} instead of {EXPECTED_EXENAME}");
            }

            if (isInstall)
            {
                var calculatedWfExeMd5 = CalculateMD5(wfExePath);
                if (EXPECTED_WF_MD5 != calculatedWfExeMd5
                    && PATCHED_MD5 != calculatedWfExeMd5) // allow rewriting an install
                {
                    AddLogLine(
                        $"Hash mismatch for {EXPECTED_EXENAME}: \r\n" + 
                        $"  expected: {EXPECTED_WF_MD5}\r\n" + 
                        $"       got: {calculatedWfExeMd5}");
                    wfExePath = null;
                    return false;
                }
            }



            return true;
        }

        private void btnInstall_click(object sender, EventArgs e)
        {
            try
            {
                tbLogs.Clear();

                if (!prereqChecks(isInstall: true))
                    return;

                installThread = new Thread(() => Install());
                installThread.Start();

            }
            catch (Exception ex)
            {
                AddLogLine(ex.ToString());
            }
        }

        private void btnUninstall_Click(object sender, EventArgs e)
        {
            tbLogs.Clear();
            try
            {
                if (!prereqChecks(isInstall: false))
                    return;

                var origBackupPath = $"{wfExePath}.original";
                if (File.Exists(origBackupPath))
                {
                    File.Copy(origBackupPath, wfExePath, overwrite: true);
                    AddLogLine("Restored original exe.");
                }

                var basewfDir = getBasewfUserDir();
                var soundsDir = Path.Combine(basewfDir, "sounds");
                if (Directory.Exists(soundsDir))
                {
                    var zipPath = Path.Combine(basewfDir, "sounds.zip");

                    File.WriteAllBytes(zipPath, Data.sounds);
                    List<string> directories = new List<string>();
                    using (var zipfile = ZipFile.OpenRead(zipPath))
                    {
                        foreach (var entry in zipfile.Entries)
                        {
                            var path = Path.Combine(basewfDir, entry.FullName.Replace("/", "\\"));
                            if (path.EndsWith("\\"))
                            {
                                directories.Add(path);
                            }

                            if (!path.EndsWith("\\")
                                && File.Exists(path))
                            {

                                AddLogLine($"Delete: {path}");
                                File.Delete(path);
                            }
                        }
                    }
                    var sortedDirs = directories.OrderBy(elem => elem.Count(c => c == '\\')).Reverse().ToList();
                    foreach (var dir in sortedDirs)
                    {

                        try
                        {
                            if (Directory.Exists(dir))
                            {
                                AddLogLine($"Delete: {dir}");
                                Directory.Delete(dir);
                            }
                        }
                        catch (Exception ex)
                        {
                            AddLogLine($"  {ex.Message}");
                        }

                    }
                }
            }
            catch (Exception ex)
            {
                AddLogLine(ex.ToString());
            }
        }

        private void btnCopyLogs_Click(object sender, EventArgs e)
        {
            try
            {
                Clipboard.SetText(tbLogs.Text);
            }
            catch { }

        }
    }
}
