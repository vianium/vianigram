// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// KeyFingerprintPageViewModel.cs
//
// Backs KeyFingerprintPage. OnNavigatedTo projects the supplied
// SecretChatId via ISecretChatsApi.GetEmojiKey onto the 4 emoji rows +
// hex fingerprint. Compare is reserved (camera-QR cross-check), CopyHex
// is VM-local clipboard work delegated to the page.

using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Vianigram.App.Navigation;
using Vianigram.App.Services;
using Vianigram.App.ViewModels;
using Vianigram.SecretChats.Domain.ValueObjects;
using Vianigram.SecretChats.Ports.Inbound;

namespace Vianigram.App.ViewModels.Pages
{
    public sealed class KeyFingerprintPageViewModel : BaseViewModel
    {
        // Telegram's recognisable 333-emoji palette is huge; for the
        // pre-wire VM we use a small representative palette so the page
        // shows something sensible against design-time data.
        private static readonly string[] EmojiPalette = new string[]
        {
            "\U0001F600", // grinning face
            "\U0001F60A", // smiling face
            "\U0001F914", // thinking face
            "\U0001F44D", // thumbs up
            "\U0001F389", // party popper
            "\U0001F4AB", // dizzy
            "\U0001F525", // fire
            "\U0001F496", // sparkling heart
            "\U0001F31F", // glowing star
            "\U0001F308", // rainbow
            "\U0001F340", // four-leaf clover
            "\U0001F353", // strawberry
            "\U0001F30D", // earth globe
            "\U0001F319", // crescent moon
            "\U0001F4A1", // light bulb
            "\U0001F511"  // key
        };

        private readonly ISecretChatsApi _secret;
        private readonly INavigationService _nav;

        private SecretChatId _chatId;
        private string _contactName;
        private string _avatarLetter;
        private string _hexFingerprint;
        private string _statusMessage;

        public KeyFingerprintPageViewModel() : this(null, null)
        {
        }

        public KeyFingerprintPageViewModel(ISecretChatsApi secret, INavigationService nav)
        {
            _secret = secret;
            _nav = nav;

            _contactName = string.Empty;
            _avatarLetter = "?";
            _hexFingerprint = string.Empty;
            _statusMessage = string.Empty;

            EmojiRows = new ObservableCollection<string>();
            for (int i = 0; i < 4; i++) EmojiRows.Add(string.Empty);

            CompareCommand = new RelayCommand(_ => RaiseCompare(), _ => true);
            CopyHexCommand = new RelayCommand(_ => RaiseCopyHex(), _ => !string.IsNullOrEmpty(_hexFingerprint));
        }

        // ---- Bindable surface ---------------------------------------

        public ObservableCollection<string> EmojiRows { get; private set; }

        public string ContactName
        {
            get { return _contactName; }
            set { SetProperty(ref _contactName, value); }
        }

        public string AvatarLetter
        {
            get { return _avatarLetter; }
            set { SetProperty(ref _avatarLetter, value); }
        }

        public string HexFingerprint
        {
            get { return _hexFingerprint; }
            set
            {
                if (SetProperty(ref _hexFingerprint, value))
                {
                    var rc = CopyHexCommand as RelayCommand;
                    if (rc != null) rc.RaiseCanExecuteChanged();
                }
            }
        }

        public string StatusMessage
        {
            get { return _statusMessage; }
            private set
            {
                if (SetProperty(ref _statusMessage, value))
                    OnPropertyChanged("HasStatus");
            }
        }

        public bool HasStatus
        {
            get { return !string.IsNullOrEmpty(_statusMessage); }
        }

        // ---- Commands -------------------------------------------------

        public ICommand CompareCommand { get; private set; }
        public ICommand CopyHexCommand { get; private set; }

        // ---- Page-supplied callbacks --------------------------------

        // Compare-in-person and clipboard copy are not bounded-context
        // ports; the page wires them into native shims (camera/QR scan
        // and Windows.ApplicationModel.DataTransfer.Clipboard).
        public Action CompareRequested { get; set; }
        public Action<string> CopyHexRequested { get; set; }

        // ---- Lifecycle -----------------------------------------------

