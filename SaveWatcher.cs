using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ValheimSaveShield
{
    public class SaveWatcher : IDisposable
    {
        public FileSystemWatcher WorldWatcher { get; set; }
        public FileSystemWatcher CharacterWatcher { get; set; }
        public string SavePath { get; set; }
        public event EventHandler<SaveWatcherLogMessageEventArgs> LogMessage;
        public SaveWatcher(string path)
        {
            SavePath = path;
            WorldWatcher = new FileSystemWatcher();
            if (Directory.Exists($@"{path}\worlds"))
            {
                WorldWatcher.Path = $@"{path}\worlds";
            }
            else
            {
                logMessage($@"Folder {path}\worlds does not exist. Please set the correct location of your save files.", LogType.Error);
            }

            // Watch for changes in LastWrite times.
            WorldWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName;

            // Only watch .db files.
            WorldWatcher.Filter = "*.db";

            CharacterWatcher = new FileSystemWatcher();
            if (Directory.Exists($@"{path}\characters"))
            {
                CharacterWatcher.Path = $@"{path}\characters";
            }
            else
            {
                Directory.CreateDirectory($@"{path}\characters");
                //logMessage($@"Folder {saveDirPath}\characters does not exist. Please set the correct location of your save files.", LogType.Error);
            }

            // Watch for changes in LastWrite and file creation times.
            CharacterWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName;

            // Only watch .fch files.
            CharacterWatcher.Filter = "*.fch";

            if (WorldWatcher.Path == "")
            {
                WorldWatcher.EnableRaisingEvents = false;
            }
            if (CharacterWatcher.Path == "")
            {
                CharacterWatcher.EnableRaisingEvents = false;
            }
        }
        private void logMessage(string message, LogType logtype)
        {
            OnLogMessage(new SaveWatcherLogMessageEventArgs(this, message, logtype));
        }
        private void OnLogMessage(SaveWatcherLogMessageEventArgs args)
        {
            EventHandler<SaveWatcherLogMessageEventArgs> handler = LogMessage;
            if (null != handler) handler(this, args);
        }
        public void Dispose()
        {
            WorldWatcher.Dispose();
            CharacterWatcher.Dispose();
        }
    }

    public class SaveWatcherLogMessageEventArgs : LogMessageEventArgs
    {
        private readonly SaveWatcher _savewatcher;
        public SaveWatcherLogMessageEventArgs(SaveWatcher watcher, string message, LogType logtype) : base(message, logtype)
        {
            _savewatcher = watcher;
        }
        public SaveWatcherLogMessageEventArgs(SaveWatcher watcher, string message) : this(watcher, message, LogType.Normal) { }

        public SaveWatcher SaveWatcher
        {
            get { return _savewatcher; }
        }
    }
}
