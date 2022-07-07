using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;
using System.Collections;

namespace ValheimSaveShield
{
    class SaveFile
    {
        private string filePath;

        public SaveFile(string path)
        {
            filePath = path;
        }

        public string Type
        {
            get
            {
                if (new FileInfo(this.filePath).Directory.FullName.EndsWith("\\worlds_local"))
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
                var fileName = FileName;
                var parts = new ArrayList(FileName.Split('.'));
                parts.RemoveAt(parts.Count - 1);
                return string.Join(".", parts.ToArray()).Trim();
            }
        }

        public string FileName
        {
            get
            {
                return new FileInfo(this.filePath).Name;
            }
        }
        public DateTime SaveTime
        {
            get
            {
                return new FileInfo(filePath).LastWriteTime;
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
                return Properties.Settings.Default.BackupFolder + "\\" + this.Type.ToLower() + "s_local\\" + this.Name;
            }
        }

        public string FullBackupPath
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
                return File.Exists(this.FullBackupPath);
            }
        }

        public bool NeedsBackedUp
        {
            get
            {
                //DateTime newBackupTime = this.BackupDueTime;
                if (DateTime.Compare(SaveTime, this.BackupDueTime) >= 0)
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
                if (!Directory.Exists(this.BackupsPath))
                {
                    Directory.CreateDirectory(this.BackupsPath);
                }
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
                DateTime latestBackupTime = new DateTime();
                if (latestBackup != null)
                {
                    latestBackupTime = latestBackup.SaveDate;
                }
                //Debug.WriteLine($"Latest backup for {this.FullPath} is {latestBackup.SaveDate}");
                return latestBackupTime.AddMinutes(Properties.Settings.Default.BackupMinutes);
            }
        }

        public SaveBackup PerformBackup()
        {
            int copyAttempts = 0;
            try
            {
                string backupFolder = $@"{Properties.Settings.Default.BackupFolder}\{this.Type.ToLower()}s_local\{this.Name}\{File.GetLastWriteTime(this.FullPath).Ticks}";
                if (!Directory.Exists(backupFolder))
                {
                    Directory.CreateDirectory(backupFolder);
                }
                File.Copy(this.FullPath, this.FullBackupPath, true);
                if (this.Type.Equals("World"))
                {
                    FileInfo info = new FileInfo(this.FullPath);
                    string sourcefwl = info.DirectoryName + "\\" + this.Name + ".fwl";
                    string destfwl = this.BackupFolder + "\\" + this.Name + ".fwl";
                    File.Copy(sourcefwl, destfwl, true);
                    foreach (var ext in Properties.Settings.Default.WorldFileExtensions)
                    {
                        string sourcefile = info.DirectoryName + "\\" + this.Name + ext;
                        if (File.Exists(sourcefile))
                        {
                            string destfile = this.BackupFolder + "\\" + this.Name + ext;
                            File.Copy(sourcefile, destfile, true);
                        }
                    }
                }
                return new SaveBackup(this.FullBackupPath);
            }
            catch (IOException ex)
            {
                if (ex.Message.Contains("being used by another process"))
                {
                    copyAttempts++;
                    if (copyAttempts < 5)
                    {
                        //logMessage("Save file in use; waiting 0.5 seconds and retrying.");
                        System.Threading.Thread.Sleep(500*copyAttempts);
                        return this.PerformBackup();
                    }
                    else
                    {
                        throw new Exception("Save file in use; multiple attempts to copy failed.");
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return null;
        }
    }
}
