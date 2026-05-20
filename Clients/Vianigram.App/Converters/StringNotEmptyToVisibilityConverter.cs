// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// StringNotEmptyToVisibilityConverter.cs
//
// Maps a string to Visibility: non-empty (and non-null) string => Visible,
// empty/null => Collapsed. Used by message-bubble templates to hide
// caption / address / description rows when the field is unset, without
// each binding needing a custom converter.

using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;

namespace Vianigram.App.Converters
{
    public sealed class StringNotEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string s = value as string;
            bool visible = !string.IsNullOrEmpty(s);
            string mode = parameter as string;
            if (!string.IsNullOrEmpty(mode) &&
                string.Equals(mode, "Invert", StringComparison.OrdinalIgnoreCase))
                visible = !visible;
            return visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            // Not used.
            return string.Empty;
        }
    }
}
