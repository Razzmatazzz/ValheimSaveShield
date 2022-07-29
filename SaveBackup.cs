using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;
using System.IO;
using System.Diagnostics;
using System.Collections;

namespace ValheimSaveShield
{
    public class SaveBackup : IEditableObject, IComparable
    {
        struct BackupData
        {
            internal string backupPath;
            internal string label;
            //internal string type;
            internal bool keep;
        }

        public event EventHandler<UpdatedEventArgs> Updated;
        private BackupData backupData;
        private BackupData backupDataBackup;
        private bool inTxn = false;
        public string Label
        {
            get
            {
                if (this.backupData.label == "" || this.backupData.label == null)
                {
                    return this.DefaultLabel;
                }
                else
                {
                    return this.backupData.label;
                }
            }
            set
            {
                if (value == "" || value == null)
                {
                    this.backupData.label = this.DefaultLabel;
                } else
                {
                    this.backupData.label = value;
                }
            }
        }
        public string Name
        {
            get
            {
                var fileName = new FileInfo(this.backupData.backupPath).Name;
                var parts = new ArrayList(FileName.Split('.'));
                parts.RemoveAt(parts.Count - 1);
                return string.Join(".", parts.ToArray()).Trim();
                //return new FileInfo(this.saveData.savePath).Name.Split('.')[0];
            }
        }
        public string Type
        {
            get
            {
                if (new FileInfo(this.backupData.backupPath).Directory.FullName.StartsWith($@"{Properties.Settings.Default.BackupFolder}\worlds_local\"))
                {
                    return "World";
                }
                else
                {
                    return "Character";
                }
            }
        }
        public string DefaultLabel
        {
            get
            {
                return this.Name + " " + Math.Abs(this.SaveDate.Ticks % 10000);
            }
        }
        public DateTime SaveDate
        {
            get {
                return File.GetLastWriteTime(this.FullPath);
            }
        }
        public bool Keep
        {
            get
            {
                return this.backupData.keep;
            }
            set
            {
                this.backupData.keep = value;
            }
        }
        public bool Active
        {
            get
            {
                foreach (var activePath in ActivePaths)
                {
                    if (File.Exists(activePath) && File.GetLastWriteTime(activePath).Ticks == this.SaveDate.Ticks)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public string FileName
        {
            get
            {
                return new FileInfo(this.backupData.backupPath).Name;
            }
        }

        public string FullPath
        {
            get
            {
                return this.backupData.backupPath;
            }
        }

        public string Folder
        {
            get
            {
                return this.FullPath.Replace("\\" + this.FileName, "");
            }
        }

        public List<string> ActivePaths
        {
            get
            {
                var paths = new List<string>();
                foreach (var savePath in Properties.Settings.Default.SaveFolders)
                {
                    paths.Add($@"{savePath}\{this.Type.ToLower()}s_local\{this.FileName}");
                }
                return paths;
            }
        }

        public SaveBackup(string backupPath)
        {
            if (this.backupData.Equals(default(BackupData)))
            {
                //this.backupData = new SaveData();
                this.backupData.label = "";
            }
            this.backupData.backupPath = backupPath;
            this.backupData.keep = false;
            
        }

        public SaveBackup(string savePath, string label) : this(savePath)
        {
            this.Label = label;
        }

        public void Restore()
        {
            Restore(this.ActivePaths.First());
        }

        public void Restore(string path)
        {
            File.Copy(this.FullPath, path, true);
            if (this.Type == "World")
            {
                FileInfo info = new FileInfo(this.FullPath);
                FileInfo destInfo = new FileInfo(path);
                string sourcefwl = info.DirectoryName + "\\" + this.Name + ".fwl";
                string destfwl = destInfo.DirectoryName + "\\" + this.Name + ".fwl";
                File.Copy(sourcefwl, destfwl, true);
                foreach (var ext in Properties.Settings.Default.WorldFileExtensions)
                {
                    string sourcefile = info.DirectoryName + "\\" + this.Name + ext;
                    if (File.Exists(sourcefile))
                    {
                        string destfile = destInfo.DirectoryName + "\\" + this.Name + ext;
                        File.Copy(sourcefile, destfile, true);
                    }
                }
            }
        }
        public void RestoreFtp()
        {
            if (this.Type != "World")
            {
                throw new Exception("You can only restore world saves to the FTP location");
            }
            SynchronizeDirectories.uploadFile(Properties.Settings.Default.FtpIpAddress, Properties.Settings.Default.FtpPort, Properties.Settings.Default.FtpFilePath, this.FullPath, Properties.Settings.Default.FtpUsername, Properties.Settings.Default.FtpPassword, (WinSCP.FtpMode)Properties.Settings.Default.FtpMode);
            if (this.Type == "World")
            {
                FileInfo info = new FileInfo(this.FullPath);
                string sourcefwl = info.DirectoryName + "\\" + this.Name + ".fwl";
                SynchronizeDirectories.uploadFile(Properties.Settings.Default.FtpIpAddress, Properties.Settings.Default.FtpPort, Properties.Settings.Default.FtpFilePath, sourcefwl, Properties.Settings.Default.FtpUsername, Properties.Settings.Default.FtpPassword, (WinSCP.FtpMode)Properties.Settings.Default.FtpMode);
                foreach (var ext in Properties.Settings.Default.WorldFileExtensions)
                {
                    string sourcefile = info.DirectoryName + "\\" + this.Name + ext;
                    if (File.Exists(sourcefile))
                    {
                        SynchronizeDirectories.uploadFile(Properties.Settings.Default.FtpIpAddress, Properties.Settings.Default.FtpPort, Properties.Settings.Default.FtpFilePath, sourcefile, Properties.Settings.Default.FtpUsername, Properties.Settings.Default.FtpPassword, (WinSCP.FtpMode)Properties.Settings.Default.FtpMode);
                    }
                }
            }
        }

        // Implements IEditableObject
        void IEditableObject.BeginEdit()
        {
            if (!inTxn)
            {
                this.backupDataBackup = backupData;
                inTxn = true;
            }
        }

        void IEditableObject.CancelEdit()
        {
            if (inTxn)
            {
                this.backupData = backupDataBackup;
                inTxn = false;
            }
        }

        void IEditableObject.EndEdit()
        {
            if (inTxn)
            {
                if (backupDataBackup.label != backupData.label)
                {
                    OnUpdated(new UpdatedEventArgs("Label"));
                }
                if (backupDataBackup.keep != backupData.keep)
                {
                    OnUpdated(new UpdatedEventArgs("Keep"));
                }
                backupDataBackup = new BackupData();
                inTxn = false;
            }
        }

        int IComparable.CompareTo(object obj)
        {
            SaveBackup sb = (SaveBackup)obj;
            return DateTime.Compare(this.SaveDate, sb.SaveDate);
        }

        public void OnUpdated(UpdatedEventArgs args)
        {
            EventHandler<UpdatedEventArgs> handler = Updated;
            if (null != handler) handler(this, args);
        }
    }

    public class UpdatedEventArgs : EventArgs
    {
        private readonly string _fieldName;

        public UpdatedEventArgs(string fieldName) {
            _fieldName = fieldName;
        }

        public string FieldName
        {
            get { return _fieldName; }
        }
    }
}
