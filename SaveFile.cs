using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace ValheimSaveShield
{
    class SaveFile
    {
        private string filePath;
        private DateTime backupDueTime;

        public SaveFile(string path)
        {
            filePath = path;
        }

        public string Type
        {
            get
            {
                if (this.filePath.StartsWith(Properties.Settings.Default.SaveFolder + "\\worlds\\"))
                {
                    return "World";
                }
                else
                {
                    return "Character";
                }
            }
        }

        public string Name
        {
            get
            {
                return new FileInfo(this.filePath).Name.Split('.')[0];
            }
        }

        public string FileName
        {
            get
            {
                return new FileInfo(this.filePath).Name;
            }
        }

        public string FullPath
        {
            get
            {
                return filePath;
            }
            set
            {
                this.filePath = value;
            }
        }

        public string BackupsPath
        {
            get
            {
                return Properties.Settings.Default.BackupFolder + "\\" + this.Type.ToLower() + "s\\" + this.Name;
            }
        }

        public string BackupPath
        {
            get
            {
                return this.BackupFolder + "\\" + this.FileName;
            }
        }

        public string BackupFolder
        {
            get
            {
                return this.BackupsPath + "\\" + new FileInfo(filePath).LastWriteTime.Ticks;
            }
        }

        public bool BackedUp
        {
            get
            {
                return File.Exists(this.BackupPath);
            }
        }

        public bool NeedsBackedUp
        {
            get
            {
                DateTime newBackupTime = this.BackupDueTime;
                if (DateTime.Compare(DateTime.Now, newBackupTime) >= 0)
                {
                    return true;
                }
                return false;
            }
        }

        public DateTime BackupDueTime
        {
            get
            {
                if (backupDueTime == null)
                {
                    string[] backups = Directory.GetDirectories(this.BackupsPath);
                    SaveBackup latestBackup = null;
                    foreach (string bdir in backups)
                    {
                        SaveBackup backup = new SaveBackup(bdir + "\\" + this.FileName);
                        if (latestBackup == null || backup.SaveDate.Ticks > latestBackup.SaveDate.Ticks)
                        {
                            latestBackup = backup;
                        }
                    }
                    DateTime latestBackupTime;
                    if (latestBackup == null)
                    {
                        latestBackupTime = new DateTime(1970, 1, 1, 0, 0, 0, 0);
                    }
                    else
                    {
                        latestBackupTime = latestBackup.SaveDate;
                    }
                    this.backupDueTime = latestBackupTime.AddMinutes(Properties.Settings.Default.BackupMinutes);
                }
                return backupDueTime;
            }
        }

        public SaveBackup PerformBackup()
        {
            try
            {
                string backupFolder = Properties.Settings.Default.BackupFolder + "\\" + this.Type.ToLower() + "s\\" + this.Name + "\\" + File.GetLastWriteTime(this.FullPath).Ticks;
                if (!Directory.Exists(backupFolder))
                {
                    Directory.CreateDirectory(backupFolder);
                }
                File.Copy(this.FullPath, this.BackupPath, true);
                if (this.Type.Equals("World"))
                {
                    FileInfo info = new FileInfo(this.FullPath);
                    string sourcefwl = info.DirectoryName + "\\" + this.Name + ".fwl";
                    string destfwl = this.BackupFolder + "\\" + this.Name + ".fwl";
                    File.Copy(sourcefwl, destfwl, true);
                }
                return new SaveBackup(this.BackupPath);
            }
            catch (IOException ex)
            {
                if (ex.Message.Contains("being used by another process"))
                {
                    //logMessage("Save file in use; waiting 0.5 seconds and retrying.");
                    System.Threading.Thread.Sleep(500);
                    return this.PerformBackup();
                }
            }
            return null;
        }
    }
}
