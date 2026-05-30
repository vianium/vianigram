// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// ImagePathToImageSourceConverter.cs
//
// Maps a local app-data path, file path, or URI string to a BitmapImage.
// XAML's built-in string -> ImageSource conversion is inconsistent for
// absolute LocalState paths on WP 8.1, so chat preview cards go through this
// converter before assigning Image.Source.

using System;
using System.Text;
using Windows.Storage;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media.Imaging;

namespace Vianigram.App.Converters
{
    public sealed class ImagePathToImageSourceConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string source = value as string;
            if (string.IsNullOrWhiteSpace(source)) return null;

            Uri uri = CreateUri(source);
            if (uri == null) return null;

            try { return new BitmapImage(uri); }
            catch { return null; }
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return string.Empty;
        }

        private static Uri CreateUri(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return null;

            Uri uri;
            if (IsFileSystemPath(source))
            {
                string appDataUri = TryMakeAppDataLocalUri(source);
                if (!string.IsNullOrEmpty(appDataUri) &&
                    Uri.TryCreate(appDataUri, UriKind.Absolute, out uri))
                    return uri;

                if (Uri.TryCreate("file:///" + source.Replace('\\', '/'), UriKind.Absolute, out uri))
                    return uri;
            }

            if (Uri.TryCreate(source, UriKind.Absolute, out uri)) return uri;
            if (Uri.TryCreate(source, UriKind.RelativeOrAbsolute, out uri)) return uri;
            return null;
        }

        private static bool IsFileSystemPath(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return false;
            if (source.Length <= 2 || source[1] != ':') return false;
            return !source.StartsWith("ms-appdata:", StringComparison.OrdinalIgnoreCase)
                && !source.StartsWith("ms-appx:", StringComparison.OrdinalIgnoreCase);
        }

        private static string TryMakeAppDataLocalUri(string source)
        {
            try
            {
                string localRoot = ApplicationData.Current.LocalFolder.Path;
                if (string.IsNullOrEmpty(localRoot)) return null;
                if (!source.StartsWith(localRoot, StringComparison.OrdinalIgnoreCase)) return null;

                string relative = source.Substring(localRoot.Length).TrimStart('\\', '/');
                if (string.IsNullOrEmpty(relative)) return null;
                return "ms-appdata:///local/" + EscapePath(relative.Replace('\\', '/'));
            }
            catch
            {
                return null;
            }
        }

        private static string EscapePath(string relativePath)
        {
            string[] parts = relativePath.Split('/');
            var sb = new StringBuilder(relativePath.Length + 16);
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) sb.Append('/');
                sb.Append(Uri.EscapeDataString(parts[i]));
            }
            return sb.ToString();
        }
    }
}
