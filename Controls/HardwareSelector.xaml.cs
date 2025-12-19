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
        private GpuInfo? lastSelectedGpu;

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
            typeof(GpuInfo),
            typeof(HardwareSelector),
            new PropertyMetadata(null, OnSelectedGPUChanged));

        public GpuInfo? SelectedGpu
        {
            get => (GpuInfo?)GetValue(SelectedGpuProperty);
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
            selector.viewModel.Gpus = new ObservableCollection<GpuInfo>(gpuList);
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
            var gpuList = selector.viewModel.Gpus;
            if (gpuList == null) return;
            if (gpuList.Count > 0)
            {
                if (!gpuList.Contains(selector.SelectedGpu))
                {
                    selector.SelectedGpu = gpuList.First(g => g.DeviceId == selector.SelectedGpu.DeviceId);
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
            SelectedGpu ??= lastSelectedGpu ?? viewModel.Gpus.First();
        }

        private void OnUnchecked(object sender, RoutedEventArgs e)
        {
            SelectedGpu = null;
        }
    }

    internal class ViewModel: INotifyPropertyChanged
    {
        private ObservableCollection<GpuInfo> _gpus;
        public ObservableCollection<GpuInfo> Gpus
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

    public enum GpuVendor
    {
        None,
        Nvidia,
        Amd,
        Intel
    }

    public class GpuInfo(string name, int deviceId, GpuVendor vendor)
    {
        public string Name { get; set; } = name;
        public int DeviceId { get; set; } = deviceId;
        public GpuVendor Vendor { get; set; } = vendor;

        public override string ToString() => Name;

        public static string InputParams(GpuInfo? gpuInfo, string input)
        {
            return gpuInfo?.Vendor switch
            {
                GpuVendor.Nvidia => $"-hwaccel cuda -hwaccel_output_format cuda -hwaccel_device {gpuInfo.DeviceId} -i \"{input}\"",
                GpuVendor.Amd => $"-hwaccel d3d11va -hwaccel_output_format d3d11 -hwaccel_device {gpuInfo.DeviceId} -i \"{input}\"",
                GpuVendor.Intel => $"-hwaccel qsv -hwaccel_output_format qsv -hwaccel_device {gpuInfo.DeviceId} -i \"{input}\"",
                _ => $"-i \"{input}\""
            };
        }

        public static string QualityParams(GpuInfo? gpuInfo, int quality)
        {
            return gpuInfo?.Vendor switch
            {
                GpuVendor.Nvidia => $"-rc vbr -b:v 0 -cq {quality}",
                GpuVendor.Amd => $"-qp_i {quality} -qp_p {quality} -qp_b {quality}",
                GpuVendor.Intel => $"-rc icq -global_quality {quality}",
                _ => $"-crf {quality}"
            };
        }

        public static string PresetParams(GpuInfo? gpuInfo, int presetIndex)
        {
            var presets = Presets[gpuInfo?.Vendor ?? GpuVendor.None];
            if (presetIndex < 0 || presetIndex >= presets.Length)
                throw new ArgumentOutOfRangeException(
                    $"Preset level {presetIndex} does not exist for {gpuInfo?.Vendor}");
            return $"-{(gpuInfo?.Vendor == GpuVendor.Amd ? "quality" : "preset")} {presets[presetIndex]}";
        }

        public static readonly Dictionary<GpuVendor, string> EncodingParamsDict = new()
        {
            { GpuVendor.None, "libx265" },
            { GpuVendor.Nvidia, "hevc_nvenc" },
            { GpuVendor.Amd, "hevc_amf" },
            { GpuVendor.Intel, "hevc_qsv" }
        };

        public static readonly Dictionary<GpuVendor, string[]> Presets = new()
        {
            { GpuVendor.None, ["ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow", "placebo"] },
            //{ GPUVendor.Nvidia, ["p1", "p2", "p3", "p4", "p5", "p6", "p7"] },
            { GpuVendor.Nvidia, ["default", "slow", "medium", "fast", "hp", "hq", "bd", "ll", "llhq", "llhp", "lossless", "losslesshp"] },
            { GpuVendor.Amd, ["speed", "balanced", "quality"] },
            { GpuVendor.Intel, ["veryfast", "fast", "medium", "slow", "veryslow"] }
        };
    }
}