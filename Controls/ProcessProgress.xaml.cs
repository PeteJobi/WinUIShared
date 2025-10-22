using CommunityToolkit.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Xml.Linq;
using Windows.Foundation;
using Windows.Foundation.Collections;
using WinUIShared.Enums;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WinUIShared.Controls
{
    public sealed partial class ProcessProgress : UserControl, INotifyPropertyChanged
    {
        public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
            nameof(State),
            typeof(OperationState),
            typeof(ProcessProgress),
            new PropertyMetadata(OperationState.BeforeOperation, OnStateChanged));
        public OperationState State
        {
            get => (OperationState)GetValue(StateProperty);
            set => SetValue(StateProperty, value);
        }
        public event EventHandler<OperationState> StateChanged;
        private bool BeforeOperation => State == OperationState.BeforeOperation;
        private bool DuringOperation => State == OperationState.DuringOperation;
        private bool AfterOperation => State == OperationState.AfterOperation;

        private bool paused;
        private bool Paused
        {
            get => paused;
            set => SetField(ref paused, value);
        }

        public static readonly DependencyProperty OnlyPrimaryProperty = DependencyProperty.Register(
            nameof(OnlyPrimary),
            typeof(bool),
            typeof(ProcessProgress),
            new PropertyMetadata(false));

        public bool OnlyPrimary
        {
            get => (bool)GetValue(OnlyPrimaryProperty);
            set => SetValue(OnlyPrimaryProperty, value);
        }

        public static readonly DependencyProperty LeftTextPrimaryProperty = DependencyProperty.Register(
            nameof(LeftTextPrimary),
            typeof(string),
            typeof(ProcessProgress),
            new PropertyMetadata(string.Empty));

        public string LeftTextPrimary
        {
            get => (string)GetValue(LeftTextPrimaryProperty);
            set => SetValue(LeftTextPrimaryProperty, value);
        }

        public static readonly DependencyProperty CenterTextPrimaryProperty = DependencyProperty.Register(
            nameof(CenterTextPrimary),
            typeof(string),
            typeof(ProcessProgress),
            new PropertyMetadata(string.Empty));

        public string CenterTextPrimary
        {
            get => (string)GetValue(CenterTextPrimaryProperty);
            set => SetValue(CenterTextPrimaryProperty, value);
        }

        public static readonly DependencyProperty RightTextPrimaryProperty = DependencyProperty.Register(
            nameof(RightTextPrimary),
            typeof(string),
            typeof(ProcessProgress),
            new PropertyMetadata(string.Empty));

        public string RightTextPrimary
        {
            get => (string)GetValue(RightTextPrimaryProperty);
            set => SetValue(RightTextPrimaryProperty, value);
        }

        public static readonly DependencyProperty ProgressPrimaryProperty = DependencyProperty.Register(
            nameof(ProgressPrimary),
            typeof(double),
            typeof(ProcessProgress),
            new PropertyMetadata(0d));

        public double ProgressPrimary
        {
            get => (double)GetValue(ProgressPrimaryProperty);
            set => SetValue(ProgressPrimaryProperty, value);
        }

        public static readonly DependencyProperty LeftTextSecondaryProperty = DependencyProperty.Register(
            nameof(LeftTextSecondary),
            typeof(string),
            typeof(ProcessProgress),
            new PropertyMetadata(string.Empty));

        public string LeftTextSecondary
        {
            get => (string)GetValue(LeftTextSecondaryProperty);
            set => SetValue(LeftTextSecondaryProperty, value);
        }

        public static readonly DependencyProperty CenterTextSecondaryProperty = DependencyProperty.Register(
            nameof(CenterTextSecondary),
            typeof(string),
            typeof(ProcessProgress),
            new PropertyMetadata(string.Empty));

        public string CenterTextSecondary
        {
            get => (string)GetValue(CenterTextSecondaryProperty);
            set => SetValue(CenterTextSecondaryProperty, value);
        }

        public static readonly DependencyProperty RightTextSecondaryProperty = DependencyProperty.Register(
            nameof(RightTextSecondary),
            typeof(string),
            typeof(ProcessProgress),
            new PropertyMetadata(string.Empty));

        public string RightTextSecondary
        {
            get => (string)GetValue(RightTextSecondaryProperty);
            set => SetValue(RightTextSecondaryProperty, value);
        }

        public static readonly DependencyProperty ProgressSecondaryProperty = DependencyProperty.Register(
            nameof(ProgressSecondary),
            typeof(double),
            typeof(ProcessProgress),
            new PropertyMetadata(0d));


        public double ProgressSecondary
        {
            get => (double)GetValue(ProgressSecondaryProperty);
            set => SetValue(ProgressSecondaryProperty, value);
        }

        public event EventHandler ViewRequested;
        public event EventHandler PauseRequested;
        public event EventHandler ResumeRequested;
        public event EventHandler CancelRequested;
        public event EventHandler CloseRequested;

        public ProcessProgress()
        {
            InitializeComponent();
        }

        private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var pp = (ProcessProgress)d;
            pp.OnPropertyChanged(nameof(BeforeOperation));
            pp.OnPropertyChanged(nameof(DuringOperation));
            pp.OnPropertyChanged(nameof(AfterOperation));
            pp.StateChanged?.Invoke(pp, pp.State);
        }

        private void PauseOrViewTour_OnClick(object sender, RoutedEventArgs e)
        {
            if (State == OperationState.AfterOperation)
            {
                ViewRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (Paused)
            {
                ResumeRequested?.Invoke(this, EventArgs.Empty);
                Paused = false;
            }
            else
            {
                PauseRequested?.Invoke(this, EventArgs.Empty);
                Paused = true;
            }
        }

        private void CancelOrClose_OnClick(object sender, RoutedEventArgs e)
        {
            if (State == OperationState.AfterOperation)
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            FlyoutBase.ShowAttachedFlyout((FrameworkElement)sender);
        }

        private void CancelProcess(object sender, RoutedEventArgs e)
        {
            CancelRequested?.Invoke(this, EventArgs.Empty);
            Paused = false;

            var button = (Button)sender;
            var container = button.Parent;
            while (container != null && container is not FlyoutPresenter)
            {
                container = VisualTreeHelper.GetParent(container);
            }
            var flyoutPresenter = (FlyoutPresenter)container;
            var popup = flyoutPresenter.Parent as Popup;
            popup.IsOpen = false;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    public class TimeSpanToTextConverter : IValueConverter
    {
        public bool DontShowFractionalSeconds { get; set; }

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is TimeSpan timeSpan)
            {
                return TimespanToTextFormat(timeSpan, DontShowFractionalSeconds);
            }

            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is string str && TimeSpan.TryParse(str, out var timeSpan))
            {
                return timeSpan;
            }

            return TimeSpan.Zero;
        }

        public static string TimespanToTextFormat(TimeSpan timeSpan, bool dontShowFractionalSeconds = false)
        {
            var format = dontShowFractionalSeconds ? @"hh\:mm\:ss" : @"hh\:mm\:ss\.fff";
            return timeSpan.ToString(format);
        }
    }
}
