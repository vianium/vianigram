// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// BoolToVisibilityConverter.cs
//
// Maps a bool to Visibility (true => Visible, false => Collapsed) for
// XAML one-way bindings. Pass parameter="invert" or "Negate" to flip
// the mapping (true => Collapsed). WP8.1 / Universal Windows does not
// ship a built-in BooleanToVisibilityConverter, so the App owns this
// minimal one.

using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;

namespace Vianigram.App.Converters
{
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool b = false;
            if (value is bool) b = (bool)value;
            else if (value != null)
            {
                bool parsed;
                if (bool.TryParse(value.ToString(), out parsed)) b = parsed;
            }

            if (parameter != null)
            {
                string p = parameter.ToString();
                if (string.Equals(p, "invert", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p, "Negate", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p, "!", StringComparison.OrdinalIgnoreCase))
                {
                    b = !b;
                }
            }

            return b ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is Visibility) return (Visibility)value == Visibility.Visible;
            return false;
        }
    }
}
