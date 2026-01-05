using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Data;
using WinUIShared.Enums;
using WinUIShared.Helpers;

namespace WinUIShared.Controls
{
    public sealed partial class ProcessManager : UserControl
    {
        public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
            nameof(State),
            typeof(OperationState),
            typeof(ProcessProgress),
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
            typeof(ProcessProgress),
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

        private bool processFailed;
        private string processFailedMessage;

        public ProcessManager()
        {
            InitializeComponent();
        }

        private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var manager = (ProcessManager)d;
            manager.StateChanged?.Invoke(manager, manager.State);
        }

        private static void OnProcessorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var manager = (ProcessManager)d;
            if (manager.Processor == null) return;
            manager.Processor.SetProgressAndErrorReporters(
                new Progress<string>(s => manager.ProcessProgress.LeftTextPrimary = s),
                new Progress<string>(s => manager.ProcessProgress.CenterTextPrimary = s),
                new Progress<string>(s => manager.ProcessProgress.RightTextPrimary = s),
                new Progress<double>(d => manager.ProcessProgress.ProgressPrimary = d),
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
        }

        private void ProcessProgress_OnResumeRequested(object? sender, EventArgs e)
        {
            Processor.Resume();
        }

        private void ProcessProgress_OnViewRequested(object? sender, EventArgs e)
        {
            Processor.ViewFile();
        }

        private async void ProcessProgress_OnCancelRequested(object? sender, EventArgs e)
        {
            await Processor.Cancel();
            State = OperationState.BeforeOperation;
        }

        public async Task<string?> StartProcess(Task processTask)
        {
            return (await StartProcess(NonGenericToGeneric())).Item1;

            async Task<object?> NonGenericToGeneric()
            {
                await processTask;
                return null;
            }
        }

        public async Task<(string?, T?)> StartProcess<T>(Task<T> processTask)
        {
            SetupProcess();

            try
            {
                var result = await processTask;

                if (State == OperationState.BeforeOperation) return (null, default); //Canceled
                if (processFailed)
                {
                    State = OperationState.BeforeOperation;
                    await ErrorAction(processFailedMessage);
                    await Processor.Cancel();
                    return (null, default);
                }

                State = OperationState.AfterOperation;
                ProcessProgress.RightTextPrimary = "Done";
                return (Processor.GetFile(), result);
            }
            catch (Exception ex)
            {
                await ErrorAction(ex.Message);
                State = OperationState.BeforeOperation;
                return (null, default);
            }

            async Task ErrorAction(string message)
            {
                ErrorDialog.Title = "Operation failed";
                ErrorDialog.Content = message;
                await ErrorDialog.ShowAsync();
            }
        }

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
}