using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        public SaveWatcher(string path) : this(path, null) { }
        public SaveWatcher(string path, EventHandler<SaveWatcherLogMessageEventArgs> logEventHandler)
        {
            if (logEventHandler != null)
            {
                LogMessage += logEventHandler;
            }
            SavePath = path;
            WorldWatcher = new FileSystemWatcher();
            if (!Directory.Exists($@"{path}\worlds_local"))
            {
                Directory.CreateDirectory($@"{path}\worlds_local");
            }
            WorldWatcher.Path = $@"{path}\worlds_local";

            // Watch for changes in LastWrite times.
            WorldWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName;

            // Only watch .db files.
            WorldWatcher.Filter = "*.db";

            CharacterWatcher = new FileSystemWatcher();
            if (!Directory.Exists($@"{path}\characters_local"))
            {
                Directory.CreateDirectory($@"{path}\characters_local");
            }
            CharacterWatcher.Path = $@"{path}\characters_local";

            // Watch for changes in LastWrite and file creation times.
            CharacterWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName;

            // Only watch .fch files.
            CharacterWatcher.Filter = "*.fch";
        }
        private void logMessage(string message)
        {
            logMessage(message, LogType.Normal);
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
