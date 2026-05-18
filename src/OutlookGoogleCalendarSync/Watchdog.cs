using log4net;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace OutlookGoogleCalendarSync {
    /// <summary>
    /// Self-contained restart watchdog. Registers a Windows Scheduled Task that periodically
    /// re-invokes this same .exe with <see cref="CliFlag"/>; the watchdog invocation checks
    /// whether a normal OGCS instance is running and relaunches it if not.
    ///
    /// No external script needed: because OGCS is a WinExe, Task Scheduler can launch it
    /// directly with zero console flash.
    /// </summary>
    internal static class Watchdog {
        private static readonly ILog log = LogManager.GetLogger(typeof(Watchdog));

        public const string CliFlag = "--watchdog";
        private const string TaskName = "OGCS_Watchdog";
        private const int IntervalMinutes = 1;

        public static Boolean IsWatchdogInvocation(string[] args) {
            return args != null && args.Any(a => string.Equals(a, CliFlag, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Watchdog-mode entry: if no main OGCS instance is running, relaunch it. Always exits.
        /// </summary>
        public static void RunCheck() {
            try {
                string exePath = Application.ExecutablePath;
                int myPid = Process.GetCurrentProcess().Id;
                string processName = Path.GetFileNameWithoutExtension(exePath);

                Boolean mainInstanceAlive = false;
                foreach (Process p in Process.GetProcessesByName(processName)) {
                    try {
                        if (p.Id == myPid) continue;
                        string otherPath = null;
                        try { otherPath = p.MainModule?.FileName; } catch { /* 32/64-bit or access denied */ }
                        if (!string.IsNullOrEmpty(otherPath) &&
                            !otherPath.Equals(exePath, StringComparison.OrdinalIgnoreCase)) continue;

                        string cmdLine = TryGetCommandLine(p.Id);
                        if (cmdLine != null && cmdLine.IndexOf(CliFlag, StringComparison.OrdinalIgnoreCase) >= 0) continue;

                        mainInstanceAlive = true;
                        break;
                    } finally {
                        p.Dispose();
                    }
                }

                if (mainInstanceAlive) return;

                ProcessStartInfo psi = new ProcessStartInfo(exePath) {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? "",
                };
                Process.Start(psi);
            } catch (System.Exception ex) {
                try { log.Error("Watchdog check failed: " + ex.Message); } catch { /* logger may not be initialised */ }
            }
        }

        private static string TryGetCommandLine(int processId) {
            try {
                using (var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + processId)) {
                    foreach (System.Management.ManagementObject mo in searcher.Get()) {
                        return mo["CommandLine"] as string;
                    }
                }
            } catch { /* WMI can fail under low privilege */ }
            return null;
        }

        /// <summary>
        /// Create or update the scheduled task so it points at the current executable.
        /// Idempotent; safe to call on every startup / version change.
        /// </summary>
        public static void EnsureScheduledTask() {
            try {
                string exePath = Application.ExecutablePath;
                // schtasks /tr requires the action quoted as a single token; embedded quotes via \" .
                string action = "\\\"" + exePath + "\\\" " + CliFlag;
                string args = "/Create /TN \"" + TaskName + "\" /TR \"" + action + "\" /SC MINUTE /MO " + IntervalMinutes + " /IT /F";
                int exitCode = RunSchtasks(args);
                if (exitCode == 0) log.Info("Watchdog scheduled task registered (every " + IntervalMinutes + " min).");
                else log.Warn("Watchdog scheduled task registration returned exit code " + exitCode + ".");
            } catch (System.Exception ex) {
                log.Error("Failed to register watchdog scheduled task: " + ex.Message);
            }
        }

        public static void RemoveScheduledTask() {
            try {
                int exitCode = RunSchtasks("/Delete /TN \"" + TaskName + "\" /F");
                if (exitCode == 0) log.Info("Watchdog scheduled task removed.");
            } catch (System.Exception ex) {
                log.Error("Failed to remove watchdog scheduled task: " + ex.Message);
            }
        }

        private static int RunSchtasks(string arguments) {
            ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", arguments) {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using (Process proc = Process.Start(psi)) {
                proc.WaitForExit(15000);
                return proc.HasExited ? proc.ExitCode : -1;
            }
        }
    }
}
