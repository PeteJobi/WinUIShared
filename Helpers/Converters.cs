using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;
using System.Collections.Generic;
using System.Text;

namespace WinUIShared.Helpers
{
    public class EnumToVisibilityConverter<T> : IValueConverter
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
}
