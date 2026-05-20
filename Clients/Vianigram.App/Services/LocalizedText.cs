// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// LocalizedText.cs — Vianigram.App.Services
//
// Translation layer between the language-agnostic descriptions emitted
// by Vianigram.Sync.TlDecoder
// (which lives in a context that can't depend on Resources.resw) and
// the localized strings the UI shows in toasts, tile content, dialog
// previews, and the chat list.
//
// Wire format:
//   "~Key|Arg1|Arg2"   — translatable, looked up via Strings.Get +
//                        string.Format with the args.
//   "~Key"             — no args, plain Strings.Get.
//   "anything else"    — literal pass-through (already localized or
//                        verbatim message text like CustomAction body).
//
// The sentinel "~" was chosen because it never appears at the start
// of natural-language text written by users; even a Telegram message
// that begins with "~" is still safe because we only treat "~Key|"
// or "~Key$" patterns where Key matches `[A-Za-z][A-Za-z0-9.]+` —
// see IsLikelyKeyedFormat below.
//
// Args are passed as strings. Numeric formatting is done at the
// emitter side so the resw template can stay locale-aware
// ("scored {0}" works for both English digits and any future RTL
// flips).

using System;
using System.Globalization;

namespace Vianigram.App.Services
{
    public static class LocalizedText
    {
        /// <summary>
        /// Resolve a possibly-keyed body string into the user-facing
        /// localized form. Idempotent for non-keyed input.
        /// </summary>
        public static string Resolve(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            if (!IsLikelyKeyedFormat(raw)) return raw;

            // Drop the sentinel and split.
            string content = raw.Substring(1);
            int firstPipe = content.IndexOf('|');
            string key;
            string[] args;
            if (firstPipe < 0)
            {
                key = content;
                args = null;
            }
            else
            {
                key = content.Substring(0, firstPipe);
                string tail = content.Substring(firstPipe + 1);
                args = tail.Split('|');
            }

            string template = Strings.Get(key);
            if (string.IsNullOrEmpty(template)) return key;
            if (args == null || args.Length == 0) return template;

            try
            {
                object[] boxed = new object[args.Length];
                for (int i = 0; i < args.Length; i++) boxed[i] = args[i];
                return string.Format(CultureInfo.CurrentCulture, template, boxed);
            }
            catch
            {
                // Bad format string in the resw — show the raw template
                // rather than crash. The translator can fix the resw
                // entry without breaking the app.
                return template;
            }
        }

        /// <summary>
        /// Build a keyed-format string from a key + args. Inverse of
        /// <see cref="Resolve"/>. Use this in cross-context layers
        /// (Sync TlDecoder) that need to emit a localizable description
        /// without taking a dependency on the App's resource catalog.
        /// </summary>
        public static string Compose(string key, params string[] args)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            if (args == null || args.Length == 0) return "~" + key;
            // Reject pipe inside args — would break the wire format.
            // Replace with a Unicode-similar broken-bar so the message
            // still surfaces something readable.
            var sb = new System.Text.StringBuilder(64);
            sb.Append('~').Append(key);
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i] ?? string.Empty;
                if (a.IndexOf('|') >= 0) a = a.Replace('|', '¦');
                sb.Append('|').Append(a);
            }
            return sb.ToString();
        }

        private static bool IsLikelyKeyedFormat(string raw)
        {
            // Sentinel '~' followed by a Latin letter and at least one
            // more identifier-like character. Free-form text starting
            // with '~' (rare) is preserved verbatim.
            if (raw.Length < 3) return false;
            if (raw[0] != '~') return false;
            char c1 = raw[1];
            if (!IsAsciiLetter(c1)) return false;
            // Walk forward — first char of key must be letter, then
            // letter/digit/dot until '|' or end.
            for (int i = 2; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c == '|') return true; // valid key + args
                if (IsAsciiLetter(c) || (c >= '0' && c <= '9') || c == '.' || c == '_') continue;
                return false;
            }
            // No pipe and no invalid char — it's a bare ~Key.
            return true;
        }

        private static bool IsAsciiLetter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }
    }
}
