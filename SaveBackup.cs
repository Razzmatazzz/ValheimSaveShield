using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;
using System.IO;

namespace ValheimSaveShield
{
    public class SaveBackup : IEditableObject, IComparable
    {
        struct SaveData
        {
            internal string savePath;
            internal string label;
            //internal string type;
            internal DateTime date;
            internal bool keep;
        }

        public event EventHandler<UpdatedEventArgs> Updated;
        private SaveData saveData;
        private SaveData backupData;
        private bool inTxn = false;
        public string Label
        {
            get
            {
                if (this.saveData.label.Equals(""))
                {
                    return this.DefaultLabel;
                }
                else
                {
                    return this.saveData.label;
                }
            }
            set
            {
                if (value.Equals(""))
                {
                    this.saveData.label = this.DefaultLabel;
                } else
                {
                    this.saveData.label = value;
                }
                //OnUpdated(new UpdatedEventArgs("Name"));
            }
        }
        public string Name
        {
            get
            {
                return new FileInfo(this.saveData.savePath).Name.Split('.')[0];
            }
        }
        public string Type
        {
            get
            {
                if (this.saveData.savePath.StartsWith(Properties.Settings.Default.BackupFolder + "\\worlds\\"))
                {
                    return "World";
                }
                else
                {
                    return "Character";
                }
                //return this.saveData.type;
            }
            /*set
            {
                this.saveData.type = value;
            }*/
        }
        public string DefaultLabel
        {
            get
            {
                return this.Name + " " + Math.Abs(this.saveData.date.Ticks % 10000);
            }
        }
        public DateTime SaveDate
        {
            get {
                return this.saveData.date;
            }
            set
            {
                this.saveData.date = value;
                //OnUpdated(new UpdatedEventArgs("SaveDate"));
            }
        }
        public bool Keep
        {
            get
            {
                return this.saveData.keep;
            }
            set
            {
                this.saveData.keep = value;
                //OnUpdated(new UpdatedEventArgs("Keep"));
            }
        }
        public bool Active
        {
            get
            {
                string activePath = this.ActivePath;
                if (File.Exists(activePath) && File.GetLastWriteTime(activePath).Ticks == this.SaveDateTime.Ticks)
                {
                    return true;
                }
                return false;
            }/*
            set
            {
                this.saveData.active = value;
                //OnUpdated(new UpdatedEventArgs("Active"));
            }*/
        }

        public string FileName
        {
            get
            {
                return new FileInfo(this.saveData.savePath).Name;
            }
        }

        public string FullPath
        {
            get
            {
                return this.saveData.savePath;
            }
        }

        public string Folder
        {
            get
            {
                return this.FullPath.Replace("\\" + this.FileName, "");
            }
        }

        public string ActivePath
        {
            get
            {
                return Properties.Settings.Default.SaveFolder + "\\" + this.Type.ToLower() + "s\\" + this.FileName;
            }
        }

        private DateTime SaveDateTime
        {
            get
            {
                return File.GetLastWriteTime(this.saveData.savePath);
            }
        }

        //public SaveBackup(DateTime saveDate)
        public SaveBackup(string savePath)
        {
            if (this.saveData.Equals(default(SaveData)))
            {
                //this.saveData = new SaveData();
                this.saveData.label = "";
            }
            this.saveData.savePath = savePath;
            this.saveData.date = this.SaveDateTime;
            this.saveData.keep = false;
            
        }

        public SaveBackup(string savePath, string label) : this(savePath)
        {
            this.Label = label;
        }

        public void Restore()
        {
            File.Copy(this.FullPath, this.ActivePath, true);
            if (this.Type.Equals("World"))
            {
                FileInfo info = new FileInfo(this.FullPath);
                FileInfo destInfo = new FileInfo(this.ActivePath);
                string sourcefwl = info.DirectoryName + "\\" + this.Name + ".fwl";
                string destfwl = destInfo.DirectoryName + "\\" + this.Name + ".fwl";
                File.Copy(sourcefwl, destfwl, true);
            }
        }

        // Implements IEditableObject
        void IEditableObject.BeginEdit()
        {
            if (!inTxn)
            {
                this.backupData = saveData;
                inTxn = true;
            }
        }

        void IEditableObject.CancelEdit()
        {
            if (inTxn)
            {
                this.saveData = backupData;
                inTxn = false;
            }
        }

        void IEditableObject.EndEdit()
        {
            if (inTxn)
            {
                if (!backupData.label.Equals(saveData.label))
                {
                    OnUpdated(new UpdatedEventArgs("Label"));
                }
                if (!backupData.date.Equals(saveData.date))
                {
                    OnUpdated(new UpdatedEventArgs("SaveDate"));
                }
                if (!backupData.keep.Equals(saveData.keep))
                {
                    OnUpdated(new UpdatedEventArgs("Keep"));
                }
                /*if (!backupData.active.Equals(saveData.active))
                {
                    OnUpdated(new UpdatedEventArgs("Active"));
                }*/
                backupData = new SaveData();
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
