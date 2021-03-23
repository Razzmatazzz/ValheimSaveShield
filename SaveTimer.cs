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

        public SaveTimer(SaveFile saveFile) : base()
        {
            Save = saveFile;
        }
    }
}
