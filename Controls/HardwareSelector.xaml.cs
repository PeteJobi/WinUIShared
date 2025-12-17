using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using WinUIShared.Helpers;

namespace WinUIShared.Controls
{
    public sealed partial class HardwareSelector : UserControl
    {
        private ViewModel viewModel = new();
        private GPUInfo? lastSelectedGpu;

        public static readonly DependencyProperty ProcessorProperty = DependencyProperty.Register(
            nameof(Processor),
            typeof(Processor),
            typeof(HardwareSelector),
            new PropertyMetadata(null, OnProcessorChanged));

        public Processor Processor
        {
            get => (Processor)GetValue(ProcessorProperty);
            set => SetValue(ProcessorProperty, value);
        }

        public static readonly DependencyProperty SelectedGpuProperty = DependencyProperty.Register(
            nameof(SelectedGpu),
            typeof(GPUInfo),
            typeof(HardwareSelector),
            new PropertyMetadata(null, OnSelectedGPUChanged));

        public GPUInfo? SelectedGpu
        {
            get => (GPUInfo?)GetValue(SelectedGpuProperty);
            set => SetValue(SelectedGpuProperty, value);
        }

        public HardwareSelector()
        {
            InitializeComponent();
        }

        private static async void OnProcessorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var selector = (HardwareSelector)d;
            if (selector.Processor == null) return;
            var gpuList = await selector.Processor.GetGpUs();
            selector.viewModel.GPUs = new ObservableCollection<GPUInfo>(gpuList);
            if(selector.SelectedGpu != null) OnSelectedGPUChanged(d, e);
        }

        private static void OnSelectedGPUChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var selector = (HardwareSelector)d;
            if (selector.SelectedGpu == null)
            {
                selector.UseHardwareAccel.IsChecked = false;
                return;
            }
            var gpuList = selector.viewModel.GPUs;
            if (gpuList == null) return;
            if (gpuList.Count > 0)
            {
                if (!gpuList.Contains(selector.SelectedGpu))
                {
                    selector.SelectedGpu = gpuList.First(g => g.DeviceID == selector.SelectedGpu.DeviceID);
                }
                else
                {
                    if(selector.UseHardwareAccel.IsChecked != true)
                        selector.UseHardwareAccel.IsChecked = true;
                    selector.lastSelectedGpu = selector.SelectedGpu;
                }
            }
            else selector.SelectedGpu = null;
        }

        private void OnChecked(object sender, RoutedEventArgs e)
        {
            SelectedGpu ??= lastSelectedGpu ?? viewModel.GPUs.First();
        }

        private void OnUnchecked(object sender, RoutedEventArgs e)
        {
            SelectedGpu = null;
        }
    }

    internal class ViewModel: INotifyPropertyChanged
    {
        private ObservableCollection<GPUInfo> _gpus;
        public ObservableCollection<GPUInfo> GPUs
        {
            get => _gpus;
            set => SetProperty(ref _gpus, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    public enum GPUVendor
    {
        None,
        Nvidia,
        AMD,
        Intel
    }

    public class GPUInfo
    {
        public string Name { get; set; }
        public int DeviceID { get; set; }
        public GPUVendor Vendor { get; set; }
        public GPUInfo(string name, int deviceID, GPUVendor vendor)
        {
            Name = name;
            DeviceID = deviceID;
            Vendor = vendor;
        }

        public override string ToString() => Name;

        public static string InputParams(GPUInfo? gpuInfo, string input)
        {
            return gpuInfo?.Vendor switch
            {
                GPUVendor.Nvidia => $"-hwaccel cuda -hwaccel_output_format cuda -hwaccel_device {gpuInfo.DeviceID} -i \"{input}\"",
                GPUVendor.AMD => $"-hwaccel d3d11va -hwaccel_output_format d3d11 -hwaccel_device {gpuInfo.DeviceID} -i \"{input}\"",
                GPUVendor.Intel => $"-hwaccel qsv -hwaccel_output_format qsv -hwaccel_device {gpuInfo.DeviceID} -i \"{input}\"",
                _ => $"-i \"{input}\""
            };
        }

        public static string QualityParams(GPUInfo? gpuInfo, int quality)
        {
            return gpuInfo?.Vendor switch
            {
                GPUVendor.Nvidia => $"-rc vbr -b:v 0 -cq {quality}",
                GPUVendor.AMD => $"-qp_i {quality} -qp_p {quality} -qp_b {quality}",
                GPUVendor.Intel => $"-rc icq -global_quality {quality}",
                _ => $"-crf {quality}"
            };
        }

        public static string PresetParams(GPUInfo? gpuInfo, int presetIndex)
        {
            var presets = Presets[gpuInfo?.Vendor ?? GPUVendor.None];
            if (presetIndex < 0 || presetIndex >= presets.Length)
                throw new ArgumentOutOfRangeException(
                    $"Preset level {presetIndex} does not exist for {gpuInfo?.Vendor}");
            return $"-{(gpuInfo?.Vendor == GPUVendor.AMD ? "quality" : "preset")} {presets[presetIndex]}";
        }

        public static readonly Dictionary<GPUVendor, string> EncodingParamsDict = new()
        {
            { GPUVendor.None, "libx265" },
            { GPUVendor.Nvidia, "hevc_nvenc" },
            { GPUVendor.AMD, "hevc_amf" },
            { GPUVendor.Intel, "hevc_qsv" }
        };

        public static readonly Dictionary<GPUVendor, string[]> Presets = new()
        {
            { GPUVendor.None, ["ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow", "placebo"] },
            //{ GPUVendor.Nvidia, ["p1", "p2", "p3", "p4", "p5", "p6", "p7", "p8", "p9", "p10"] },
            { GPUVendor.Nvidia, ["default", "slow", "medium", "fast", "hp", "hq", "bd", "ll", "llhq", "llhp", "lossless", "losslessh"] },
            { GPUVendor.AMD, ["speed", "balanced", "quality"] },
            { GPUVendor.Intel, ["veryfast", "fast", "medium", "slow", "veryslow"] }
        };
    }
}