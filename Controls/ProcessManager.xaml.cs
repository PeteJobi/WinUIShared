using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using WinUIShared.Enums;
using WinUIShared.Helpers;

namespace WinUIShared.Controls
{
    public sealed partial class ProcessManager : UserControl
    {
        private IntPtr _hwnd;
        private bool _displayingTaskbarProgress;
        private bool processFailed;
        private string processFailedMessage;

        public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
            nameof(State),
            typeof(OperationState),
            typeof(ProcessManager),
            new PropertyMetadata(OperationState.BeforeOperation, OnStateChanged));
        public OperationState State
        {
            get => (OperationState)GetValue(StateProperty);
            set { if (State != value) SetValue(StateProperty, value); }
        }
        public event EventHandler<OperationState> StateChanged;

        public static readonly DependencyProperty ProcessorProperty = DependencyProperty.Register(
            nameof(Processor),
            typeof(Processor),
            typeof(ProcessManager),
            new PropertyMetadata(null, OnProcessorChanged));

        public Processor Processor
        {
            get => (Processor)GetValue(ProcessorProperty);
            set => SetValue(ProcessorProperty, value);
        }

        public static readonly DependencyProperty HardwareSelectorProperty = DependencyProperty.Register(
            nameof(HardwareSelector),
            typeof(HardwareSelector),
            typeof(ProcessManager),
            new PropertyMetadata(null, OnHardwareSelectorChanged));

        public HardwareSelector HardwareSelector
        {
            get => (HardwareSelector)GetValue(HardwareSelectorProperty);
            set => SetValue(HardwareSelectorProperty, value);
        }

        public static readonly DependencyProperty OnlyPrimaryProperty = DependencyProperty.Register(
            nameof(OnlyPrimary),
            typeof(bool),
            typeof(ProcessManager),
            new PropertyMetadata(false));

        public bool OnlyPrimary
        {
            get => (bool)GetValue(OnlyPrimaryProperty);
            set => SetValue(OnlyPrimaryProperty, value);
        }

        public ProcessManager()
        {
            InitializeComponent();
            Loaded += ProcessManager_Loaded;
        }

