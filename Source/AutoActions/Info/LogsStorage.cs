using CodectoryCore;
using CodectoryCore.Logging;
using CodectoryCore.UI.Wpf;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoActions.Info
{
    public class LogsStorage : DialogViewModelBase
    {
        SortableObservableCollection<LogEntry> _entries = new SortableObservableCollection<LogEntry>();

        public SortableObservableCollection<LogEntry> Entries

        {
            get => _entries;
            private set { _entries = value; OnPropertyChanged(); }
        }

        readonly object _lockLogs = new object();

        public const int MaximumEntries = 5000;

        public LogsStorage()
        {
            Globals.Logs.NewLog += Logs_NewLog;
        }

        private void Logs_NewLog(object sender, LogEntry e)
        {
            lock (_lockLogs)
            {
                // Entries arrive nearly in order, so insert in place instead of re-sorting the whole list each time,
                // and keep only the newest ones so the list can't grow without limit.
                int index = Entries.Count;
                while (index > 0 && Entries[index - 1].Date > e.Date)
                    index--;
                Entries.Insert(index, e);
                while (Entries.Count > MaximumEntries)
                    Entries.RemoveAt(0);
            }
        }

    }
}
