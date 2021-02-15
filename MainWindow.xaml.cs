using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.IO;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Net;

namespace ValheimSaveShield
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        //private static string defaultBackupFolder = LocalLow + "\\IronGate\\Valheim\\backups";
        private static string defaultBackupFolder = $@"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}\AppData\LocalLow\IronGate\Valheim\backups";
        private static string backupDirPath;
        //private static string defaultSaveFolder = LocalLow + "\\IronGate\\Valheim";
        private static string defaultSaveFolder = $@"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}\AppData\LocalLow\IronGate\Valheim";
        private static string saveDirPath;
        private List<SaveBackup> listBackups;
        private Boolean suppressLog;
        private FileSystemWatcher worldWatcher;
        private FileSystemWatcher charWatcher;

        private System.Timers.Timer saveTimer;
        private DateTime lastUpdateCheck;
        private SaveFile charSaveForBackup;

        private Thread ftpDirectorySync = null;

        public enum LogType
        {
            Normal,
            Success,
            Error
        }

        private bool BackupIsCurrent {
            get {
                if (!Directory.Exists($@"{saveDirPath}\worlds"))
                {
                    return false;
                }
                var worlds = Directory.GetFiles($@"{saveDirPath}\worlds", "*.db");
                bool backedup = false;
                foreach (string world in worlds)
                {
                    backedup = false;
                    string worldName = new FileInfo(world).Name.Split('.')[0];
                    DateTime saveDate = File.GetLastWriteTime(world);
                    for (int i = 0; i < listBackups.Count; i++)
                    {
                        SaveBackup b = listBackups.ToArray()[i];
                        DateTime backupDate = b.SaveDate;
                        if (saveDate.Equals(backupDate) && b.Type.Equals("World") && b.Name.Equals(worldName))
                        {
                            backedup = true;
                            break;
                        }
                    }
                    if (!backedup) return false;
                }
                backedup = false;
                var characters = Directory.GetFiles($@"{saveDirPath}\characters", "*.fch");
                foreach (string character in characters)
                {
                    string charName = new FileInfo(character).Name.Split('.')[0];
                    DateTime saveDate = File.GetLastWriteTime(character);
                    for (int i = 0; i < listBackups.Count; i++)
                    {
                        SaveBackup b = listBackups.ToArray()[i];
                        DateTime backupDate = b.SaveDate;
                        if (saveDate.Equals(backupDate) && b.Type.Equals("Character") && b.Name.Equals(charName))
                        {
                            backedup = true;
                            break;
                        }
                    }
                    if (!backedup) return false;
                }
                return true;
            }
            set
            {
                if (value)
                {
                    lblStatus.ToolTip = "Backed Up";
                    lblStatus.Content = FindResource("StatusOK");
                    btnBackup.IsEnabled = false;
                    btnBackup.Content = FindResource("SaveGrey");
                }
                else
                {
                    lblStatus.ToolTip = "Not Backed Up";
                    lblStatus.Content = FindResource("StatusNo");
                    btnBackup.IsEnabled = true;
                    btnBackup.Content = FindResource("Save");
                }
            }
        }

        ~MainWindow()
        {
            if (ftpDirectorySync != null)
            {
                ftpDirectorySync.Abort();
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            suppressLog = false;
            txtLog.Text = "Version " + typeof(MainWindow).Assembly.GetName().Version;
            if (Properties.Settings.Default.CreateLogFile)
            {
                System.IO.File.WriteAllText("log.txt", DateTime.Now.ToString() + ": Version " + typeof(MainWindow).Assembly.GetName().Version + "\r\n");
            }
            logMessage("Loading...");
            if (Properties.Settings.Default.UpgradeRequired)
            {
                Properties.Settings.Default.Upgrade();
                Properties.Settings.Default.UpgradeRequired = false;
                Properties.Settings.Default.Save();
            }

            if (Properties.Settings.Default.SaveFolder.Length == 0)
            {
                logMessage("Save folder not set; reverting to default.");
                Properties.Settings.Default.SaveFolder = defaultSaveFolder;
                Properties.Settings.Default.Save();
            }
            else if (!Directory.Exists(Properties.Settings.Default.SaveFolder) && !Properties.Settings.Default.SaveFolder.Equals(defaultSaveFolder))
            {
                logMessage("Save folder (" + Properties.Settings.Default.SaveFolder + ") not found; reverting to default.");
                Properties.Settings.Default.SaveFolder = defaultSaveFolder;
                Properties.Settings.Default.Save();
            }
            if (Properties.Settings.Default.BackupFolder.Length == 0)
            {
                logMessage("Backup folder not set; reverting to default.");
                Properties.Settings.Default.BackupFolder = defaultBackupFolder;
                Properties.Settings.Default.Save();
            }
            else if (!Directory.Exists(Properties.Settings.Default.BackupFolder) && !Properties.Settings.Default.BackupFolder.Equals(defaultBackupFolder))
            {
                logMessage($"Backup folder {Properties.Settings.Default.BackupFolder}) not found; reverting to default.");
                Properties.Settings.Default.BackupFolder = defaultBackupFolder;
                Properties.Settings.Default.Save();
            }

            saveDirPath = Properties.Settings.Default.SaveFolder;
            txtSaveFolder.Text = saveDirPath;
            backupDirPath = Properties.Settings.Default.BackupFolder;
            txtBackupFolder.Text = backupDirPath;

            txtFtpImport.Text = "ftp://" + Properties.Settings.Default.FtpIpAddress + ":" + Properties.Settings.Default.FtpPort + "/" + Properties.Settings.Default.FtpFilePath;

            // start the directory syncing if user has the correct settings for it
            syncDirectoriesAsync();

            chkCreateLogFile.IsChecked = Properties.Settings.Default.CreateLogFile;

            saveTimer = new System.Timers.Timer();
            saveTimer.Interval = 2000;
            saveTimer.AutoReset = false;
            saveTimer.Elapsed += OnSaveTimerElapsed;

            worldWatcher = new FileSystemWatcher();
            if (Directory.Exists($@"{saveDirPath}\worlds"))
            {
                worldWatcher.Path = $@"{saveDirPath}\worlds";
            } else
            {
                logMessage($@"Folder {saveDirPath}\worlds does not exist. Please set the correct location of your save files.", LogType.Error);
            }

            // Watch for changes in LastWrite times.
            worldWatcher.NotifyFilter = NotifyFilters.LastWrite;

            // Only watch .db files.
            worldWatcher.Filter = "*.db";

            // Add event handlers.
            worldWatcher.Changed += OnSaveFileChanged;
            worldWatcher.Created += OnSaveFileChanged;

            charWatcher = new FileSystemWatcher();
            if (Directory.Exists($@"{ saveDirPath}\characters"))
            {
                charWatcher.Path = $@"{saveDirPath}\characters";
            }
            else
            {
                logMessage($@"Folder {saveDirPath}\characters does not exist. Please set the correct location of your save files.", LogType.Error);
            }

            // Watch for changes in LastWrite times.
            charWatcher.NotifyFilter = NotifyFilters.LastWrite;

            // Only watch .db files.
            charWatcher.Filter = "*.fch";

            // Add event handlers.
            charWatcher.Changed += OnSaveFileChanged;
            charWatcher.Created += OnSaveFileChanged;

            listBackups = new List<SaveBackup>();

            ((MenuItem)dataBackups.ContextMenu.Items[0]).Click += deleteMenuItem_Click;

            dataBackups.CanUserDeleteRows = false;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            txtLog.IsReadOnly = true;
            //logMessage("Current save date: " + File.GetLastWriteTime(saveDirPath + "\\profile.sav").ToString());
            //logMessage("Backups folder: " + backupDirPath);
            //logMessage("Save folder: " + saveDirPath);
            loadBackups();
            bool autoBackup = Properties.Settings.Default.AutoBackup;
            chkAutoBackup.IsChecked = autoBackup;
            txtBackupMins.Text = Properties.Settings.Default.BackupMinutes.ToString();
            txtBackupLimit.Text = Properties.Settings.Default.BackupLimit.ToString();
            chkAutoCheckUpdate.IsChecked = Properties.Settings.Default.AutoCheckUpdate;

            if (!worldWatcher.Path.Equals(""))
            {
                worldWatcher.EnableRaisingEvents = true;
            }
            if (!charWatcher.Path.Equals(""))
            {
                charWatcher.EnableRaisingEvents = true;
            }

            if (Properties.Settings.Default.AutoCheckUpdate)
            {
                checkForUpdate();
            }
            if (BackupIsCurrent) BackupIsCurrent = true;
        }

        private void loadBackups()
        {
            if (!Directory.Exists(backupDirPath))
            {
                logMessage("Backups folder not found, creating...");
                Directory.CreateDirectory(backupDirPath);
                Directory.CreateDirectory($@"{backupDirPath}\worlds");
                Directory.CreateDirectory($@"{backupDirPath}\characters");
            }
            dataBackups.ItemsSource = null;
            listBackups.Clear();
            Dictionary<long, string> backupWorldNames = getBackupNames("World");
            Dictionary<long, bool> backupWorldKeeps = getBackupKeeps("World");
            
            string[] worldBackups = Directory.GetDirectories(backupDirPath + "\\worlds");
            foreach (string w in worldBackups)
            {
                string name = w.Replace($@"{backupDirPath}\worlds", "");
                string[] backupDirs = Directory.GetDirectories(w);
                foreach (string backupDir in backupDirs)
                {
                    SaveBackup backup = new SaveBackup($@"{backupDir}\{name}.db");
                    if (backupWorldNames.ContainsKey(backup.SaveDate.Ticks))
                    {
                        backup.Label = backupWorldNames[backup.SaveDate.Ticks];
                    }
                    if (backupWorldKeeps.ContainsKey(backup.SaveDate.Ticks))
                    {
                        backup.Keep = backupWorldKeeps[backup.SaveDate.Ticks];
                    }

                    backup.Updated += saveUpdated;

                    listBackups.Add(backup);
                }
            }

            Dictionary<long, string> backupCharNames = getBackupNames("Character");
            Dictionary<long, bool> backupCharKeeps = getBackupKeeps("Character");
            string[] charBackups = Directory.GetDirectories($@"{backupDirPath}\characters");
            foreach (string c in charBackups)
            {
                string name = c.Replace($@"{backupDirPath}\characters", "");
                string[] backupDirs = Directory.GetDirectories(c);
                foreach (string backupDir in backupDirs)
                {
                    SaveBackup backup = new SaveBackup($@"{backupDir}\{name}.fch");
                    if (backupCharNames.ContainsKey(backup.SaveDate.Ticks))
                    {
                        backup.Label = backupCharNames[backup.SaveDate.Ticks];
                    }
                    if (backupCharKeeps.ContainsKey(backup.SaveDate.Ticks))
                    {
                        backup.Keep = backupCharKeeps[backup.SaveDate.Ticks];
                    }

                    backup.Updated += saveUpdated;

                    listBackups.Add(backup);
                }
            }
            listBackups.Sort();
            dataBackups.ItemsSource = listBackups;
            logMessage($"Backups found: {listBackups.Count}");
            if (listBackups.Count > 0)
            {
                logMessage("Last backup save date: " + listBackups[listBackups.Count - 1].SaveDate.ToString());
            }

            //dataBackups.SelectedItem = activeBackup;
        }

        private void saveUpdated(object sender, UpdatedEventArgs args)
        {
            if (args.FieldName.Equals("Label"))
            {
                updateSavedLabels();
            }
            else if (args.FieldName.Equals("Keep"))
            {
                updateSavedKeeps();
            }
        }

        private void loadBackups(Boolean verbose)
        {
            Boolean oldVal = suppressLog;
            suppressLog = !verbose;
            loadBackups();
            suppressLog = oldVal;
        }

        public void logMessage(string msg)
        {
            logMessage(msg, Colors.White);
        }

        public void logMessage(string msg, LogType lt)
        {
            Color color = Colors.White;
            if (lt == LogType.Success)
            {
                color = Color.FromRgb(0, 200, 0);
            }
            else if (lt == LogType.Error)
            {
                color = Color.FromRgb(200, 0, 0);
            }
            logMessage(msg, color);
        }

        public void logMessage(string msg, Color color)
        {
            if (!suppressLog)
            {
                txtLog.Text = txtLog.Text + Environment.NewLine + DateTime.Now.ToString() + ": " + msg;
                lblLastMessage.Content = msg;
                lblLastMessage.Foreground = new SolidColorBrush(color);
                if (color.Equals(Colors.White))
                {
                    lblLastMessage.FontWeight = FontWeights.Normal;
                }
                else
                {
                    lblLastMessage.FontWeight = FontWeights.Bold;
                }
            }
            if (Properties.Settings.Default.CreateLogFile)
            {
                StreamWriter writer = System.IO.File.AppendText("log.txt");
                writer.WriteLine(DateTime.Now.ToString() + ": " + msg);
                writer.Close();
            }
        }

        private void BtnBackup_Click(object sender, RoutedEventArgs e)
        {
            string[] worlds = Directory.GetFiles($@"{saveDirPath}\worlds", "*.db");
            foreach (string save in worlds)
            {
                doBackup(save);
            }
            string[] characters = Directory.GetFiles($@"{saveDirPath}\characters", "*.fch");
            foreach (string save in characters)
            {
                doBackup(save);
            }
            if (this.BackupIsCurrent)
            {
                this.BackupIsCurrent = true;
            }
            //doBackup();
        }

        private void doBackup(string savepath)
        {
            SaveFile save = new SaveFile(savepath);
            if (!save.BackedUp)
            {
                SaveBackup backup = save.PerformBackup();
                if (backup != null)
                {
                    listBackups.Add(backup);
                    checkBackupLimits();
                    dataBackups.Items.Refresh();
                    if (this.BackupIsCurrent)
                    {
                        this.BackupIsCurrent = true;
                    }
                    logMessage($"Backup of {backup.Type.ToLower()} {backup.Name} completed!", LogType.Success);
                }
                else
                {
                    logMessage($"Backup of {save.Type.ToLower()} {save.Name} failed!", LogType.Error);
                }
            }
        }

        private Boolean isValheimRunning()
        {
            Process[] pname = Process.GetProcessesByName("valheim");
            if (pname.Length == 0)
            {
                return false;
            }
            return true;
        }
        private Boolean isValheimServerRunning()
        {
            Process[] pname = Process.GetProcessesByName("valheim_server");
            if (pname.Length == 0)
            {
                return false;
            }
            return true;
        }

        private void BtnRestore_Click(object sender, RoutedEventArgs e)
        {
            if (isValheimRunning())
            {
                logMessage("Exit the game before restoring a save backup.", LogType.Error);
                return;
            }

            if (dataBackups.SelectedItem == null)
            {
                logMessage("Choose a backup to restore from the list!", LogType.Error);
                return;
            }
            SaveBackup selectedBackup = (SaveBackup)dataBackups.SelectedItem;
            if (selectedBackup.Type.Equals("World") && isValheimServerRunning())
            {
                logMessage("Stop the game server before restoring a world backup.", LogType.Error);
                return;
            }
            if (selectedBackup.Active)
            {
                logMessage("That backup is already active. No need to restore.");
                return;
            }
            if (File.Exists(selectedBackup.ActivePath))
            {
                //check if active save is backed up
                SaveFile save = new SaveFile(selectedBackup.ActivePath);
                if (!save.BackedUp)
                {
                    doBackup(save.FullPath);
                }
            }
            worldWatcher.EnableRaisingEvents = false;
            charWatcher.EnableRaisingEvents = false;
            //File.Copy(selectedBackup.FullPath, selectedBackup.ActivePath);
            selectedBackup.Restore();
            dataBackups.Items.Refresh();
            btnRestore.IsEnabled = false;
            btnRestore.Content = FindResource("RestoreGrey");
            logMessage(selectedBackup.Name+" backup restored!", LogType.Success);
            worldWatcher.EnableRaisingEvents = Properties.Settings.Default.AutoBackup;
            charWatcher.EnableRaisingEvents = Properties.Settings.Default.AutoBackup;
        }

        private void ChkAutoBackup_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.AutoBackup = chkAutoBackup.IsChecked.HasValue ? chkAutoBackup.IsChecked.Value : false;
            Properties.Settings.Default.Save();
            worldWatcher.EnableRaisingEvents = Properties.Settings.Default.AutoBackup;
            charWatcher.EnableRaisingEvents = Properties.Settings.Default.AutoBackup;
        }

        private void OnSaveFileChanged(object source, FileSystemEventArgs e)
        {
            // Specify what is done when a file is changed, created, or deleted.
            this.Dispatcher.Invoke(() =>
            {
                try
                {
                    if (Properties.Settings.Default.AutoBackup)
                    {
                        SaveFile save = new SaveFile(e.FullPath);
                        if (save.Type.Equals("World"))
                        {
                            if (save.NeedsBackedUp)
                            {
                                doBackup(e.FullPath);
                            }
                            else
                            {
                                this.BackupIsCurrent = false;
                                dataBackups.Items.Refresh();
                                TimeSpan span = (save.BackupDueTime - DateTime.Now);
                                logMessage($"Save change detected, but {span.Minutes + Math.Round(span.Seconds / 60.0, 2)} minutes, left until next backup");
                            }
                        } else
                        {
                            //When character saves are modified, they are modified
                            //two times in relatively rapid succession.
                            //This timer is refreshed each time the save is modified,
                            //and a backup only occurs after the timer expires.
                            charSaveForBackup = save;
                            saveTimer.Interval = 3000;
                            saveTimer.Enabled = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    logMessage($"{ex.GetType()} setting save file timer: {ex.Message}({ex.StackTrace})");
                }
            });
        }

        private void OnSaveTimerElapsed(Object source, System.Timers.ElapsedEventArgs e)
        {
            this.Dispatcher.Invoke(() =>
            {
                try
                {
                    if (charSaveForBackup.NeedsBackedUp)
                    {
                        doBackup(charSaveForBackup.FullPath);
                        charSaveForBackup = null;
                    }
                    else
                    {
                        this.BackupIsCurrent = false;
                        dataBackups.Items.Refresh();
                        TimeSpan span = (charSaveForBackup.BackupDueTime - DateTime.Now);
                        logMessage($"Save change detected, but {span.Minutes + Math.Round(span.Seconds / 60.0, 2)} minutes, left until next backup");
                    }
                }
                catch (Exception ex)
                {
                    logMessage($"{ex.GetType()} processing save file change: {ex.Message} ({ex.StackTrace})");
                }
            });
        }

        private void TxtBackupMins_LostFocus(object sender, RoutedEventArgs e)
        {
            updateBackupMins();
        }

        private void TxtBackupMins_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                updateBackupMins();
            }
        }

        private void updateBackupMins()
        {
            string txt = txtBackupMins.Text;
            int mins;
            bool valid = false;
            if (txt.Length > 0)
            {
                if (int.TryParse(txt, out mins))
                {
                    valid = true;
                }
                else
                {
                    mins = Properties.Settings.Default.BackupMinutes;
                }
            }
            else
            {
                mins = Properties.Settings.Default.BackupMinutes;
            }
            if (mins != Properties.Settings.Default.BackupMinutes)
            {
                Properties.Settings.Default.BackupMinutes = mins;
                Properties.Settings.Default.Save();
            }
            if (!valid)
            {
                txtBackupMins.Text = Properties.Settings.Default.BackupMinutes.ToString();
            }
        }

        private void TxtBackupLimit_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                updateBackupLimit();
            }
        }

        private void TxtBackupLimit_LostFocus(object sender, RoutedEventArgs e)
        {
            updateBackupLimit();
        }

        private void updateBackupLimit()
        {
            string txt = txtBackupLimit.Text;
            int num;
            bool valid = false;
            if (txt.Length > 0)
            {
                if (int.TryParse(txt, out num))
                {
                    valid = true;
                }
                else
                {
                    num = Properties.Settings.Default.BackupLimit;
                }
            }
            else
            {
                num = 0;
            }
            if (num != Properties.Settings.Default.BackupLimit)
            {
                Properties.Settings.Default.BackupLimit = num;
                Properties.Settings.Default.Save();
            }
            if (!valid)
            {
                txtBackupLimit.Text = Properties.Settings.Default.BackupLimit.ToString();
            }
        }

        private void checkBackupLimits()
        {
            if (Properties.Settings.Default.BackupLimit > 0)
            {
                listBackups.Sort();
                Dictionary<string, Dictionary<string, List<SaveBackup>>> backups = new Dictionary<string, Dictionary<string, List<SaveBackup>>>();
                foreach (SaveBackup backup in listBackups)
                {
                    if (!backups.ContainsKey(backup.Type))
                    {
                        backups.Add(backup.Type, new Dictionary<string, List<SaveBackup>>());
                    }
                    if (!backups[backup.Type].ContainsKey(backup.Name))
                    {
                        backups[backup.Type].Add(backup.Name, new List<SaveBackup>());
                    }
                    backups[backup.Type][backup.Name].Add(backup);
                }
                List<SaveBackup> removeBackups = new List<SaveBackup>();
                foreach (string backupType in backups.Keys)
                {
                    foreach (string saveName in backups[backupType].Keys)
                    {
                        if (backups[backupType][saveName].Count > Properties.Settings.Default.BackupLimit)
                        {
                            int delNum = backups[backupType][saveName].Count - Properties.Settings.Default.BackupLimit;
                            for (int i = 0; i < backups[backupType][saveName].Count && delNum > 0; i++)
                            {
                                SaveBackup backup = backups[backupType][saveName][i];
                                if (!backup.Keep && !backup.Active)
                                {
                                    logMessage($"Deleting excess backup {backup.Label} ({backup.SaveDate})");
                                    Directory.Delete(backup.Folder, true);
                                    removeBackups.Add(backup);
                                    delNum--;
                                }
                            }
                        }
                    }
                }

                for (int i=0; i < removeBackups.Count; i++)
                {
                    listBackups.Remove(removeBackups[i]);
                }
            }
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (!Directory.Exists(backupDirPath))
            {
                logMessage("Backups folder not found, creating...");
                Directory.CreateDirectory(backupDirPath);
            }
            Process.Start(backupDirPath+"\\");
        }

        private Dictionary<long, string> getBackupNames(string type)
        {
            Dictionary<long, string> names = new Dictionary<long, string>();
            string savedString = "";
            if (type.Equals("World")) {
                savedString = Properties.Settings.Default.WorldBackupLabel;
            } else
            {
                savedString = Properties.Settings.Default.CharBackupLabel;
            }
            string[] savedNames = savedString.Split(',');
            for (int i = 0; i < savedNames.Length; i++)
            {
                string[] vals = savedNames[i].Split('=');
                if (vals.Length == 2)
                {
                    names.Add(long.Parse(vals[0]), System.Net.WebUtility.UrlDecode(vals[1]));
                }
            }
            return names;
        }

        private Dictionary<long, bool> getBackupKeeps(string type)
        {
            Dictionary<long, bool> keeps = new Dictionary<long, bool>();
            string savedString = "";
            if (type.Equals("World")) {
                savedString = Properties.Settings.Default.WorldBackupKeep;
            } else
            {
                savedString = Properties.Settings.Default.CharBackupKeep;
            }
            string[] savedKeeps = savedString.Split(',');
            for (int i = 0; i < savedKeeps.Length; i++)
            {
                string[] vals = savedKeeps[i].Split('=');
                if (vals.Length == 2)
                {
                    keeps.Add(long.Parse(vals[0]), bool.Parse(vals[1]));
                }
            }
            return keeps;
        }

        private void DataBackups_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (e.Column.Header.ToString().Equals("Name") || 
                e.Column.Header.ToString().Equals("Type") ||
                e.Column.Header.ToString().Equals("SaveDate") ||
                e.Column.Header.ToString().Equals("Active")) e.Cancel = true;
        }

        private void DataBackups_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.Column.Header.ToString().Equals("Name") && e.EditAction == DataGridEditAction.Commit)
            {
                SaveBackup sb = (SaveBackup)e.Row.Item;
                if (sb.Label.Equals(""))
                {
                    sb.Label = sb.SaveDate.Ticks.ToString();
                }
            }
        }

        private void updateSavedLabels()
        {
            List<string> savedWorldLabels = new List<string>();
            List<string> savedCharLabels = new List<string>();
            for (int i = 0; i < listBackups.Count; i++)
            {
                SaveBackup s = listBackups[i];
                if (!s.Label.Equals(s.DefaultLabel))
                {
                    if (s.Type.Equals("World"))
                    {
                        savedWorldLabels.Add(s.SaveDate.Ticks + "=" + System.Net.WebUtility.UrlEncode(s.Label));
                    }
                    else
                    {
                        savedCharLabels.Add(s.SaveDate.Ticks + "=" + System.Net.WebUtility.UrlEncode(s.Label));
                    }
                }
                else
                {
                }
            }
            if (savedWorldLabels.Count > 0)
            {
                Properties.Settings.Default.WorldBackupLabel = string.Join(",", savedWorldLabels.ToArray());
            }
            else
            {
                Properties.Settings.Default.WorldBackupLabel = "";
            }
            if (savedCharLabels.Count > 0)
            {
                Properties.Settings.Default.CharBackupLabel = string.Join(",", savedCharLabels.ToArray());
            }
            else
            {
                Properties.Settings.Default.CharBackupLabel = "";
            }
            Properties.Settings.Default.Save();
        }

        private void updateSavedKeeps()
        {
            List<string> savedWorldKeeps = new List<string>();
            List<string> savedCharKeeps = new List<string>();
            for (int i = 0; i < listBackups.Count; i++)
            {
                SaveBackup s = listBackups[i];
                if (s.Keep)
                {
                    if (s.Type.Equals("World"))
                    {
                        savedWorldKeeps.Add(s.SaveDate.Ticks + "=True");
                    } else
                    {
                        savedCharKeeps.Add(s.SaveDate.Ticks + "=True");
                    }
                }
            }
            if (savedWorldKeeps.Count > 0)
            {
                Properties.Settings.Default.WorldBackupKeep = string.Join(",", savedWorldKeeps.ToArray());
            }
            else
            {
                Properties.Settings.Default.WorldBackupKeep = "";
            }
            if (savedCharKeeps.Count > 0)
            {
                Properties.Settings.Default.CharBackupKeep = string.Join(",", savedCharKeeps.ToArray());
            }
            else
            {
                Properties.Settings.Default.CharBackupKeep = "";
            }
            Properties.Settings.Default.Save();
        }

        private void DataBackups_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            MenuItem deleteMenu = ((MenuItem)dataBackups.ContextMenu.Items[0]);
            if (e.AddedItems.Count > 0)
            {
                SaveBackup selectedBackup = (SaveBackup)(dataBackups.SelectedItem);
                if (selectedBackup.Active)
                {
                    btnRestore.IsEnabled = false;
                    btnRestore.Content = FindResource("RestoreGrey");
                }
                else
                {
                    btnRestore.IsEnabled = true;
                    btnRestore.Content = FindResource("Restore");
                }

                deleteMenu.IsEnabled = true;
            }
            else
            {
                deleteMenu.IsEnabled = false;
                btnRestore.IsEnabled = false;
                btnRestore.Content = FindResource("RestoreGrey");
            }
        }

        private void deleteMenuItem_Click(object sender, System.EventArgs e)
        {
            SaveBackup save = (SaveBackup)dataBackups.SelectedItem;
            var confirmResult = MessageBox.Show($"Are you sure to delete backup \"{save.Label}\" ({save.SaveDate.ToString()})?",
                                     "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (confirmResult == MessageBoxResult.Yes)
            {
                if (save.Keep)
                {
                    confirmResult = MessageBox.Show($"This backup is marked for keeping. Are you SURE to delete backup \"{save.Label}\" ({save.SaveDate.ToString()})?",
                                     "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                    if (confirmResult != MessageBoxResult.Yes)
                    {
                        return;
                    }
                }
                if (save.Active)
                {
                    this.BackupIsCurrent = false;
                }
                if (Directory.Exists(save.Folder))
                {
                    Directory.Delete(save.Folder, true);
                }
                listBackups.Remove(save);
                dataBackups.Items.Refresh();
                logMessage($"Backup \"{save.Label}\" ({save.SaveDate}) deleted.");
            }
        }

        private void checkForUpdate()
        {
            new Thread(() =>
            {
                Thread.CurrentThread.IsBackground = true;

                try
                {
                    WebClient client = new WebClient();
                    string source = client.DownloadString("https://github.com/Razzmatazzz/ValheimSaveShield/releases/latest");
                    string title = Regex.Match(source, @"\<title\b[^>]*\>\s*(?<Title>[\s\S]*?)\</title\>", RegexOptions.IgnoreCase).Groups["Title"].Value;
                    string remoteVer = Regex.Match(source, @"Valheim Save Shield (?<Version>([\d.]+)?)", RegexOptions.IgnoreCase).Groups["Version"].Value;

                    Version remoteVersion = new Version(remoteVer);
                    Version localVersion = typeof(MainWindow).Assembly.GetName().Version;

                    this.Dispatcher.Invoke(() =>
                    {
                        //do stuff in here with the interface
                        if (localVersion.CompareTo(remoteVersion) == -1)
                        {
                            var confirmResult = MessageBox.Show("There is a new version available. Would you like to open the download page?",
                                     "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                            if (confirmResult == MessageBoxResult.Yes)
                            {
                                Process.Start("https://github.com/Razzmatazzz/ValheimSaveShield/releases/latest");
                                System.Environment.Exit(1);
                            }
                        } else
                        {
                            //logMessage("No new version found.");
                        }
                    });
                }
                catch (Exception ex)
                {
                    this.Dispatcher.Invoke(() =>
                    {
                        logMessage($"Error checking for new version: {ex.Message}", LogType.Error);
                    });
                }
            }).Start();
            lastUpdateCheck = DateTime.Now;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            //any cleanup to do before exit
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            System.Environment.Exit(1);
        }

        private void BtnBackupFolder_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Forms.FolderBrowserDialog openFolderDialog = new System.Windows.Forms.FolderBrowserDialog();
            openFolderDialog.SelectedPath = backupDirPath;
            openFolderDialog.Description = "Select the folder where you want your backups kept.";
            System.Windows.Forms.DialogResult result = openFolderDialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                string folderName = openFolderDialog.SelectedPath;
                if (folderName.Equals(saveDirPath))
                {
                    MessageBox.Show("Please select a folder other than the game's save folder.",
                                     "Invalid Folder", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK);
                    return;
                }
                if (folderName.Equals(backupDirPath))
                {
                    return;
                }
                if (listBackups.Count > 0)
                {
                    var confirmResult = MessageBox.Show("Do you want to move your backups to this new folder?",
                                     "Move Backups", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                    if (confirmResult == MessageBoxResult.Yes)
                    {
                        CopyFolder(new DirectoryInfo(backupDirPath), new DirectoryInfo(folderName));
                        List<String> backupFolders = Directory.GetDirectories(backupDirPath).ToList();
                        foreach (string file in backupFolders)
                        {
                            /*string subFolderName = file.Substring(file.LastIndexOf(@"\"));
                            Directory.CreateDirectory(folderName + subFolderName);
                            Directory.SetCreationTime(folderName + subFolderName, Directory.GetCreationTime(file));
                            Directory.SetLastWriteTime(folderName + subFolderName, Directory.GetCreationTime(file));
                            foreach (string filename in Directory.GetFiles(file))
                            {
                                File.Copy(filename, filename.Replace(backupDirPath, folderName));
                            }*/
                            Directory.Delete(file, true);
                        }
                    }
                }
                txtBackupFolder.Text = folderName;
                backupDirPath = folderName;
                Properties.Settings.Default.BackupFolder = folderName;
                Properties.Settings.Default.Save();
                loadBackups();
            }
        }

        public static void CopyFolder(DirectoryInfo source, DirectoryInfo target)
        {
            foreach (DirectoryInfo dir in source.GetDirectories())
                CopyFolder(dir, target.CreateSubdirectory(dir.Name));
            foreach (FileInfo file in source.GetFiles())
                file.CopyTo(System.IO.Path.Combine(target.FullName, file.Name));
        }

        private void DataBackups_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            if (e.Column.Header.Equals("DefaultLabel")) {
                e.Cancel = true;
            } 
            else if (e.Column.Header.Equals("FileName"))
            {
                e.Cancel = true;
            }
            else if (e.Column.Header.Equals("FullPath"))
            {
                e.Cancel = true;
            }
            else if (e.Column.Header.Equals("Folder"))
            {
                e.Cancel = true;
            }
            else if (e.Column.Header.Equals("ActivePath"))
            {
                e.Cancel = true;
            }
            else if (e.Column.Header.Equals("SaveDate"))
            {
                //e.Column.SortDirection = System.ComponentModel.ListSortDirection.Ascending;
            }
        }

        private void btnGameInfoUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (lastUpdateCheck.AddMinutes(10) < DateTime.Now)
            {
                checkForUpdate();
            }
            else
            {
                TimeSpan span = (lastUpdateCheck.AddMinutes(10) - DateTime.Now);
                logMessage($"Please wait {span.Minutes} minutes, {span.Seconds} seconds before checking for update.");
            }
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            //need to call twice for some reason
            dataBackups.CancelEdit();
            dataBackups.CancelEdit();
        }

        private void chkCreateLogFile_Click(object sender, RoutedEventArgs e)
        {
            bool newValue = chkCreateLogFile.IsChecked.HasValue ? chkCreateLogFile.IsChecked.Value : false;
            if (newValue & !Properties.Settings.Default.CreateLogFile)
            {
                System.IO.File.WriteAllText("log.txt", DateTime.Now.ToString() + ": Version " + typeof(MainWindow).Assembly.GetName().Version + "\r\n");
            }
            Properties.Settings.Default.CreateLogFile = newValue;
            Properties.Settings.Default.Save();
        }

        private void chkAutoCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            bool newValue = chkAutoCheckUpdate.IsChecked.HasValue ? chkAutoCheckUpdate.IsChecked.Value : false;
            Properties.Settings.Default.AutoCheckUpdate = newValue;
            Properties.Settings.Default.Save();
        }

        private void btnSaveFolder_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Forms.FolderBrowserDialog openFolderDialog = new System.Windows.Forms.FolderBrowserDialog();
            openFolderDialog.SelectedPath = saveDirPath;
            openFolderDialog.Description = "Select where your Valheim saves are stored.";
            System.Windows.Forms.DialogResult result = openFolderDialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                string folderName = openFolderDialog.SelectedPath;
                if (folderName.Equals(backupDirPath))
                {
                    MessageBox.Show("Please select a folder other than the backup folder.",
                                     "Invalid Folder", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK);
                    return;
                }
                if (folderName.Equals(saveDirPath))
                {
                    return;
                }
                if (!Directory.Exists($@"{folderName}\worlds") || !Directory.Exists($@"{folderName}\characters"))
                {
                    MessageBox.Show("Please select the folder where your Valheim save files are located. This folder should contain both a \"worlds\" and a \"characters\" folder..",
                                     "Invalid Folder", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK);
                    return;
                }
                txtSaveFolder.Text = folderName;
                saveDirPath = folderName;
                worldWatcher.Path = $@"{saveDirPath}\worlds";
                charWatcher.Path = $@"{saveDirPath}\characters";
                worldWatcher.EnableRaisingEvents = true;
                charWatcher.EnableRaisingEvents = true;
                Properties.Settings.Default.SaveFolder = folderName;
                Properties.Settings.Default.Save();
            }
        }

        private void btnFtpImport_Click(object sender, RoutedEventArgs e)
        {
            GetFtpSettings();

            if (ftpDirectorySync == null)
            {
                System.Diagnostics.Debug.WriteLine("btnFtpImport_Click sync");
                syncDirectoriesAsync();
            }
        }

        private void syncDirectoriesAsync()
        {
            
            // asynchronously sync local directory with ftp
            ftpDirectorySync = new Thread(() => {
                while (true)
                {
                    if (Properties.Settings.Default.FtpIpAddress.Length == 0
                        || Properties.Settings.Default.FtpPort.Length == 0
                        || Properties.Settings.Default.FtpFilePath.Length == 0
                        || Properties.Settings.Default.SaveFolder.Length == 0
                        || Properties.Settings.Default.FtpUsername.Length == 0
                        || Properties.Settings.Default.FtpPassword.Length == 0
                    )
                    {
                        System.Diagnostics.Debug.WriteLine("exiting sync thread");
                        ftpDirectorySync = null;
                        break;
                    }

                    System.Diagnostics.Debug.WriteLine("re-syncing");
                    SynchronizeDirectories.remoteSync(
                        Properties.Settings.Default.FtpIpAddress,
                        Properties.Settings.Default.FtpPort,
                        '/' + Properties.Settings.Default.FtpFilePath,
                        Properties.Settings.Default.SaveFolder + "\\worlds",
                        Properties.Settings.Default.FtpUsername,
                        Properties.Settings.Default.FtpPassword
                    );
                    Thread.Sleep(Properties.Settings.Default.BackupMinutes * 60000);
                }
            });

            if (ftpDirectorySync != null)
            {
                ftpDirectorySync.Start();
            }
        }

        private bool GetFtpSettings()
        {
            System.Windows.Forms.Form prompt = new System.Windows.Forms.Form()
            {
                Width = 500,
                Height = 500,
                FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog,
                Text = "FTP Import",
                StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen
            };
            System.Windows.Forms.Label ipLabel = new System.Windows.Forms.Label() { Left = 50, Top = 20, Text = "Server IP" };
            System.Windows.Forms.TextBox ipBox = new System.Windows.Forms.TextBox() { Left = 50, Top = 50, Width = 400 };
            System.Windows.Forms.Label portLabel = new System.Windows.Forms.Label() { Left = 50, Top = 100, Text = "Port" };
            System.Windows.Forms.TextBox portBox = new System.Windows.Forms.TextBox() { Left = 50, Top = 130, Width = 400 };
            System.Windows.Forms.Label filePathLabel = new System.Windows.Forms.Label() { Left = 50, Top = 180, Text = "Worlds Path" };
            System.Windows.Forms.TextBox filePathBox = new System.Windows.Forms.TextBox() { Left = 50, Top = 210, Width = 400 };
            System.Windows.Forms.Label usernameLabel = new System.Windows.Forms.Label() { Left = 50, Top = 260, Text = "Username" };
            System.Windows.Forms.TextBox usernameBox = new System.Windows.Forms.TextBox() { Left = 50, Top = 290, Width = 400 };
            System.Windows.Forms.Label passwordLabel = new System.Windows.Forms.Label() { Left = 50, Top = 340, Text = "Password" };
            System.Windows.Forms.TextBox passwordBox = new System.Windows.Forms.TextBox() { Left = 50, Top = 370, Width = 400 };

            System.Windows.Forms.Button confirmation = new System.Windows.Forms.Button() { Text = "Import Worlds", Left = 350, Width = 100, Top = 400, DialogResult = System.Windows.Forms.DialogResult.OK };
            confirmation.Click += (sender, e) => { prompt.Close(); };

            // load defaults
            ipBox.Text = Properties.Settings.Default.FtpIpAddress;
            portBox.Text = Properties.Settings.Default.FtpPort;
            filePathBox.Text = Properties.Settings.Default.FtpFilePath;
            usernameBox.Text = Properties.Settings.Default.FtpUsername;
            passwordBox.Text = Properties.Settings.Default.FtpPassword;

            prompt.Controls.Add(ipLabel);
            prompt.Controls.Add(ipBox);
            prompt.Controls.Add(portLabel);
            prompt.Controls.Add(portBox);
            prompt.Controls.Add(filePathLabel);
            prompt.Controls.Add(filePathBox);
            prompt.Controls.Add(usernameLabel);
            prompt.Controls.Add(usernameBox);
            prompt.Controls.Add(passwordLabel);
            prompt.Controls.Add(passwordBox);

            prompt.Controls.Add(confirmation);
            prompt.AcceptButton = confirmation;

            System.Windows.Forms.DialogResult dialog = prompt.ShowDialog();

            if (dialog == System.Windows.Forms.DialogResult.OK)
            {
                if (filePathBox.Text.StartsWith("/"))
                {
                    filePathBox.Text = filePathBox.Text.Remove(0, 1);
                }

                if (filePathBox.Text.EndsWith("/"))
                {
                    filePathBox.Text = filePathBox.Text.Remove(filePathBox.Text.Length - 1, 1);
                }

                Properties.Settings.Default.FtpIpAddress = ipBox.Text;
                Properties.Settings.Default.FtpPort = portBox.Text;
                Properties.Settings.Default.FtpFilePath = filePathBox.Text;
                Properties.Settings.Default.FtpUsername = usernameBox.Text;
                Properties.Settings.Default.FtpPassword = passwordBox.Text;
                Properties.Settings.Default.Save();

                txtFtpImport.Text = "ftp://" + Properties.Settings.Default.FtpIpAddress + ":" + Properties.Settings.Default.FtpPort + "/" + Properties.Settings.Default.FtpFilePath;
            }
            return dialog == System.Windows.Forms.DialogResult.OK;
        }
    }
}