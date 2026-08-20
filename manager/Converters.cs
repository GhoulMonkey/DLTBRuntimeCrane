// SPDX-License-Identifier: GPL-3.0-only
// Two tiny converters the row template needs.
//
// A row binds a brush by name rather than by value, so the state vocabulary
// lives in App.xaml with the rest of the palette instead of being constructed in
// code. "What colour is a failure" then stays answerable in one file.

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace CraneManager
{
    public class BrushLookupConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string key = value as string;
            if (string.IsNullOrEmpty(key)) return Brushes.Transparent;
            object found = Application.Current != null ? Application.Current.TryFindResource(key) : null;
            return found ?? Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public class ShowIfConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool show = value is bool && (bool)value;
            return show ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
