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
                return false;
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
