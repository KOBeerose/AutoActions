using CodectoryCore.Logging;
using System;
using System.IO;
using System.Text;

namespace AutoActions.Info
{
    /// <summary>
    /// Appends log entries to AutoActions.log and rolls it over to AutoActions.old.log when it gets too big.
    /// Replaces the file writing of CodectoryCore.Logging.Logs, which reads, trims and rewrites the whole file for
    /// every entry: once the file passes 2 MB its trim splits and re-concatenates the log line by line, so each
    /// entry allocates gigabytes on the large object heap and the process grows to tens of GB.
    /// </summary>
    public class LogFile
    {
        public const long MaximumSize = 2 * 1024 * 1024;

        readonly object _lockFile = new object();
        readonly string _path;
        readonly string _oldPath;
        readonly string _header;

        public bool Enabled { get; set; } = false;

        public LogFile(Logs logs, string path, string version)
        {
            _path = path;
            _oldPath = Path.ChangeExtension(path, ".old" + Path.GetExtension(path));
            _header = $"AutoActions\t {Environment.MachineName}\t {version}{Environment.NewLine}{Environment.NewLine}#######################{Environment.NewLine}{Environment.NewLine}";
            logs.LogFileEnabled = false;
            logs.NewLog += (o, entry) => Append(entry);
        }

        public void Append(LogEntry entry)
        {
            if (!Enabled || entry == null)
                return;
            lock (_lockFile)
            {
                try
                {
                    FileInfo file = new FileInfo(_path);
                    if (file.Exists && file.Length > MaximumSize)
                    {
                        File.Copy(_path, _oldPath, true);
                        File.Delete(_path);
                        file.Refresh();
                    }
                    string text = (file.Exists ? string.Empty : _header) + Environment.NewLine + entry;
                    File.AppendAllText(_path, text, Encoding.UTF8);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
