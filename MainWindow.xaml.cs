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
using System.Windows.Documents;
using ModernWpf;
using System.Timers;
using System.Collections.Specialized;
using System.Collections;
using RazzTools;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace ValheimSaveShield
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static string DefaultBackupFolder { get { return $@"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}\AppData\LocalLow\IronGate\Valheim\backups"; } }
        private static string DefaultSaveFolder { get { return $@"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}\AppData\LocalLow\IronGate\Valheim"; } }
        private List<SaveBackup> listBackups;
        private Boolean suppressLog;
        private Color defaultTextColor;
        private List<SaveWatcher> saveWatchers;

        private Dictionary<string, SaveTimer> saveTimers;
        private DateTime lastUpdateCheck;
        private System.Windows.Forms.NotifyIcon notifyIcon;
        private WindowState storedWindowState;

        private Thread ftpDirectorySync = null;

        private readonly Mutex _mutex;
        private const string mutexName = "MUTEX_VALHEIMSAVESHIELD";

        private bool IsBackupCurrent {
            get {
                foreach (var saveDirPath in Properties.Settings.Default.SaveFolders)
                {
                    if (!Directory.Exists($@"{saveDirPath}\worlds_local"))
                    {
                        return false;
                    }
                    var worlds = Directory.GetFiles($@"{saveDirPath}\worlds_local", "*.db");
                    foreach (string world in worlds)
                    {
                        SaveFile save = new SaveFile(world);
                        if (!save.BackedUp)
                        {
                            return false;
                        }
                    }
                    var characters = Directory.GetFiles($@"{saveDirPath}\characters_local", "*.fch");
                    foreach (string character in characters)
                    {
                        SaveFile save = new SaveFile(character);
                        if (!save.BackedUp)
                        {
                            return false;
                        }
                    }
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
        private StringCollection SavePaths
        {
            get
            {
                return Properties.Settings.Default.SaveFolders;
            }
        }

        ~MainWindow()
        {
            if (notifyIcon != null)
            {
                notifyIcon.Dispose();
            }
            if (ftpDirectorySync != null)
            {
                ftpDirectorySync.Abort();
            }
            if (_mutex != null)
            {
                _mutex.Dispose();
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            bool firstInstance;
            _mutex = new Mutex(true, mutexName, out firstInstance);
            if (!firstInstance)
            {
                NativeMethods.PostMessage(
                    (IntPtr)NativeMethods.HWND_BROADCAST,
                    NativeMethods.WM_SHOWME,
                    IntPtr.Zero,
                    IntPtr.Zero
                );
                Close();
                return;
            }
            suppressLog = false;
            if (Properties.Settings.Default.CreateLogFile)
            {
                System.IO.File.WriteAllText("log.txt", "");
            }
            defaultTextColor = ((SolidColorBrush)txtLog.Foreground).Color;
            txtLog.Document.Blocks.Clear();
            logMessage($"Version {typeof(MainWindow).Assembly.GetName().Version}");
            if (Properties.Settings.Default.UpgradeRequired)
            {
                Properties.Settings.Default.Upgrade();
                //logMessage($"Previous backup folder: {Properties.Settings.Default.GetPreviousVersion("BackupFolder")}");
                Properties.Settings.Default.UpgradeRequired = false;
                if (!Properties.Settings.Default.FtpFilePath.StartsWith("/"))
                {
                    Properties.Settings.Default.FtpFilePath = "/" + Properties.Settings.Default.FtpFilePath;
                }
                Properties.Settings.Default.Save();
                //logMessage($"Current backup folder: {Properties.Settings.Default.BackupFolder}");
            }
            Width = Properties.Settings.Default.MainWindowWidth;
            Height = Properties.Settings.Default.MainWindowHeight;
            if (Properties.Settings.Default.SaveFolders == null)
            {
                Properties.Settings.Default.SaveFolders = new StringCollection();
                Properties.Settings.Default.Save();
            }
            if (Properties.Settings.Default.WorldBackupLabel == null)
            {
                Properties.Settings.Default.WorldBackupLabel = new SerializableStringDictionary();
                Properties.Settings.Default.Save();
            }
            if (Properties.Settings.Default.WorldBackupKeep == null)
            {
                Properties.Settings.Default.WorldBackupKeep = new SerializableStringDictionary();
                Properties.Settings.Default.Save();
            }
            if (Properties.Settings.Default.CharBackupLabel == null)
            {
                Properties.Settings.Default.CharBackupLabel = new SerializableStringDictionary();
                Properties.Settings.Default.Save();
            }
            if (Properties.Settings.Default.CharBackupKeep == null)
            {
                Properties.Settings.Default.CharBackupKeep = new SerializableStringDictionary();
                Properties.Settings.Default.Save();
            }
            if (Properties.Settings.Default.WorldFileExtensions == null)
            {
                Properties.Settings.Default.WorldFileExtensions = new StringCollection();
                Properties.Settings.Default.Save();
            }
            saveWatchers = new List<SaveWatcher>();
            if (Properties.Settings.Default.BackupFolder == "")
            {
                logMessage("Backup folder not set; reverting to default.");
                Properties.Settings.Default.BackupFolder = DefaultBackupFolder;
                Properties.Settings.Default.Save();
            }
            else if (!Directory.Exists(Properties.Settings.Default.BackupFolder) && !Properties.Settings.Default.BackupFolder.Equals(DefaultBackupFolder))
            {
                logMessage($"Backup folder {Properties.Settings.Default.BackupFolder}) not found; reverting to default.");
                Properties.Settings.Default.BackupFolder = DefaultBackupFolder;
                Properties.Settings.Default.Save();
            }
            if (Properties.Settings.Default.SaveFolders.Count > 0)
            {
                foreach (var path in Properties.Settings.Default.SaveFolders)
                {
                    if (!Directory.Exists(path))
                    {
                        logMessage($"Save path {path} does not exist; disregarding.");
                        continue;
                    }
                    lstSaveFolders.Items.Add(path);
                    AddToSaveWatchers(path);
                }
                lstSaveFolders.Items.Refresh();
                if (lstSaveFolders.Items.Count > 1)
                {
                    lblSaveFolders.Content = "Save Folders";
                }
                else
                {
                    lblSaveFolders.Content = "Save Folder";
                }
            }
            else
            {
                logMessage("Reverting to default save folder.");
                lstSaveFolders.Items.Add(DefaultSaveFolder);
                AddToSaveWatchers(DefaultSaveFolder);

                lstSaveFolders.Items.Refresh();
                Properties.Settings.Default.SaveFolders.Add(DefaultSaveFolder);
                Properties.Settings.Default.FtpSaveDest = DefaultSaveFolder;
                Properties.Settings.Default.Save();
            }
            // start the directory syncing if user has the correct settings for it
            syncDirectoriesAsync();

            saveTimers = new Dictionary<string, SaveTimer>();

            listBackups = new List<SaveBackup>();
            notifyIcon = new System.Windows.Forms.NotifyIcon();
            notifyIcon.BalloonTipText = "VSS has been minimized. Click the tray icon to restore.";
            notifyIcon.BalloonTipClicked += NotifyIcon_Click;
            notifyIcon.Text = "Valheim Save Shield";
            this.notifyIcon.Icon = ValheimSaveShield.Properties.Resources.vss;
            notifyIcon.Click += NotifyIcon_Click;
            storedWindowState = WindowState.Normal;
        }
        //This event is raised to support interoperation with Win32
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            HwndSource source = PresentationSource.FromVisual(this) as HwndSource;
            source.AddHook(WndProc);
        }
        //Receive and act on messages
        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // Handle messages...
            if (msg == NativeMethods.WM_SHOWME)
            {
                Show();
                Activate();
                WindowState = storedWindowState;
            }
            return IntPtr.Zero;
        }
        //dll import magic
        internal class NativeMethods
        {
            public const int HWND_BROADCAST = 0xffff;
            public static readonly int WM_SHOWME = RegisterWindowMessage("WM_SHOWME");
            [DllImport("user32")]
            public static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam);
            [DllImport("user32")]
            public static extern int RegisterWindowMessage(string message);
        }

        private void SaveWatcher_LogMessage(object sender, SaveWatcherLogMessageEventArgs e)
        {
            try
            {
                this.Dispatcher.Invoke(() =>
                {
                    logMessage(e.Message, e.LogType);
                });
            }
            catch (Exception ex)
            {
                logMessage($"Error responding to SaveWatcher LogMessage event: {ex.Message}", LogType.Error);
            }
        }

        private void AddToSaveWatchers(string path)
        {
            var watcher = new SaveWatcher(path, SaveWatcher_LogMessage);
            watcher.WorldWatcher.Changed += OnSaveFileChanged;
            watcher.WorldWatcher.Created += OnSaveFileChanged;
            watcher.WorldWatcher.Renamed += OnSaveFileChanged;

            watcher.CharacterWatcher.Changed += OnSaveFileChanged;
            watcher.CharacterWatcher.Created += OnSaveFileChanged;
            watcher.CharacterWatcher.Renamed += OnSaveFileChanged;
            saveWatchers.Add(watcher);
        }

        private void NotifyIcon_Click(object sender, EventArgs e)
        {
            Show();
            Activate();
            WindowState = storedWindowState;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (Properties.Settings.Default.StartMinimized)
            {
                this.WindowState = WindowState.Minimized;
            }
            loadBackups();
            txtBackupFolder.Text = Properties.Settings.Default.BackupFolder;

            txtFtpImport.Text = "ftp://" + Properties.Settings.Default.FtpIpAddress + ":" + Properties.Settings.Default.FtpPort + "/" + Properties.Settings.Default.FtpFilePath;
            chkAutoBackup.IsChecked = Properties.Settings.Default.AutoBackup;
            txtBackupMins.Text = Properties.Settings.Default.BackupMinutes.ToString();
            txtBackupLimit.Text = Properties.Settings.Default.BackupLimit.ToString();
            chkAutoCheckUpdate.IsChecked = Properties.Settings.Default.AutoCheckUpdate;
            chkCreateLogFile.IsChecked = Properties.Settings.Default.CreateLogFile;
            chkStartMinimized.IsChecked = Properties.Settings.Default.StartMinimized;

            if (Properties.Settings.Default.AutoCheckUpdate)
            {
                checkForUpdate();
            }
            IsBackupCurrent = IsBackupCurrent;
        }

        private void loadBackups()
        {
            try
            {
                var backupDirPath = Properties.Settings.Default.BackupFolder;
                if (!Directory.Exists(backupDirPath))
                {
                    logMessage("Backups folder not found, creating...");
                    Directory.CreateDirectory(backupDirPath);
                    Directory.CreateDirectory($@"{backupDirPath}\worlds_local");
                    Directory.CreateDirectory($@"{backupDirPath}\characters_local");
                }
                else
                {
                    if (!Directory.Exists($@"{backupDirPath}\worlds_local"))
                    {
                        Directory.CreateDirectory($@"{backupDirPath}\worlds_local");
                    }
                    if (!Directory.Exists($@"{backupDirPath}\characters_local"))
                    {
                        Directory.CreateDirectory($@"{backupDirPath}\characters_local");
                    }
                }

                dataBackups.ItemsSource = null;
                listBackups.Clear();
                Dictionary<long, string> backupWorldNames = getBackupNames("World");
                Dictionary<long, bool> backupWorldKeeps = getBackupKeeps("World");
                string[] worldBackups = Directory.GetDirectories(backupDirPath + "\\worlds_local");
                foreach (string w in worldBackups)
                {
                    //string name = w.Replace($@"{backupDirPath}\worlds_local", "");
                    string[] backupDirs = Directory.GetDirectories(w);
                    foreach (string backupDir in backupDirs)
                    {
                        string[] files = System.IO.Directory.GetFiles(backupDir, "*.db");
                        if (files.Length < 1) continue;
                        var name = new FileInfo(files[0]).Name;
                        SaveBackup backup = new SaveBackup($@"{backupDir}\{name}");
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
                string[] charBackups = Directory.GetDirectories($@"{backupDirPath}\characters_local");
                foreach (string c in charBackups)
                {
                    //string name = c.Replace($@"{backupDirPath}\characters_local", "");
                    string[] backupDirs = Directory.GetDirectories(c);
                    foreach (string backupDir in backupDirs)
                    {
                        string[] files = System.IO.Directory.GetFiles(backupDir, "*.fch");
                        if (files.Length < 1) continue;
                        var name = new FileInfo(files[0]).Name;
                        SaveBackup backup = new SaveBackup($@"{backupDir}\{name}");
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
                //listBackups.Sort();
                listBackups = listBackups.OrderByDescending(x => x.SaveDate).ToList();
                dataBackups.ItemsSource = listBackups;
            }
            catch (Exception ex)
            {
                logMessage($"Error loading backups: {ex.Message}", LogType.Error);
            }
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
            logMessage(msg, defaultTextColor);
        }

        public void logMessage(string msg, LogType lt)
        {
            //Color color = Colors.White;
            Color color = defaultTextColor;
            if (lt == LogType.Success)
            {
                color = Color.FromRgb(50, 200, 50);
            }
            else if (lt == LogType.Error)
            {
                color = Color.FromRgb(200, 50, 50);
            }
            logMessage(msg, color);
        }

        public void logMessage(string msg, Color color)
        {
            if (!suppressLog)
            {
                this.Dispatcher.Invoke(() =>
                {
                    //txtLog.Text = txtLog.Text + Environment.NewLine + DateTime.Now.ToString() + ": " + msg;
                    Run run = new Run(DateTime.Now.ToString() + ": " + msg);
                    run.Foreground = new SolidColorBrush(color);
                    Paragraph paragraph = new Paragraph(run);
                    paragraph.Margin = new Thickness(0);
                    if (txtLog.Document.Blocks.Count > 0)
                    {
                        txtLog.Document.Blocks.InsertBefore(txtLog.Document.Blocks.FirstBlock, paragraph);
                    }
                    else
                    {
                        txtLog.Document.Blocks.Add(paragraph);
                    }
                    if (msg.Contains("\n"))
                    {
                        lblLastMessage.Content = msg.Split('\n')[0];
                    }
                    else
                    {
                        lblLastMessage.Content = msg;
                    }
                    lblLastMessage.Foreground = new SolidColorBrush(color);
                    if (color.Equals(defaultTextColor))
                    {
                        lblLastMessage.FontWeight = FontWeights.Normal;
                    }
                    else
                    {
                        lblLastMessage.FontWeight = FontWeights.Bold;
                    }
                });
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
            foreach (var saveDirPath in Properties.Settings.Default.SaveFolders)
            {
                string[] worlds = Directory.GetFiles($@"{saveDirPath}\worlds_local", "*.db");
                foreach (string save in worlds)
                {
                    doBackup(save);
                }
                if (!Directory.Exists($@"{saveDirPath}\characters_local"))
                {
                    Directory.CreateDirectory($@"{saveDirPath}\characters_local");
                }
                string[] characters = Directory.GetFiles($@"{saveDirPath}\characters_local", "*.fch");
                foreach (string save in characters)
                {
                    doBackup(save);
                }
                this.IsBackupCurrent = this.IsBackupCurrent;
                //doBackup();
            }
        }

        private void doBackup(string savepath)
        {
            try
            {
                new Thread(() =>
                {
                    Thread.CurrentThread.IsBackground = true;
                    SaveFile save = new SaveFile(savepath);
                    if (!save.BackedUp)
                    {
                        SaveBackup backup = save.PerformBackup();
                        this.Dispatcher.Invoke(() =>
                        {
                            if (backup != null)
                            {
                                listBackups.Add(backup);
                                checkBackupLimits();
                                listBackups = listBackups.OrderByDescending(x => x.SaveDate).ToList();
                                dataBackups.ItemsSource = listBackups;
                                dataBackups.Items.Refresh();
                                this.IsBackupCurrent = this.IsBackupCurrent;
                                logMessage($"Backup of {backup.Type.ToLower()} {backup.Name} completed!", LogType.Success);
                            }
                            else
                            {
                                logMessage($"Backup of {save.Type.ToLower()} {save.Name} failed!", LogType.Error);
                            }
                        });
                    }
                }).Start();
            }
            catch (Exception ex)
            {
                logMessage($"Error attempting backup of {savepath}: {ex.Message}");
            }
        }

        private Boolean isValheimRunning()
        {
            Process[] pname = Process.GetProcessesByName("valheim");
            return pname.Length > 0;
        }
        private Boolean isValheimServerRunning()
        {
            Process[] pname = Process.GetProcessesByName("valheim_server");
            return pname.Length > 0;
        }

        private void restoreBackup(SaveBackup selectedBackup, string restorePath)
        {
            if (File.Exists(restorePath))
            {
                //check if active save is backed up
                SaveFile save = new SaveFile(restorePath);
                if (!save.BackedUp)
                {
                    doBackup(save.FullPath);
                }
            }
            foreach (var watcher in saveWatchers)
            {
                watcher.WorldWatcher.EnableRaisingEvents = false;
                watcher.CharacterWatcher.EnableRaisingEvents = false;
            }
            selectedBackup.Restore(restorePath);
            dataBackups.Items.Refresh();
            if (Properties.Settings.Default.SaveFolders.Count == 1)
            {
                logMessage(selectedBackup.Name + " backup restored!", LogType.Success);
            }
            else
            {
                logMessage($"{selectedBackup.Name} backup restored to {restorePath}!", LogType.Success);
            }
            foreach (var watcher in saveWatchers)
            {
                watcher.WorldWatcher.EnableRaisingEvents = Properties.Settings.Default.AutoBackup;
                watcher.CharacterWatcher.EnableRaisingEvents = Properties.Settings.Default.AutoBackup;
            }
        }

        private void ChkAutoBackup_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.AutoBackup = chkAutoBackup.IsChecked.HasValue ? chkAutoBackup.IsChecked.Value : false;
            Properties.Settings.Default.Save();
            foreach (var watcher in saveWatchers)
            {
                watcher.WorldWatcher.EnableRaisingEvents = Properties.Settings.Default.AutoBackup;
                watcher.CharacterWatcher.EnableRaisingEvents = Properties.Settings.Default.AutoBackup;
            }
        }

        private void OnSaveFileChanged(object source, FileSystemEventArgs e)
        {
            // Specify what is done when a file is changed, created, or deleted.
            this.Dispatcher.Invoke(() =>
            {
                try
                {
                    if (e.FullPath.EndsWith(".old") || !Properties.Settings.Default.AutoBackup) return;
                    SaveFile save = new SaveFile(e.FullPath);
                    if (!save.BackedUp)
                    {
                        if (!saveTimers.ContainsKey(e.FullPath))
                        {
                            var saveTimer = new SaveTimer(save);
                            saveTimer.Interval = 1000;
                            saveTimer.AutoReset = false;
                            saveTimer.Elapsed += OnSaveTimerElapsed;
                            saveTimer.Enabled = true;
                            saveTimers.Add(e.FullPath, saveTimer);
                        }
                        else
                        {
                            saveTimers[e.FullPath].Interval = 1000;
                        }
                    }
                    else
                    {
                        
                    }
                }
                catch (Exception ex)
                {
                    logMessage($"{ex.GetType()} setting save file timer: {ex.Message}({ex.StackTrace})");
                }
            });
        }

        private void OnSaveTimerElapsed(Object source, ElapsedEventArgs e)
        {
            //this.Dispatcher.Invoke(() =>
            //{
                try
                {
                    var timer = (SaveTimer)source;
                    if (timer.Save.NeedsBackedUp)
                    //if (charSaveForBackup != null && charSaveForBackup.NeedsBackedUp)
                    {
                        doBackup(timer.Save.FullPath);
                    }
                    else
                    {
                        this.Dispatcher.Invoke(() =>
                        {
                            this.IsBackupCurrent = false;
                            dataBackups.Items.Refresh();
                            TimeSpan span = (timer.Save.BackupDueTime - timer.Save.SaveTime);
                            logMessage($"Save change detected, but {span.Minutes + Math.Round(span.Seconds / 60.0, 2)} minutes left until next backup is due.");
                        });
                    }
                    //var timer = saveTimers[e.Save.FullPath];
                    saveTimers.Remove(timer.Save.FullPath);
                    timer.Dispose();
                }
                catch (Exception ex)
                {
                    logMessage($"{ex.GetType()} processing save file change: {ex.Message} ({ex.StackTrace})");
                }
            //});
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

        private Dictionary<long, string> getBackupNames(string type)
        {
            Dictionary<long, string> names = new Dictionary<long, string>();
            StringDictionary savedLabels;
            if (type.Equals("World"))
            {
                savedLabels = Properties.Settings.Default.WorldBackupLabel;
            }
            else
            {
                savedLabels = Properties.Settings.Default.CharBackupLabel;
            }
            foreach (DictionaryEntry entry in savedLabels)
            {
                Debug.WriteLine($"label for {entry.Key.ToString()}: {entry.Value.ToString()}");
                names.Add(long.Parse(entry.Key.ToString()), entry.Value.ToString());
            }
            return names;
        }

        private Dictionary<long, bool> getBackupKeeps(string type)
        {
            Dictionary<long, bool> keeps = new Dictionary<long, bool>();
            StringDictionary savedKeeps;
            if (type.Equals("World")) {
                savedKeeps = Properties.Settings.Default.WorldBackupKeep;
            } else
            {
                savedKeeps = Properties.Settings.Default.CharBackupKeep;
            }
            foreach (DictionaryEntry entry in savedKeeps)
            {
                Debug.WriteLine($"Keeping {entry.Key.ToString()}");
                keeps.Add(long.Parse(entry.Key.ToString()), bool.Parse(entry.Value.ToString()));
            }
            return keeps;
        }

        private void updateSavedLabels()
        {
            var savedWorldLabels = new SerializableStringDictionary();
            var savedCharLabels = new SerializableStringDictionary();
            foreach (var s in listBackups) 
            {
                if (s.Label != s.DefaultLabel)
                {
                    if (s.Type.Equals("World"))
                    {
                        savedWorldLabels.Add(s.SaveDate.Ticks.ToString(), s.Label);
                    }
                    else
                    {
                        savedCharLabels.Add(s.SaveDate.Ticks.ToString(), s.Label);
                    }
                    Debug.WriteLine($"adding label for {s.SaveDate.Ticks}: {s.Label}");
                }
            }
            if (savedWorldLabels.Count > 0)
            {
                Properties.Settings.Default.WorldBackupLabel = savedWorldLabels;
            }
            else
            {
                Properties.Settings.Default.WorldBackupLabel = new SerializableStringDictionary();
            }
            if (savedCharLabels.Count > 0)
            {
                Properties.Settings.Default.CharBackupLabel = savedCharLabels;
            }
            else
            {
                Properties.Settings.Default.CharBackupLabel = new SerializableStringDictionary();
            }
            Properties.Settings.Default.Save();
        }

        private void updateSavedKeeps()
        {
            var savedWorldKeeps = new SerializableStringDictionary();
            var savedCharKeeps = new SerializableStringDictionary();
            for (int i = 0; i < listBackups.Count; i++)
            {
                SaveBackup s = listBackups[i];
                if (s.Keep)
                {
                    if (s.Type.Equals("World"))
                    {
                        savedWorldKeeps.Add(s.SaveDate.Ticks.ToString(), "True");
                    } else
                    {
                        savedCharKeeps.Add(s.SaveDate.Ticks.ToString(), "True");
                    }
                }
            }
            if (savedWorldKeeps.Count > 0)
            {
                Properties.Settings.Default.WorldBackupKeep = savedWorldKeeps;
            }
            else
            {
                Properties.Settings.Default.WorldBackupKeep = new SerializableStringDictionary();
            }
            if (savedCharKeeps.Count > 0)
            {
                Properties.Settings.Default.CharBackupKeep = savedCharKeeps;
            }
            else
            {
                Properties.Settings.Default.CharBackupKeep = new SerializableStringDictionary();
            }
            Properties.Settings.Default.Save();
        }

        private void menuBackupsDelete_Click(object sender, System.EventArgs e)
        {
            SaveBackup save = (SaveBackup)dataBackups.SelectedItem;
            ModernMessageBox mmbConfirm = new ModernMessageBox(this);
            var confirmResult = mmbConfirm.Show($"Are you sure to delete backup \"{save.Label}\" ({save.SaveDate.ToString()})?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (confirmResult == MessageBoxResult.Yes)
            {
                if (save.Keep)
                {
                    mmbConfirm = new ModernMessageBox(this);
                    confirmResult = mmbConfirm.Show($"This backup is marked for keeping. Are you SURE to delete backup \"{save.Label}\" ({save.SaveDate.ToString()})?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                    if (confirmResult != MessageBoxResult.Yes)
                    {
                        return;
                    }
                }
                if (save.Active)
                {
                    this.IsBackupCurrent = false;
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
                            ModernMessageBox mmbConfirm = new ModernMessageBox(this);
                            var confirmResult = mmbConfirm.Show("There is a new version available. Would you like to open the download page?", "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
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
            Properties.Settings.Default.MainWindowWidth = Width;
            Properties.Settings.Default.MainWindowHeight = Height;
            Properties.Settings.Default.Save();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            if (notifyIcon != null)
            {
                notifyIcon.Dispose();
            }
            if (ftpDirectorySync != null)
            {
                ftpDirectorySync.Abort();
                ftpDirectorySync = null;
            }
            if (_mutex != null)
            {
                _mutex.Dispose();
            }
        }

        private void BtnBackupFolder_Click(object sender, RoutedEventArgs e)
        {
            var backupDirPath = Properties.Settings.Default.BackupFolder;
            System.Windows.Forms.FolderBrowserDialog openFolderDialog = new System.Windows.Forms.FolderBrowserDialog();
            openFolderDialog.SelectedPath = backupDirPath;
            openFolderDialog.Description = "Select the folder where you want your backups kept.";
            System.Windows.Forms.DialogResult result = openFolderDialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                string folderName = openFolderDialog.SelectedPath;
                foreach (var saveDirPath in Properties.Settings.Default.SaveFolders)
                {
                    if (folderName.Equals(saveDirPath))
                    {
                        ModernMessageBox mmbConfirm = new ModernMessageBox(this);
                        mmbConfirm.Show("Please select a folder other than the game's save folder.", "Invalid Folder", MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK);
                        return;
                    }
                }
                if (folderName.Equals(backupDirPath))
                {
                    return;
                }
                if (listBackups.Count > 0)
                {
                    ModernMessageBox mmbConfirm = new ModernMessageBox(this);
                    var confirmResult = mmbConfirm.Show("Do you want to move your backups to this new folder?", "Move Backups", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                    if (confirmResult == MessageBoxResult.Yes)
                    {
                        CopyFolder(new DirectoryInfo(backupDirPath), new DirectoryInfo(folderName));
                        List<String> backupFolders = Directory.GetDirectories(backupDirPath).ToList();
                        foreach (string file in backupFolders)
                        {
                            Directory.Delete(file, true);
                        }
                    }
                }
                txtBackupFolder.Text = folderName;
                backupDirPath = folderName;
                if (!Directory.Exists($@"{backupDirPath}\worlds_local"))
                {
                    Directory.CreateDirectory($@"{backupDirPath}\worlds_local");
                }
                if (!Directory.Exists($@"{backupDirPath}\characters_local"))
                {
                    Directory.CreateDirectory($@"{backupDirPath}\characters_local");
                }
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
            e.Cancel = true;
        }

        private void btnAppUpdate_Click(object sender, RoutedEventArgs e)
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
            try
            {
                bool newValue = chkCreateLogFile.IsChecked.GetValueOrDefault();
                if (newValue & !Properties.Settings.Default.CreateLogFile)
                {
                    System.IO.File.WriteAllText("log.txt", DateTime.Now.ToString() + ": Version " + typeof(MainWindow).Assembly.GetName().Version + "\r\n");
                }
                Properties.Settings.Default.CreateLogFile = newValue;
                Properties.Settings.Default.Save();
            } catch (Exception ex)
            {
                logMessage($"Error changing log option: {ex.Message}");
            }
        }

        private void chkAutoCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            bool newValue = chkAutoCheckUpdate.IsChecked.HasValue ? chkAutoCheckUpdate.IsChecked.Value : false;
            Properties.Settings.Default.AutoCheckUpdate = newValue;
            Properties.Settings.Default.Save();
        }

        private void btnFtpImport_Click(object sender, RoutedEventArgs e)
        {
            FtpSettingsWindow ftpWin = new FtpSettingsWindow();
            ftpWin.Owner = this;
            if ((bool)ftpWin.ShowDialog())
            {
                txtFtpImport.Text = "ftp://" + Properties.Settings.Default.FtpIpAddress + ":" + Properties.Settings.Default.FtpPort + "/" + Properties.Settings.Default.FtpFilePath;
                if (ftpDirectorySync == null)
                {
                    syncDirectoriesAsync();
                }
            }
        }

        private bool ftpSyncEnabled()
        {
            return !(Properties.Settings.Default.FtpIpAddress.Length == 0
                            || Properties.Settings.Default.FtpPort.Length == 0
                            || Properties.Settings.Default.FtpFilePath.Length == 0
                            || Properties.Settings.Default.FtpSaveDest.Length == 0
                            || Properties.Settings.Default.FtpUsername.Length == 0
                            || Properties.Settings.Default.FtpPassword.Length == 0
                        );
        }

        private void syncDirectoriesAsync()
        {
            
            // asynchronously sync local directory with ftp
            ftpDirectorySync = new Thread(() => {
                try
                {
                    while (ftpDirectorySync != null)
                    {
                        if (Properties.Settings.Default.FtpIpAddress.Length == 0
                            || Properties.Settings.Default.FtpPort.Length == 0
                            || Properties.Settings.Default.FtpFilePath.Length == 0
                            || Properties.Settings.Default.FtpSaveDest.Length == 0
                            || Properties.Settings.Default.FtpUsername.Length == 0
                            || Properties.Settings.Default.FtpPassword.Length == 0
                        )
                        {
                            System.Diagnostics.Debug.WriteLine("exiting sync thread");
                            ftpDirectorySync = null;
                            break;
                        }

                        System.Diagnostics.Debug.WriteLine("re-syncing");
                        int syncstatus = SynchronizeDirectories.downloadWorlds(
                            Properties.Settings.Default.FtpIpAddress,
                            Properties.Settings.Default.FtpPort,
                            Properties.Settings.Default.FtpFilePath,
                            Properties.Settings.Default.FtpSaveDest + "\\worlds_local",
                            Properties.Settings.Default.FtpUsername,
                            Properties.Settings.Default.FtpPassword,
                            (WinSCP.FtpMode)Properties.Settings.Default.FtpMode
                        );
                        if (syncstatus == 0)
                        {
                            this.Dispatcher.Invoke(() =>
                            {
                                logMessage("Successfully synced world saves from FTP server.", LogType.Success);
                            });
                        }
                        else
                        {
                            this.Dispatcher.Invoke(() =>
                            {
                                logMessage($"Error syncing world saves from FTP server: {SynchronizeDirectories.LastError.Message}", LogType.Error);
                            });
                        }
                        Thread.Sleep(Properties.Settings.Default.BackupMinutes * 60000);
                    }
                }
                catch (Exception ex)
                {
                    this.Dispatcher.Invoke(() =>
                    {
                        logMessage($"Error checking FTP server: {ex.Message}", LogType.Error);
                    });
                }
            });

            try
            {
                if (ftpDirectorySync != null)
                {
                    ftpDirectorySync.Start();
                }
            } 
            catch (Exception ex)
            {
                logMessage($"Error starting FTP sync thread: {ex.Message}");
            }
        }
        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                {
                    Hide();
                    if (notifyIcon != null)
                    {
                        if (Properties.Settings.Default.ShowMinimizeMessage)
                        {
                            notifyIcon.ShowBalloonTip(2000);
                            Properties.Settings.Default.ShowMinimizeMessage = false;
                            Properties.Settings.Default.Save();
                        }
                    }
                }
            }
            else
            {
                storedWindowState = WindowState;
            }
        }

        private void Window_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            CheckTrayIcon();
        }
        void CheckTrayIcon()
        {
            ShowTrayIcon(!IsVisible);
        }
        void ShowTrayIcon(bool show)
        {
            if (notifyIcon != null)
            {
                notifyIcon.Visible = show;
            }
        }

        private void menuSavePathOpen_Click(object sender, RoutedEventArgs e)
        {
            var saveDirPath = (string)lstSaveFolders.SelectedItem;
            if (!Directory.Exists(saveDirPath))
            {
                logMessage("Save path not found, please select a valid path for your save files.");
                return;
            }
            Process.Start(saveDirPath + "\\");
        }

        private void menuBackupPathOpen_Click(object sender, RoutedEventArgs e)
        {
            var backupDirPath = Properties.Settings.Default.BackupFolder;
            if (!Directory.Exists(backupDirPath))
            {
                logMessage("Backups folder not found, creating...");
                Directory.CreateDirectory(backupDirPath);
            }
            Process.Start(backupDirPath + "\\");
        }

        private void btnReportBug_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("https://github.com/Razzmatazzz/ValheimSaveShield/issues");
        }

        private void lstSaveFolders_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (lstSaveFolders.Items.Count > 1 && lstSaveFolders.SelectedIndex > -1)
            {
                menuSavePathRemove.IsEnabled = true;
                menuSavePathRemove.Icon = FindResource("Remove");
            }
            else
            {
                menuSavePathRemove.IsEnabled = false;
                menuSavePathRemove.Icon = FindResource("RemoveGrey");
            }

            if (lstSaveFolders.SelectedIndex > -1)
            {
                menuSavePathEdit.IsEnabled = true;
                menuSavePathEdit.Icon = FindResource("Edit");
            }
            else
            {
                menuSavePathEdit.IsEnabled = false;
                menuSavePathEdit.Icon = FindResource("EditGrey");
            }
        }

        private void menuSavePathAdd_Click(object sender, RoutedEventArgs e)
        {
            var saveDirPath = (string)lstSaveFolders.SelectedItem;
            System.Windows.Forms.FolderBrowserDialog openFolderDialog = new System.Windows.Forms.FolderBrowserDialog();
            openFolderDialog.SelectedPath = saveDirPath;
            openFolderDialog.Description = "Select where your Valheim saves are stored";
            System.Windows.Forms.DialogResult result = openFolderDialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                string folderName = openFolderDialog.SelectedPath;
                if (folderName.Equals(Properties.Settings.Default.BackupFolder))
                {
                    ModernMessageBox mmbWarn = new ModernMessageBox(this);
                    mmbWarn.Show("Please select a folder other than the backup folder.",
                                     "Invalid Folder", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK);
                    return;
                }
                foreach (var path in Properties.Settings.Default.SaveFolders)
                {
                    if (folderName.Equals(path))
                    {
                        return;
                    }
                }
                if (!Directory.Exists($@"{folderName}\worlds_local"))
                {
                    Directory.CreateDirectory($@"{folderName}\worlds_local");
                    logMessage($"{folderName} did not contain a \"worlds_local\" folder, so it may not be a valid save location.");
                }
                if (!Directory.Exists($@"{folderName}\characters_local"))
                {
                    Directory.CreateDirectory($@"{folderName}\characters_local");
                }
                lstSaveFolders.Items.Add(folderName);
                lblSaveFolders.Content = "Save Folders";
                AddToSaveWatchers(folderName);

                Properties.Settings.Default.SaveFolders.Add(folderName);
                Properties.Settings.Default.Save();
            }
        }

        private void menuSavePathEdit_Click(object sender, RoutedEventArgs e)
        {
            var saveDirPath = (string)lstSaveFolders.SelectedItem;
            System.Windows.Forms.FolderBrowserDialog openFolderDialog = new System.Windows.Forms.FolderBrowserDialog();
            openFolderDialog.SelectedPath = saveDirPath;
            openFolderDialog.Description = "Select where your Valheim saves are stored";
            System.Windows.Forms.DialogResult result = openFolderDialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                string folderName = openFolderDialog.SelectedPath;
                if (folderName.Equals(Properties.Settings.Default.BackupFolder))
                {
                    ModernMessageBox mmbWarn = new ModernMessageBox(this);
                    mmbWarn.Show("Please select a folder other than the backup folder.",
                                     "Invalid Folder", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK);
                    return;
                }
                foreach (var path in Properties.Settings.Default.SaveFolders)
                {
                    if (folderName.Equals(path))
                    {
                        return;
                    }
                }
                if (!Directory.Exists($@"{folderName}\worlds_local"))
                {
                    Directory.CreateDirectory($@"{folderName}\worlds_local");
                    logMessage($"{folderName} did not contain a \"worlds_local\" folder, so it may not be a valid save location.");
                }
                if (!Directory.Exists($@"{folderName}\characters_local"))
                {
                    Directory.CreateDirectory($@"{folderName}\characters_local");
                }
                lstSaveFolders.Items[lstSaveFolders.SelectedIndex] = folderName;
                foreach (var swatcher in saveWatchers)
                {
                    if (swatcher.SavePath == folderName)
                    {
                        swatcher.Dispose();
                        saveWatchers.Remove(swatcher);
                        break;
                    }
                }
                AddToSaveWatchers(folderName);
                Properties.Settings.Default.SaveFolders.Remove(saveDirPath);
                Properties.Settings.Default.SaveFolders.Add(folderName);
                if (Properties.Settings.Default.FtpSaveDest == saveDirPath)
                {
                    Properties.Settings.Default.FtpSaveDest = folderName;
                    logMessage($"Local FTP destination folder changed to {folderName}.");
                }
                Properties.Settings.Default.Save();
            }
        }

        private void menuSavePathRemove_Click(object sender, RoutedEventArgs e)
        {
            var saveDirPath = (string)lstSaveFolders.SelectedItem;
            foreach (var watcher in saveWatchers)
            {
                if (watcher.SavePath == saveDirPath)
                {
                    watcher.Dispose();
                    saveWatchers.Remove(watcher);
                    break;
                }
            }
            lstSaveFolders.Items.Remove(saveDirPath);
            lstSaveFolders.Items.Refresh();
            Properties.Settings.Default.SaveFolders.Remove(saveDirPath);
            if (Properties.Settings.Default.FtpSaveDest == saveDirPath)
            {
                Properties.Settings.Default.FtpSaveDest = Properties.Settings.Default.SaveFolders[0];
                logMessage($"Local FTP destination folder changed to {Properties.Settings.Default.SaveFolders[0]}.");
            }
            if (lstSaveFolders.Items.Count > 1)
            {
                lblSaveFolders.Content = "Save Folders";
            }
            else
            {
                lblSaveFolders.Content = "Save Folder";
            }
            Properties.Settings.Default.Save();
        }

        private void menuBackups_Opened(object sender, RoutedEventArgs e)
        {
            if (dataBackups.SelectedIndex == -1)
            {
                menuBackups.IsOpen = false;
                menuBackupsDelete.IsEnabled = false;
                return;
            }
            menuBackupsDelete.IsEnabled = true;
            SaveBackup selectedBackup = (SaveBackup)dataBackups.SelectedItem;
            menuBackupsRestore.Click -= menuBackupsRestore_Click;
            menuBackupsRestore.Items.Clear();
            if (Properties.Settings.Default.SaveFolders.Count < 2 && (!ftpSyncEnabled() || selectedBackup.Type != "World"))
            {
                if (!File.Exists(selectedBackup.ActivePaths.First()) || File.GetLastWriteTime(selectedBackup.ActivePaths.First()) != selectedBackup.SaveDate)
                {
                    menuBackupsRestore.IsEnabled = true;
                    menuBackupsRestore.Icon = FindResource("Restore");
                    menuBackupsRestore.Click += menuBackupsRestore_Click;
                    menuBackupsRestore.Tag = selectedBackup.ActivePaths.First();
                }
                else
                {
                    menuBackupsRestore.IsEnabled = false;
                    menuBackupsRestore.Icon = FindResource("RestoreGrey");
                }
            }
            else
            {
                foreach (var path in selectedBackup.ActivePaths)
                {
                    if (!File.Exists(path) || File.GetLastWriteTime(path) != selectedBackup.SaveDate)
                    {
                        MenuItem menu = new MenuItem();
                        menu.Header = new FileInfo(path).Directory.Parent.FullName;
                        menu.Tag = path;
                        menu.ToolTip = "Restore to this save location";
                        menu.Click += menuBackupsRestore_Click;
                        menuBackupsRestore.Items.Add(menu);
                    }
                }
                if (ftpSyncEnabled() && selectedBackup.Type == "World")
                {
                    MenuItem menu = new MenuItem();
                    menu.Header = "ftp://" + Properties.Settings.Default.FtpIpAddress + ":" + Properties.Settings.Default.FtpPort + "/" + Properties.Settings.Default.FtpFilePath;
                    menu.ToolTip = "Restore to the remote FTP import location";
                    menu.Click += menuFtpRestore_Click;
                    menuBackupsRestore.Items.Add(menu);
                }
                if (menuBackupsRestore.Items.Count > 0)
                {
                    menuBackupsRestore.IsEnabled = true;
                    menuBackupsRestore.Icon = FindResource("Restore");
                }
                else
                {
                    menuBackupsRestore.IsEnabled = false;
                    menuBackupsRestore.Icon = FindResource("RestoreGrey");
                }
            }
            if (selectedBackup.Type == "World")
            {
                menuBackupsViewMap.Visibility = Visibility.Visible;
            }
            else
            {
                menuBackupsViewMap.Visibility = Visibility.Collapsed;
            }
        }

        private void menuFtpRestore_Click(object sender, RoutedEventArgs e)
        {
            new Thread(() =>
            {
                Thread.CurrentThread.IsBackground = true;
                try
                {
                    SaveBackup selectedBackup = (SaveBackup)dataBackups.SelectedItem;
                    selectedBackup.RestoreFtp();
                    logMessage($"{selectedBackup.Name} backup restored to {"ftp://" + Properties.Settings.Default.FtpIpAddress + ":" + Properties.Settings.Default.FtpPort + "/" + Properties.Settings.Default.FtpFilePath}!", LogType.Success);
                }
                catch (Exception ex)
                {
                    logMessage($"Error restoring save to FTP: {ex.Message}", LogType.Error);
                }
            }).Start();
        }

        private void menuBackupsRestore_Click(object sender, RoutedEventArgs e)
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
                logMessage("Stop any running game servers before restoring a world backup.", LogType.Error);
                return;
            }
            MenuItem menu = (MenuItem)sender;
            restoreBackup(selectedBackup, menu.Tag.ToString());
        }

        private void Window_ContentRendered(object sender, EventArgs e)
        {
            InvalidateMeasure();
            InvalidateVisual();
        }

        private void menuBackupsViewMap_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedBackup = (SaveBackup)dataBackups.SelectedItem;
                var fwl = File.ReadAllText($@"{selectedBackup.Folder}\{selectedBackup.Name}.fwl");
                var lines = fwl.Split('\n');
                var seed = lines[lines.Length - 1].Substring(0, 10);
                Process.Start($"http://valheim-map.world/?seed={seed}&offset=0%2C0&zoom=0.600");

                //Get boss coordinates. Not currently very useful.
                /*var bosses = new List<string>();
                bosses.Add("Eikthyrnir");
                bosses.Add("GDKing");
                bosses.Add("Bonemass");
                bosses.Add("Dragonqueen");
                bosses.Add("GoblinKing");
                bosses.Add("Vendor_BlackForest");
                byte[] byteBuffer = File.ReadAllBytes(selectedBackup.FullPath);
                string byteBufferAsString = System.Text.Encoding.Default.GetString(byteBuffer);
                foreach (var keyval in bosses)
                {
                    Debug.WriteLine(keyval);
                    for (var offset = byteBufferAsString.IndexOf(keyval); offset != -1; offset = byteBufferAsString.IndexOf(keyval, offset))
                    {
                        var xstring = byteBufferAsString.Substring(offset + keyval.Length, 4);
                        var xfloat = System.BitConverter.ToSingle(System.Text.Encoding.Default.GetBytes(xstring), 0);
                        var xint = (int)Math.Round(xfloat);
                        var ystring = byteBufferAsString.Substring(offset + keyval.Length + 8, 4);
                        var yfloat = System.BitConverter.ToSingle(System.Text.Encoding.Default.GetBytes(ystring), 0);
                        var yint = (int)Math.Round(yfloat);
                        Debug.WriteLine($"{xint}, {yint}");
                        offset += keyval.Length + 12;
                    }
                }*/
            }
            catch (Exception ex)
            {
                logMessage($"Error showing map: {ex.Message}", LogType.Error);
            }
        }

        private void btnExtraWorldFiles_Click(object sender, RoutedEventArgs e)
        {
            var win = new ExtraWorldFilesWindow();
            win.Owner = this;
            win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            win.ShowDialog();
        }

        private void dataBackups_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (dataBackups.SelectedIndex > -1)
            {
                menuBackups.Visibility = Visibility.Visible;
            }
            else
            {
                menuBackups.Visibility = Visibility.Collapsed;
            }
        }

        private void chkStartMinimized_Checked(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.StartMinimized = chkStartMinimized.IsChecked.GetValueOrDefault();
            Properties.Settings.Default.Save();
        }
    }

    public enum LogType
    {
        Normal,
        Success,
        Error
    }

    public class LogMessageEventArgs : EventArgs
    {
        private readonly string _message;
        private readonly LogType _logtype;
        public LogMessageEventArgs(string message, LogType logtype)
        {
            _message = message;
            _logtype = logtype;
        }
        public LogMessageEventArgs(string message) : this(message, LogType.Normal) { }

        public string Message
        {
            get { return _message; }
        }
        public LogType LogType
        {
            get { return _logtype; }
        }
    }
}