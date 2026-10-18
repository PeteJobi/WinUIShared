using FileLogger;
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
    public class Processor(string ffmpegPath, IFileLogger logger)
    {
        protected Process? currentProcess;
        protected bool hasBeenKilled;
        protected const double ProgressMax = 100d;
        protected string? outputFile;
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
        protected IFileLogger logger = logger;

        public async Task<string> GetGpuPixelFormat(string videoPath)
        {
            string? over8BitDepth = null;
            var depthGotten = false;
            await StartFfmpegProcess($"-i \"{videoPath}\"", (sender, args) =>
            {
                Debug.WriteLine(args.Data);
                if (depthGotten || string.IsNullOrWhiteSpace(args.Data) || hasBeenKilled) return;
                var matchCollection = Regex.Matches(args.Data, @"\s*Stream #\d+:\d+.*?: Video: .+?, yuv420p(?<moreThan8>\d{2}le)?");
                if (matchCollection.Count == 0) return;
                over8BitDepth = matchCollection[0].Groups["moreThan8"].Value;
                depthGotten = true;
            });

            return over8BitDepth switch
            {
                "10le" => "p010le", //10 bit
                "12le" => "p012le", //12 bit
                _ => "nv12", //8 bit
            };
        }

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

        protected bool CheckFailureStrings(string line)
        {
            if (line is not ("Conversion failed!" or "Error : Invalid argument")) return false;
            error("Process failed");
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
            if (outputFile == null) return;
            var info = new ProcessStartInfo
            {
                FileName = "explorer",
                Arguments = $"/e, /select, \"{GetSafePath(outputFile)}\""
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
            logger.Log(arguments);
            Process process = new()
            {
                StartInfo = new ProcessStartInfo
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
                logger.Log(args.Data);
                lineWatcher?.Invoke(args.Data);
                if (CheckFailureStrings(args.Data)) return;
                if (duration == TimeSpan.MinValue)
                {
                    var matchCollection = Regex.Matches(args.Data, @"\s*Duration:\s(\d{2}:\d{2}:\d{2}\.\d{2}).+");
                    if (matchCollection.Count != 0) duration = TimeSpan.Parse(matchCollection[0].Groups[1].Value);
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

        protected FfmpegProcessBuilder Ffmpeg() => new(this, ffmpegPath);


        [Flags]
        public enum ThreadAccess
        {
            SUSPEND_RESUME = 0x0002
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

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        static extern uint GetShortPathName(string lpszLongPath, StringBuilder lpszShortPath, uint cchBuffer);

        public static string GetSafePath(string path)
        {
            // If the path is short enough, return as-is
            if (path.Length < 260) return path;

            // Otherwise, try to get the 8.3 short path
            var shortPath = new StringBuilder(260);
            const string prefix = @"\\?\";
            var result = GetShortPathName(prefix + path, shortPath, (uint)shortPath.Capacity);

            if (result > 0) return shortPath.ToString()[prefix.Length..];
            return path; // fallback if 8.3 not available
        }

        public class FfmpegProcessBuilder(Processor processor, string ffmpegPath)
        {
            private IEnumerable<string>? _inputPaths;
            private string? _outputPath;
            private string? _argumentsBeforeInput;
            private string? _argumentsAfterInput;
            private string? _videoFilter;
            private string? _audioFilter;
            private string? _complexFilter;
            private int? _quality;
            private string? _preset;
            private string? _videoEncoding;
            private string? _audioEncoding;
            private string? _subtitleEncoding;
            private bool _dontAddGpuParams;
            private bool _dontAddQualityParams;
            protected bool _disableHardwareDecoding;
            protected bool _doPlainProcessing;
            protected bool _noOutput;
            private DataReceivedEventHandler? _outputEventHandler;
            private DataReceivedEventHandler? _errorEventHandler;
            private IntermediateProcessHandler? _intermediateHandler;

            public async Task<bool> StartTranscodingProcess()
            {
                if (_inputPaths?.Any() != true)
                {
                    processor.error("No input paths specified.");
                    return false;
                }
                if (!_noOutput && _outputPath == null)
                {
                    processor.error("No output path specified.");
                    return false;
                }

                string inputParams;
                string qualityParams;
                string presetParams;
                string videoEncodingParam;
                string audioEncodingParam;
                var threadsParam = string.Empty;
                var fpsModeParam = string.Empty;
                var initParams = string.Empty;

                if (_doPlainProcessing)
                {
                    inputParams = string.Join(" ", _inputPaths.Select(i => $"-i \"{i}\""));
                    qualityParams = _quality == null ? string.Empty : GpuInfo.QualityParams(null, _quality.Value);
                    presetParams = _preset == null ? string.Empty : $"-preset {_preset}";
                    videoEncodingParam = _videoEncoding == null ? string.Empty : $"-c:v {_videoEncoding}";
                    audioEncodingParam = _audioEncoding == null ? string.Empty : $"-c:a {_audioEncoding}";
                }
                else
                {
                    inputParams = string.Join(" ", _inputPaths.Select(i => $"{(_disableHardwareDecoding ? string.Empty : GpuInfo.DecodingParams(processor.gpuInfo))}-i \"{i}\""));
                    qualityParams = _dontAddQualityParams ? string.Empty : GpuInfo.QualityParams(processor.gpuInfo, _quality ?? 18);
                    presetParams = _preset == null ? string.Empty : $"-{(processor.gpuInfo?.Vendor == GpuVendor.Amd ? "quality" : "preset")} {_preset}";
                    threadsParam = processor.gpuInfo == null || _dontAddGpuParams ? string.Empty : "-threads 1"; //Will limit CPU crop/scale
                    fpsModeParam = processor.gpuInfo != null || _dontAddGpuParams ? string.Empty : "-fps_mode passthrough";
                    initParams = _disableHardwareDecoding ? string.Empty : GpuInfo.InitializeParams(processor.gpuInfo);
                    videoEncodingParam = $"-c:v {_videoEncoding ?? GpuInfo.EncodingParams(processor.gpuInfo)}";
                    audioEncodingParam = $"-c:a {_audioEncoding ?? "copy"}";
                }
                var subtitleEncodingParam = _subtitleEncoding == null ? string.Empty : $"-c:s {_subtitleEncoding}";

                var arguments = $"-y {initParams} {threadsParam} {_argumentsBeforeInput ?? string.Empty} {inputParams} {videoEncodingParam} {audioEncodingParam} {subtitleEncodingParam} {fpsModeParam} {qualityParams} {presetParams} {_argumentsAfterInput ?? string.Empty}" +
                                $" {(_videoFilter == null ? string.Empty : $"-vf \"{_videoFilter}\" ")}" +
                                $"{(_audioFilter == null ? string.Empty : $"-af \"{_audioFilter}\" ")}" +
                                $"{(_complexFilter == null ? string.Empty : $"-filter_complex \"{_complexFilter}\" ")}" +
                                $"{(_noOutput ? string.Empty : $"\"{_outputPath}\"")}";

                return await processor.StartProcess(ffmpegPath, arguments, _outputEventHandler, _errorEventHandler, _intermediateHandler);
            }

            public Task<bool> StartPlainProcess()
            {
                _doPlainProcessing = true;
                return StartTranscodingProcess();
            }

            public FfmpegProcessBuilder Input(string inputPath)
            {
                _inputPaths = [inputPath];
                return this;
            }

            public FfmpegProcessBuilder Inputs(params string[] inputPaths)
            {
                _inputPaths = inputPaths;
                return this;
            }

            public FfmpegProcessBuilder Output(string outputPath)
            {
                _outputPath = outputPath;
                return this;
            }

            public FfmpegProcessBuilder NoOutput()
            {
                _noOutput = true;
                return this;
            }

            public FfmpegProcessBuilder ArgumentsBeforeInput(string arguments)
            {
                _argumentsBeforeInput = arguments;
                return this;
            }

            public FfmpegProcessBuilder ArgumentsAfterInput(string arguments)
            {
                _argumentsAfterInput = arguments;
                return this;
            }

            public FfmpegProcessBuilder VideoFilter(string videoFilter)
            {
                _videoFilter = videoFilter;
                return this;
            }

            public FfmpegProcessBuilder AudioFilter(string audioFilter)
            {
                _audioFilter = audioFilter;
                return this;
            }

            public FfmpegProcessBuilder ComplexFilter(string complexFilter)
            {
                _complexFilter = complexFilter;
                return this;
            }

            public FfmpegProcessBuilder Quality(int quality)
            {
                _quality = quality;
                return this;
            }

            public FfmpegProcessBuilder DontAddQuality()
            {
                _dontAddQualityParams = true;
                return this;
            }

            public FfmpegProcessBuilder Preset(string preset)
            {
                _preset = preset;
                return this;
            }

            public FfmpegProcessBuilder VideoEncoding(string videoEncoding)
            {
                _videoEncoding = videoEncoding;
                return this;
            }

            public FfmpegProcessBuilder AudioEncoding(string audioEncoding)
            {
                _audioEncoding = audioEncoding;
                return this;
            }

            public FfmpegProcessBuilder SubtitleEncoding(string subtitleEncoding)
            {
                _subtitleEncoding = subtitleEncoding;
                return this;
            }

            public FfmpegProcessBuilder DontAddGpuParams()
            {
                _dontAddGpuParams = true;
                return this;
            }

            public FfmpegProcessBuilder DisableHardwareEncoding(bool disable)
            {
                _disableHardwareDecoding = disable;
                return this;
            }

            public FfmpegProcessBuilder OutputHandler(DataReceivedEventHandler outputEventHandler)
            {
                _outputEventHandler = outputEventHandler;
                return this;
            }

            public FfmpegProcessBuilder ErrorHandler(DataReceivedEventHandler errorEventHandler)
            {
                _errorEventHandler = errorEventHandler;
                return this;
            }

            public FfmpegProcessBuilder ProgressHandler(ProgressEventHandler progressHandler)
            {
                _errorEventHandler = processor.ProgressToDataReceivedEventHandler(progressHandler, null);
                return this;
            }

            public FfmpegProcessBuilder ProgressHandler(ProgressEventHandler progressHandler, LineWatchHandler lineWatchHandler)
            {
                _errorEventHandler = processor.ProgressToDataReceivedEventHandler(progressHandler, lineWatchHandler);
                return this;
            }

            public FfmpegProcessBuilder IntermediateHandler(IntermediateProcessHandler intermediateHandler)
            {
                _intermediateHandler = intermediateHandler;
                return this;
            }
        }
    }
}
