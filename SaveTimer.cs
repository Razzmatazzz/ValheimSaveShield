using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace ValheimSaveShield
{
    class SaveTimer : Timer
    {
        public SaveFile Save { get; set; }

        //new public event EventHandler<SaveTimerElapsedEventArgs> Elapsed;

        public SaveTimer(SaveFile saveFile) : base()
        {
            Save = saveFile;
            //base.Elapsed += Parent_Elapsed;
        }

        /*private void Parent_Elapsed(object sender, ElapsedEventArgs e)
        {
            OnElapsed(new SaveTimerElapsedEventArgs(this.Save, e.SignalTime));
        }

        private void OnElapsed(SaveTimerElapsedEventArgs args)
        {
            EventHandler<SaveTimerElapsedEventArgs> handler = Elapsed;
            if (null != handler) handler(this, args);
        }*/
    }

    /*class SaveTimerElapsedEventArgs : EventArgs
    {
        private readonly SaveFile _save;
        private readonly DateTime _signalTime;
        public SaveFile Save { get { return _save; } }
        public DateTime SignalTime { get { return _signalTime; } }
        public SaveTimerElapsedEventArgs(SaveFile save, DateTime signaltime) 
        {
            _save = save;
            _signalTime = signaltime;
        }
    }*/
}
