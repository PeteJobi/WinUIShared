using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace WinUIShared.Helpers
{
    public class Processor(string ffmpegPath)
    {
        protected Process? currentProcess;
        protected bool hasBeenKilled;
        protected const double ProgressMax = 100d;
        protected string? outputFile;
        protected const string FileNameLongError =
            "The source file name is too long. Shorten it to get the total number of characters in the destination directory lower than 256.\n\nDestination directory: ";
        
        protected bool CheckNoSpaceDuringProcess(string line, Action<string> error)
        {
            if (!line.EndsWith("No space left on device") && !line.EndsWith("I/O error")) return false;
            Pause();
            error($"Process failed.\nError message: {line}");
            return true;
        }

        protected static bool CheckFileNameLongErrorSplit(string line, Action<string> error)
        {
            const string noSuchDirectory = ": No such file or directory";
            if (!line.EndsWith(noSuchDirectory)) return false;
            error(FileNameLongError + line[..^noSuchDirectory.Length]);
            return true;
        }

        protected bool HasBeenKilled()
        {
            if (!hasBeenKilled) return false;
            hasBeenKilled = false;
            return true;
        }

        public async Task Cancel()
        {
            if (currentProcess == null) return;
            currentProcess.Kill();
            await currentProcess.WaitForExitAsync();
            hasBeenKilled = true;
            currentProcess = null;
            if (Directory.Exists(outputFile)) Directory.Delete(outputFile, true);
            else if (File.Exists(outputFile)) File.Delete(outputFile);
        }

        public void Pause()
        {
            if (currentProcess == null || currentProcess.HasExited) return;
            SuspendProcess(currentProcess);
        }

        public void Resume()
        {
            if (currentProcess == null || currentProcess.HasExited) return;
            ResumeProcess(currentProcess);
        }

        public void ViewFile()
        {
            var info = new ProcessStartInfo
            {
                FileName = "explorer",
                Arguments = $"/e, /select, \"{outputFile}\""
            };
            Process.Start(info);
        }

        protected async Task<bool> StartProcess(string processFileName, string arguments, DataReceivedEventHandler? outputEventHandler, DataReceivedEventHandler? errorEventHandler)
        {
            Process process = new()
            {
                StartInfo = new ProcessStartInfo()
                {
                    FileName = processFileName,
                    Arguments = arguments,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                },
                EnableRaisingEvents = true
            };
            process.OutputDataReceived += outputEventHandler;
            process.ErrorDataReceived += errorEventHandler;
            process.Start();
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();
            hasBeenKilled = false;
            currentProcess = process;
            await process.WaitForExitAsync();
            var success = process.ExitCode == 0;
            process.Dispose();
            currentProcess = null;
            return success;
        }

        public async Task GetGPUs()
        {
            await StartProcess("powershell", "-NoProfile -Command \"Get-CimInstance Win32_VideoController | ForEach-Object { \\\"$($_.Caption);$($_.DeviceID);$($_.AdapterCompatibility)\\\" }\"", (sender, args) =>
            {
                if (string.IsNullOrWhiteSpace(args.Data)) return;
                Debug.WriteLine(args.Data);
            }, null);
        }

        protected Task<bool> StartFfmpegProcess(string arguments, DataReceivedEventHandler? errorEventHandler)
        {
            return StartProcess(ffmpegPath, arguments, null, errorEventHandler);
        }

        [Flags]
        public enum ThreadAccess : int
        {
            SUSPEND_RESUME = (0x0002)
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenThread(ThreadAccess dwDesiredAccess, bool bInheritHandle, uint dwThreadId);
        [DllImport("kernel32.dll")]
        private static extern uint SuspendThread(IntPtr hThread);
        [DllImport("kernel32.dll")]
        static extern int ResumeThread(IntPtr hThread);
        [DllImport("kernel32", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        private static void SuspendProcess(Process process)
        {
            foreach (ProcessThread pT in process.Threads)
            {
                IntPtr pOpenThread = OpenThread(ThreadAccess.SUSPEND_RESUME, false, (uint)pT.Id);

                if (pOpenThread == IntPtr.Zero)
                {
                    continue;
                }

                SuspendThread(pOpenThread);

                CloseHandle(pOpenThread);
            }
        }

        public static void ResumeProcess(Process process)
        {
            if (process.ProcessName == string.Empty)
                return;

            foreach (ProcessThread pT in process.Threads)
            {
                IntPtr pOpenThread = OpenThread(ThreadAccess.SUSPEND_RESUME, false, (uint)pT.Id);

                if (pOpenThread == IntPtr.Zero)
                {
                    continue;
                }

                int suspendCount;
                do
                {
                    suspendCount = ResumeThread(pOpenThread);
                } while (suspendCount > 0);

                CloseHandle(pOpenThread);
            }
        }
    }
}