        private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var manager = (ProcessManager)d;
            manager.StateChanged?.Invoke(manager, manager.State);
        }

        private static void OnProcessorChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
        {
            var manager = (ProcessManager)dependencyObject;
            if (manager.Processor == null) return;
            manager.Processor.SetProgressAndErrorReporters(
                new Progress<string>(s => manager.ProcessProgress.LeftTextPrimary = s),
                new Progress<string>(s => manager.ProcessProgress.CenterTextPrimary = s),
                new Progress<string>(s => manager.ProcessProgress.RightTextPrimary = s),
                new Progress<double>(PrimaryProgress),
                new Progress<string>(s => manager.ProcessProgress.LeftTextSecondary = s),
                new Progress<string>(s => manager.ProcessProgress.CenterTextSecondary = s),
                new Progress<string>(s => manager.ProcessProgress.RightTextSecondary = s),
                new Progress<double>(d => manager.ProcessProgress.ProgressSecondary = d),
                s =>
                {
                    manager.processFailed = true;
                    manager.processFailedMessage = s;
                }
            );
            return;

            void PrimaryProgress(double value)
            {
                manager.ProcessProgress.ProgressPrimary = value;
                if(manager._displayingTaskbarProgress)
                    TaskbarProgress.SetValue(manager._hwnd, (ulong)value, (ulong)Processor.ProgressMax);
            }
        }

        private static void OnHardwareSelectorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var manager = (ProcessManager)d;
            var selector = manager.HardwareSelector;
            if (selector == null) return;

            selector.RegisterPropertyChangedCallback(HardwareSelector.SelectedGpuProperty, (sender, dp) =>
            {
                if(selector.SelectedGpu != null) manager.Processor.EnableHardwareAccelParams(selector.SelectedGpu);
                else manager.Processor.DisableHardwareAccel();
            });
        }

        private void ProcessProgress_OnPauseRequested(object? sender, EventArgs e)
        {
            Processor.Pause();
            SetTaskbarProgressState(TaskbarProgress.TaskbarStates.Paused);
        }

        private void ProcessProgress_OnResumeRequested(object? sender, EventArgs e)
        {
            Processor.Resume();
            SetTaskbarProgressState(TaskbarProgress.TaskbarStates.Normal);
        }

        private void ProcessProgress_OnViewRequested(object? sender, EventArgs e)
        {
            Processor.ViewFile();
        }

        private async void ProcessProgress_OnCancelRequested(object? sender, EventArgs e)
        {
            await Processor.Cancel();
            State = OperationState.BeforeOperation;
            ResetTaskbarProgressState();
        }

        private readonly CancellationTokenSource _loadTokenSource = new();
        private void ProcessManager_Loaded(object sender, RoutedEventArgs e)
        {
            var windowId = XamlRoot?.ContentIslandEnvironment?.AppWindowId;
            _hwnd = Win32Interop.GetWindowFromWindowId(windowId.Value);
            _loadTokenSource.Cancel();
        }

        public async Task WaitToLoad()
        {
            if(_loadTokenSource.IsCancellationRequested) return;
            try
            {
                await Task.Delay(-1, _loadTokenSource.Token);
            }
            catch (TaskCanceledException)
            {
            }
        }

        public async Task<string?> StartProcess(Func<Task> processTask)
        {
            SetupProcess();

            var taskbarSetupTokenSource = new CancellationTokenSource();
            _ = SetupTaskbarProgress(taskbarSetupTokenSource.Token);
            try
            {
                await processTask();
                await taskbarSetupTokenSource.CancelAsync();

                if (State == OperationState.BeforeOperation) return null; //Canceled
                if (processFailed)
                {
                    State = OperationState.BeforeOperation;
                    SetTaskbarProgressState(TaskbarProgress.TaskbarStates.Error);
                    ShowNotification(true, processFailedMessage);
                    await ErrorAction(processFailedMessage);
                    ResetTaskbarProgressState();
                    await Processor.Cancel();
                    return null;
                }

                State = OperationState.AfterOperation;
                ProcessProgress.RightTextPrimary = "Done";
                ShowNotification(false);
                ResetTaskbarProgressState();
                return Processor.GetFile();
            }
            catch (Exception ex)
            {
                SetTaskbarProgressState(TaskbarProgress.TaskbarStates.Error);
                ShowNotification(true, ex.Message);
                await ErrorAction(ex.Message);
                ResetTaskbarProgressState();
                State = OperationState.BeforeOperation;
                return null;
            }

            void ShowNotification(bool isError, string? errorMessage = null)
            {
                if (!_displayingTaskbarProgress) return;
                var notification = new AppNotificationBuilder()
                    .AddText(isError ? "Process failed" : "Process completed")
                    .AddText(isError ? errorMessage : null)
                    .BuildNotification();

                AppNotificationManager.Default.Show(notification);
            }
        }

        public async Task<bool> StartSimpleProcess(Func<Task> processTask)
        {
            processFailed = false;
            await WaitToLoad();
            await processTask();

            if (!processFailed) return true;
            await ErrorAction(processFailedMessage);
            return false;

        }

        public async Task<T> StartSimpleProcess<T>(Func<Task<T>> processTask)
        {
            processFailed = false;
            await WaitToLoad();
            var result = await processTask();

            if (processFailed)
            {
                await ErrorAction(processFailedMessage);
            }

            return result;
        }

        public async Task<T?> StartSimpleProcessOrReturnDefault<T>(Func<Task<T>> processTask)
        {
            processFailed = false;
            await WaitToLoad();
            var result = await processTask();

            if (!processFailed) return result;
            await ErrorAction(processFailedMessage);
            return default;
        }

        private async Task SetupTaskbarProgress(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(5000, cancellationToken);
                if (ProcessProgress.ProgressPrimary < 0.1 * Processor.ProgressMax) //If after 5 seconds, the progress is less than 10%, we show the taskbar progress
                {
                    TaskbarProgress.SetState(_hwnd, TaskbarProgress.TaskbarStates.Normal);
                    _displayingTaskbarProgress = true;
                }
            }
            catch (TaskCanceledException)
            {
            }
        }

        private void SetTaskbarProgressState(TaskbarProgress.TaskbarStates state)
        {
            if(!_displayingTaskbarProgress) return;
            TaskbarProgress.SetState(_hwnd, state);
        }

        private void ResetTaskbarProgressState()
        {
            SetTaskbarProgressState(TaskbarProgress.TaskbarStates.NoProgress);
            _displayingTaskbarProgress = false;
        }

        public async Task ShowDialog(string title, string content)
        {
            await WaitToLoad();
            ErrorDialog.Title = title;
            ErrorDialog.Content = content;
            await ErrorDialog.ShowAsync();
        }

        private Task ErrorAction(string message) => ShowDialog("Operation failed", message);

        private void SetupProcess()
        {
            State = OperationState.DuringOperation;
            processFailed = false;
            ProcessProgress.LeftTextPrimary = string.Empty;
            ProcessProgress.CenterTextPrimary = string.Empty;
            ProcessProgress.RightTextPrimary = string.Empty;
            ProcessProgress.ProgressPrimary = 0;
            ProcessProgress.LeftTextSecondary = string.Empty;
            ProcessProgress.CenterTextSecondary = string.Empty;
            ProcessProgress.RightTextSecondary = string.Empty;
            ProcessProgress.ProgressSecondary = 0;
        }
    }

    public static class TaskbarProgress //Class generated by Gemini 3 Auto
    {
        public enum TaskbarStates
        {
            NoProgress = 0,
            Indeterminate = 0x1,
            Normal = 0x2,
            Error = 0x4,
            Paused = 0x8
        }

        [ComImport]
        [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITaskbarList3
        {
            // ITaskbarList
            void HrInit();
            void AddTab(IntPtr hwnd);
            void DeleteTab(IntPtr hwnd);
            void ActivateTab(IntPtr hwnd);
            void SetActiveAlt(IntPtr hwnd);

            // ITaskbarList2
            void MarkFullscreenWindow(IntPtr hwnd, int fFullscreen);

            // ITaskbarList3
            void SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
            void SetProgressState(IntPtr hwnd, TaskbarStates tbpFlags);
            //void RegisterTab(IntPtr hwndTab, IntPtr hwndMDI);
            //void UnregisterTab(IntPtr hwndTab);
            //void SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);
            //void SetTabActive(IntPtr hwndTab, IntPtr hwndMDI, void* tbatFlags);
            //void SetThumbnailClip(IntPtr hwnd, IntPtr prcClip);
            //void SetThumbnailTooltip(IntPtr hwnd, string pszTip);
            //void SetThumbnailButtons(IntPtr hwnd, int cButtons, IntPtr pButtons);
            //void SetThumbIcon(IntPtr hwnd, IntPtr hIcon, string pszTip);
        }

        [ComImport]
        [Guid("56fdf344-fd6d-11d0-958a-006097c9a090")]
        private class TaskbarInstance { }

        private static ITaskbarList3? _taskbarList;

        private static ITaskbarList3 GetTaskbarList()
        {
            if (_taskbarList == null)
            {
                _taskbarList = (ITaskbarList3)new TaskbarInstance();
                _taskbarList.HrInit();
            }
            return _taskbarList;
        }

        public static void SetState(IntPtr hwnd, TaskbarStates state)
        {
            try
            {
                GetTaskbarList().SetProgressState(hwnd, state);
            }
            catch { }
        }

        public static void SetValue(IntPtr hwnd, ulong current, ulong total)
        {
            try
            {
                GetTaskbarList().SetProgressValue(hwnd, current, total);
            }
            catch { }
        }
    }
}