        public void OnNavigatedTo(object parameter)
        {
            if (parameter is SecretChatId)
            {
                _chatId = (SecretChatId)parameter;
            }
            else if (parameter is int)
            {
                _chatId = new SecretChatId((int)parameter);
            }
            else if (parameter is byte[])
            {
                // Backwards-compat: design-time / smoke harness can hand
                // raw key bytes directly.
                SetFingerprint((byte[])parameter);
                return;
            }

            if (_secret == null) return;

            try
            {
                EmojiKey key = _secret.GetEmojiKey(_chatId);
                if (key != null)
                {
                    ApplyEmojiKey(key);
                }
            }
            catch (Exception ex)
            {
                AppLog.For("App.KeyFingerprintPage").Error("GetEmojiKey threw: " + ex);
            }
        }

        public void OnNavigatedFrom(object parameter)
        {
        }

        // ---- Public projection helpers -------------------------------

        /// <summary>
        /// Projects the supplied key bytes onto the emoji rows + hex
        /// fingerprint shape the page binds against. Safe to call from
        /// the page when keys arrive lazily.
        /// </summary>
        public void SetFingerprint(byte[] keyBytes)
        {
            if (keyBytes == null || keyBytes.Length == 0)
            {
                for (int i = 0; i < EmojiRows.Count; i++) EmojiRows[i] = string.Empty;
                HexFingerprint = string.Empty;
                return;
            }

            var rowBuilders = new System.Text.StringBuilder[4];
            for (int r = 0; r < 4; r++) rowBuilders[r] = new System.Text.StringBuilder();

            for (int cell = 0; cell < 16; cell++)
            {
                int byteIndex = cell % keyBytes.Length;
                int nibble = (cell % 2 == 0)
                    ? ((keyBytes[byteIndex] >> 4) & 0x0F)
                    : (keyBytes[byteIndex] & 0x0F);
                int paletteIdx = nibble % EmojiPalette.Length;
                int row = cell / 4;
                if (rowBuilders[row].Length > 0) rowBuilders[row].Append(' ');
                rowBuilders[row].Append(EmojiPalette[paletteIdx]);
            }

            for (int r = 0; r < 4; r++) EmojiRows[r] = rowBuilders[r].ToString();

            var hexBuilder = new System.Text.StringBuilder(keyBytes.Length * 2);
            int len = keyBytes.Length < 32 ? keyBytes.Length : 32;
            for (int i = 0; i < len; i++)
            {
                hexBuilder.Append(keyBytes[i].ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
                if (i < len - 1 && (i + 1) % 2 == 0) hexBuilder.Append(' ');
            }
            HexFingerprint = hexBuilder.ToString();
        }

        private void ApplyEmojiKey(EmojiKey key)
        {
            if (key == null) return;
            // Project the EmojiKey glyph names into 4 row strings (the page
            // renders one row per ItemsControl entry). Length is 4..8.
            var glyphs = key.Glyphs;
            int rowCount = EmojiRows.Count;
            int perRow = (glyphs.Count + rowCount - 1) / rowCount;
            if (perRow < 1) perRow = 1;

            var sb = new System.Text.StringBuilder(32);
            int idx = 0;
            for (int r = 0; r < rowCount; r++)
            {
                sb.Length = 0;
                for (int k = 0; k < perRow && idx < glyphs.Count; k++, idx++)
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(glyphs[idx]);
                }
                EmojiRows[r] = sb.ToString();
            }

            // Hex projection: render the 64-bit source fingerprint as 8 hex pairs.
            ulong v = unchecked((ulong)key.SourceFingerprint.Value);
            var hex = new System.Text.StringBuilder(24);
            for (int i = 0; i < 8; i++)
            {
                byte b = (byte)((v >> (i * 8)) & 0xFF);
                hex.Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
                if (i < 7 && (i + 1) % 2 == 0) hex.Append(' ');
            }
            HexFingerprint = hex.ToString();
        }

        // ---- Command handlers ---------------------------------------

        private void RaiseCompare()
        {
            // Camera-QR cross-check is not yet a port surface; the page
            // wires CompareRequested onto a future scan flow.
            var cb = CompareRequested;
            if (cb != null) cb();
        }

        private void RaiseCopyHex()
        {
            var cb = CopyHexRequested;
            if (cb != null) cb(_hexFingerprint);
            StatusMessage = "Copied to clipboard";
        }
    }
}
