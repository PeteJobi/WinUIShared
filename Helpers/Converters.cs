using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;
using System.Collections.Generic;
using System.Text;

namespace WinUIShared.Helpers
{
    public class EnumToVisibilityConverter<T> : IValueConverter //Can't be used because of XAML limitations
    {
        public T Enum { get; set; }

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is not T enumValue) return Visibility.Collapsed;
            return enumValue.Equals(Enum) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public class EnumToVisibilityConverter : IValueConverter
    {
        public Type EnumType { get; set; }
        public string EnumForVisibility { get; set; }

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var enumValue = Enum.Parse(EnumType, EnumForVisibility);
            return value.Equals(enumValue) == !(bool.TryParse(parameter.ToString(), out var invert) && invert) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToBindableStringConverter : IValueConverter
    {
        public string FalseString { get; set; } = string.Empty;
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool boolValue)
            {
                return boolValue ? parameter : FalseString;
            }
            return FalseString;
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public class DoubleToVisibilityConverter : IValueConverter
    {
        public object ZeroValue { get; set; } = Visibility.Collapsed;
        public object NotZeroValue { get; set; } = Visibility.Visible;

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is double doubleValue)
            {
                return doubleValue > 0 ? NotZeroValue : ZeroValue;
            }
            return ZeroValue;
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
