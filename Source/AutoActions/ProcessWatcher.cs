using AutoActions.Threading;
using AutoActions.UWP;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutoActions
{
    public class ProcessWatcher : IManagedThread
    {
        public bool OneProcessIsRunning { get; private set; } = false;
        public bool OneProcessIsFocused { get; private set; } = false;

        Thread _watchProcessThread = null;

        readonly object _applicationsLock = new object();
        readonly object _accessLock = new object();

        Dictionary<ApplicationItem, ApplicationState> _applications = new Dictionary<ApplicationItem, ApplicationState>();
        public IReadOnlyDictionary<ApplicationItem, ApplicationState> Applications
        {
            get
            {
                lock (_applicationsLock)
                    return new ReadOnlyDictionary<ApplicationItem, ApplicationState>(_applications.ToDictionary(entry => entry.Key, entry => entry.Value));
            }
        }

        bool _stopRequested = false;
        bool _isRunning = false;
        public bool IsRunning { get => _isRunning; private set => _isRunning = value; }

        public bool ManagedThreadIsActive => IsRunning;

        public event EventHandler<string> NewLog;
        //public event EventHandler OneProcessIsRunningChanged;
        //public event EventHandler FocusedProcessChanged;
        public event EventHandler<ApplicationChangedEventArgs> ApplicationChanged;


        public ProcessWatcher()

        {
        }


        private void CallNewLog(string logMessage)
        {
            NewLog?.Invoke(this, logMessage);
        }
  
        public void AddProcess(ApplicationItem application)
        {
            lock (_applicationsLock)
            {
                if (!_applications.ContainsKey(application))
                {
                    _applications.Add(application, ApplicationState.None);
                    CallNewLog($"Application added to process watcher: {application}");
                }
            }
        }

        public void RemoveProcess(ApplicationItem application)
        {
            lock (_applicationsLock)
            {
                if (_applications.ContainsKey(application))
                {
                    _applications.Remove(application);
                    CallNewLog($"Application removed process watcher: {application}");
                }
            }
        }


        public void StartManagedThread()
        {
            if (_stopRequested || IsRunning)
                return;
            lock (_accessLock)
            {
                CallNewLog($"Starting process watcher...");
                //startWatch.Start();
                //stopWatch.Start();
                _isRunning = true;
                _watchProcessThread = new Thread(WatchProcessLoop);
                _watchProcessThread.IsBackground = true;
                _watchProcessThread.Start();
                CallNewLog($"Process watcher started");
            }
        }

        public void StopManagedThread()
        {
            if (_stopRequested || !IsRunning)
                return;
            lock (_accessLock)
            {
                CallNewLog($"Stopping process watcher...");
                _stopRequested = true;
                _watchProcessThread.Join();
                _stopRequested = false;
                _isRunning = false;
                _watchProcessThread = null;
                CallNewLog($"Process watcher stopped.");
            }
        }

        private void WatchProcessLoop()
        {
            while (!_stopRequested)
            {
                try
                {
                    lock (_applicationsLock)
                        UpdateApplications();
                }
                catch (Exception ex)
                {
                    // E.g. a process exiting between the snapshot and a lookup; an unhandled exception here ends AutoActions.
                    CallNewLog($"Process watcher: {ex.GetType().Name}: {ex.Message}");
                }
                Thread.Sleep(Globals.GlobalRefreshInterval);
            }
        }

        private void CallApplicationChanged(ApplicationItem application, ApplicationChangedType changedType)
        {
            ApplicationChanged?.Invoke(this, new ApplicationChangedEventArgs(application,changedType));

        }
        private void UpdateApplications()
        {

            lock (_applicationsLock)
            {


                List<ApplicationItem> applications = _applications.Select(a => a.Key).ToList();

                Process[] processes = Process.GetProcesses();
                int? foregroundProcessId = null;
                try
                {
                    foreach (ApplicationItem application in applications)
                    {
                        bool isRunning = false;
                        bool isFocused = false;
                        ApplicationState oldState = _applications[application];
                        foreach (var process in processes)
                        {
                            string processName;
                            if (process.ProcessName == "WWAHost")
                            {
                                processName = UWP.WWAHostHandler.GetProcessName(process.Id);
                            }
                            else
                                processName = process.ProcessName;
                            if (application.ApplicationName.ToUpperInvariant().Equals(processName.ToUpperInvariant())
                                || (application.IsUWP && !string.IsNullOrEmpty(application.UWPIdentity) && processName.Contains(application.UWPIdentity)))
                            {
                                isRunning = true;
                                if (foregroundProcessId == null)
                                    foregroundProcessId = GetForegroundProcessId();
                                // Several processes can share the name (launcher + game); one focused is enough.
                                // Letting a later unfocused one reset the state to Running re-fired GotFocus every tick.
                                if (process.Id == foregroundProcessId)
                                    isFocused = true;
                            }
                        }
                        ApplicationState state = isFocused ? ApplicationState.Focused : isRunning ? ApplicationState.Running : ApplicationState.None;

                        _applications[application] = state;
                        if (isRunning && oldState == ApplicationState.None)
                            CallApplicationChanged(application, ApplicationChangedType.Started);
                        if (isFocused && oldState != ApplicationState.Focused)
                            CallApplicationChanged(application, ApplicationChangedType.GotFocus);
                        if (isRunning && !isFocused && oldState == ApplicationState.Focused)
                            CallApplicationChanged(application, ApplicationChangedType.LostFocus);
                        if (!isRunning && oldState != ApplicationState.None)
                            CallApplicationChanged(application, ApplicationChangedType.Closed);
                    }
                }
                finally
                {
                    foreach (Process process in processes)
                        process.Dispose();
                }
            }
        }

        private int GetForegroundProcessId()
        {
            try
            {
                using (Process foregroundProcess = GetForegroundProcess())
                    return foregroundProcess?.Id ?? -1;
            }
            catch (ArgumentException)
            {
                // The foreground process exited in the meantime.
                return -1;
            }
        }


        private Process GetForegroundProcess()
        {
            var foregroundProcess = Process.GetProcessById(WinAPIFunctions.GetWindowProcessId(WinAPIFunctions.GetforegroundWindow()));
            if (foregroundProcess.ProcessName == "ApplicationFrameHost")
            {
                foregroundProcess = GetRealProcess(foregroundProcess);
            }
            return foregroundProcess;
        }

        private Process GetRealProcess(Process foregroundProcess)
        {
            Process realActiveProcess = null;

            WinAPIFunctions.WindowEnumProc callback = (hwnd, lparam) =>
            {
                var process = Process.GetProcessById(WinAPIFunctions.GetWindowProcessId(hwnd));
                if (process.ProcessName != "ApplicationFrameHost")
                {
                    realActiveProcess = process;
                }
                return true;
            };
            WinAPIFunctions.EnumChildWindows(foregroundProcess.MainWindowHandle, callback, IntPtr.Zero);
            return realActiveProcess;
        }
    }
}
