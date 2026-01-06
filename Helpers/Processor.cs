using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WinUIShared.Controls;

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
        protected static readonly IProgress<string> defaultProgressTextReporter = new Progress<string>(s => { });
        protected static readonly IProgress<double> defaultProgressValueReporter = new Progress<double>(d => { });
        protected IProgress<string> leftTextPrimary = defaultProgressTextReporter;
        protected IProgress<string> centerTextPrimary = defaultProgressTextReporter;
        protected IProgress<string> rightTextPrimary = defaultProgressTextReporter;
        protected IProgress<double> progressPrimary = defaultProgressValueReporter;
        protected IProgress<string> leftTextSecondary = defaultProgressTextReporter;
        protected IProgress<string> centerTextSecondary = defaultProgressTextReporter;
        protected IProgress<string> rightTextSecondary = defaultProgressTextReporter;
        protected IProgress<double> progressSecondary = defaultProgressValueReporter;
        protected Action<string> error = _ => { };
        protected GpuInfo? gpuInfo;

        public static bool IsAudio(string mediaPath)
        {
            var ext = Path.GetExtension(mediaPath).ToLower();
            return ext is ".mp3" or ".wav" or ".flac" or ".aac" or ".m4a" or ".wma";
        }

        protected bool CheckNoSpaceDuringProcess(string line)
        {
            if (!line.EndsWith("No space left on device") && !line.EndsWith("I/O error")) return false;
            Pause();
            error($"Process failed.\nError message: {line}");
            return true;
        }

        protected bool CheckFileNameLongError(string line)
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

        private async Task DeleteOutputFiles()
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    Debug.WriteLine($"Trial {i}");
                    if (Directory.Exists(outputFile)) Directory.Delete(outputFile, true);
                    else if (File.Exists(outputFile)) File.Delete(outputFile);
                    return;
                }
                catch (IOException)
                {
                    await Task.Delay(100);
                }
            }
        }

        public virtual async Task Cancel()
        {
            if (currentProcess == null) return;
            currentProcess.Kill();
            await currentProcess.WaitForExitAsync();
            hasBeenKilled = true;
            currentProcess = null;
            await DeleteOutputFiles();
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

        public string? GetFile() => outputFile;

        public void SetProgressAndErrorReporters(
            IProgress<string>? leftTextPrimary = null,
            IProgress<string>? centerTextPrimary = null,
            IProgress<string>? rightTextPrimary = null,
            IProgress<double>? progressPrimary = null,
            IProgress<string>? leftTextSecondary = null,
            IProgress<string>? centerTextSecondary = null,
            IProgress<string>? rightTextSecondary = null,
            IProgress<double>? progressSecondary = null,
            Action<string>? error = null)
        {
            this.leftTextPrimary = leftTextPrimary ?? defaultProgressTextReporter;
            this.centerTextPrimary = centerTextPrimary ?? defaultProgressTextReporter;
            this.rightTextPrimary = rightTextPrimary ?? defaultProgressTextReporter;
            this.progressPrimary = progressPrimary ?? defaultProgressValueReporter;

            this.leftTextSecondary = leftTextSecondary ?? defaultProgressTextReporter;
            this.centerTextSecondary = centerTextSecondary ?? defaultProgressTextReporter;
            this.rightTextSecondary = rightTextSecondary ?? defaultProgressTextReporter;
            this.progressSecondary = progressSecondary ?? defaultProgressValueReporter;

            this.error = error ?? (_ => { });
        }

        public delegate Task IntermediateProcessHandler(Process process);
        protected async Task<bool> StartProcess(string processFileName, string arguments, DataReceivedEventHandler? outputEventHandler, DataReceivedEventHandler? errorEventHandler, IntermediateProcessHandler? intermediateHandler = null)
        {
            Process process = new()
            {
                StartInfo = new ProcessStartInfo()
                {
                    FileName = processFileName,
                    Arguments = arguments,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardInputEncoding = Encoding.Default,
                    StandardOutputEncoding = Encoding.Default,
                    StandardErrorEncoding = Encoding.Default,
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
            if(intermediateHandler != null) await intermediateHandler(process);
            process.StandardInput.Close();
            await process.WaitForExitAsync();
            var success = process.ExitCode == 0;
            process.Dispose();
            currentProcess = null;
            return success;
        }

        public void EnableHardwareAccelParams(GpuInfo gpuInfo)
        {
            this.gpuInfo = gpuInfo;
        }

        public void DisableHardwareAccel()
        {
            gpuInfo = null;
        }

        public delegate void ProgressEventHandler(double progressPercent, TimeSpan currentTime, TimeSpan duration, int currentFrame);
        public delegate void LineWatchHandler(string line);
        private DataReceivedEventHandler ProgressToDataReceivedEventHandler(ProgressEventHandler progressHandler, LineWatchHandler? lineWatcher)
        {
            var duration = TimeSpan.MinValue;
            return (sender, args) =>
            {
                if (string.IsNullOrWhiteSpace(args.Data) || hasBeenKilled) return;
                Debug.WriteLine(args.Data);
                lineWatcher?.Invoke(args.Data);
                if (CheckFileNameLongError(args.Data)) return;
                if (duration == TimeSpan.MinValue)
                {
                    var matchCollection = Regex.Matches(args.Data, @"\s*Duration:\s(\d{2}:\d{2}:\d{2}\.\d{2}).+");
                    if (matchCollection.Count == 0) return;
                    duration = TimeSpan.Parse(matchCollection[0].Groups[1].Value);
                }
                if (!args.Data.StartsWith("frame")) return;
                if (!CheckNoSpaceDuringProcess(args.Data))
                {
                    var matchCollection =
                        Regex.Matches(args.Data, @"^frame=\s*(\d+)\s.+?time=(\d{2}:\d{2}:\d{2}\.\d{2}).+");
                    if (matchCollection.Count == 0) return;
                    var currentTime = TimeSpan.Parse(matchCollection[0].Groups[2].Value);
                    progressHandler.Invoke(currentTime / duration * ProgressMax, currentTime, duration,
                        int.Parse(matchCollection[0].Groups[1].Value));
                }
            };
        }

        protected Task<bool> StartFfmpegProcess(string arguments)
        {
            return StartFfmpegProcess(arguments, errorEventHandler:null);
        }

        protected Task<bool> StartFfmpegProcess(string arguments, DataReceivedEventHandler? errorEventHandler, IntermediateProcessHandler? intermediateHandler = null)
        {
            return StartProcess(ffmpegPath, $"-y {arguments}", null, errorEventHandler, intermediateHandler);
        }

        protected Task<bool> StartFfmpegProcess(string arguments, ProgressEventHandler progressHandler, LineWatchHandler? lineWatcher = null, IntermediateProcessHandler? intermediateHandler = null)
        {
            return StartFfmpegProcess(arguments, ProgressToDataReceivedEventHandler(progressHandler, lineWatcher), intermediateHandler);
        }

        protected Task<bool> StartFfmpegTranscodingProcess(IEnumerable<string> inputs, string output, string argumentsBeforeInput, string argumentsAfterInput, DataReceivedEventHandler errorEventHandler, IntermediateProcessHandler? intermediateHandler = null)
        {
            var inputParams = string.Join(" ", inputs.Select(i => GpuInfo.InputParams(gpuInfo, i)));
            return StartFfmpegProcess($"{argumentsBeforeInput} {inputParams} {argumentsAfterInput} \"{output}\"", errorEventHandler, intermediateHandler);
        }

        protected Task<bool> StartFfmpegTranscodingProcess(IEnumerable<string> inputs, string output, string argumentsBeforeInput, string argumentsAfterInput,
            ProgressEventHandler progressHandler, LineWatchHandler? lineWatcher = null, IntermediateProcessHandler? intermediateHandler = null)
        {
            var inputParams = string.Join(" ", inputs.Select(i => GpuInfo.InputParams(gpuInfo, i)));
            return StartFfmpegProcess($"{argumentsBeforeInput} {inputParams} {argumentsAfterInput} \"{output}\"", ProgressToDataReceivedEventHandler(progressHandler, lineWatcher), intermediateHandler);
        }

        protected Task<bool> StartFfmpegTranscodingProcess(IEnumerable<string> inputs, string output, int quality,
            string? preset, string extraArgumentsAfterInput, DataReceivedEventHandler errorEventHandler, IntermediateProcessHandler? intermediateHandler = null)
        {
            return StartFfmpegTranscodingProcess(inputs, output, quality, preset, string.Empty,
                extraArgumentsAfterInput, errorEventHandler, intermediateHandler);
        }

        protected Task<bool> StartFfmpegTranscodingProcess(IEnumerable<string> inputs, string output, int quality, string? preset, string extraArgumentsBeforeInput, string extraArgumentsAfterInput, DataReceivedEventHandler errorEventHandler, IntermediateProcessHandler? intermediateHandler = null)
        {
            var threadsParam = gpuInfo == null ? string.Empty : "-threads 1"; //Will limit CPU crop/scale
            var fpsModeParam = gpuInfo != null ? string.Empty : "-fps_mode passthrough";
            var encodingParams = $"-c:v {GpuInfo.EncodingParamsDict[gpuInfo?.Vendor ?? GpuVendor.None]} -c:a copy";
            var qualityParams = GpuInfo.QualityParams(gpuInfo, quality);
            var presetParams = preset == null ? string.Empty : $"-{(gpuInfo?.Vendor == GpuVendor.Amd ? "quality" : "preset")} {preset}";
            return StartFfmpegTranscodingProcess(inputs, output, $"{threadsParam} {extraArgumentsBeforeInput}", $"{encodingParams} {fpsModeParam} {qualityParams} {presetParams} {extraArgumentsAfterInput}", errorEventHandler, intermediateHandler);
        }

        protected Task<bool> StartFfmpegTranscodingProcess(IEnumerable<string> inputs, string output, int quality,
            string? preset, string extraArgumentsAfterInput, ProgressEventHandler progressHandler, LineWatchHandler? lineWatcher = null, IntermediateProcessHandler? intermediateHandler = null)
        {
            return StartFfmpegTranscodingProcess(inputs, output, quality, preset, string.Empty, extraArgumentsAfterInput, progressHandler, lineWatcher, intermediateHandler);
        }

        protected Task<bool> StartFfmpegTranscodingProcess(IEnumerable<string> inputs, string output, int quality,
            string? preset, string extraArgumentsBeforeInput, string extraArgumentsAfterInput, ProgressEventHandler progressHandler, LineWatchHandler? lineWatcher = null, IntermediateProcessHandler? intermediateHandler = null)
        {
            return StartFfmpegTranscodingProcess(inputs, output, quality, preset, extraArgumentsBeforeInput, extraArgumentsAfterInput, ProgressToDataReceivedEventHandler(progressHandler, lineWatcher), intermediateHandler);
        }

        protected Task<bool> StartFfmpegTranscodingProcessDefaultQuality(IEnumerable<string> inputs, string output, string extraArgumentsAfterInput, DataReceivedEventHandler errorEventHandler, IntermediateProcessHandler? intermediateHandler = null)
        {
            return StartFfmpegTranscodingProcessDefaultQuality(inputs, output, string.Empty, extraArgumentsAfterInput, errorEventHandler, intermediateHandler);
        }

        protected Task<bool> StartFfmpegTranscodingProcessDefaultQuality(IEnumerable<string> inputs, string output, string extraArgumentsAfterInput, ProgressEventHandler progressHandler, LineWatchHandler? lineWatcher = null, IntermediateProcessHandler? intermediateHandler = null)
        {
            return StartFfmpegTranscodingProcessDefaultQuality(inputs, output, string.Empty, extraArgumentsAfterInput, progressHandler, lineWatcher, intermediateHandler);
        }

        protected Task<bool> StartFfmpegTranscodingProcessDefaultQuality(IEnumerable<string> inputs, string output, string extraArgumentsBeforeInput, string extraArgumentsAfterInput, DataReceivedEventHandler errorEventHandler, IntermediateProcessHandler? intermediateHandler = null)
        {
            return StartFfmpegTranscodingProcess(inputs, output, 18, null, extraArgumentsBeforeInput, extraArgumentsAfterInput, errorEventHandler, intermediateHandler);
        }

        protected Task<bool> StartFfmpegTranscodingProcessDefaultQuality(IEnumerable<string> inputs, string output, string extraArgumentsBeforeInput, string extraArgumentsAfterInput, ProgressEventHandler progressHandler, LineWatchHandler? lineWatcher = null, IntermediateProcessHandler? intermediateHandler = null)
        {
            return StartFfmpegTranscodingProcess(inputs, output, 18, null, extraArgumentsBeforeInput, extraArgumentsAfterInput, progressHandler, lineWatcher, intermediateHandler);
        }

        [Flags]
        public enum ThreadAccess
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
