// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Text;

namespace Vianigram.SecretChats.Domain.ValueObjects
{
    /// <summary>
    /// Visual representation of a secret-chat key fingerprint as a sequence
    /// of emoji glyphs, used for out-of-band key verification ("compare these
    /// 4 emojis with the other side"). This is what Telegram surfaces in its
    /// secret-chat key visualization screen.
    ///
    /// <para>
    /// Algorithm (matches Telegram's stock client):
    ///   1. Take SHA-256 of <c>auth_key || g_a</c> (where <c>g_a</c> is the
    ///      initiator's public DH value, padded to 256 bytes).
    ///   2. Read the first 32 bytes; group them into 4 chunks of 8 bytes
    ///      each (big-endian).
    ///   3. For each chunk, compute <c>chunk mod 333</c> and look up the
    ///      glyph in the standard emoji table.
    /// </para>
    ///
    /// <para>
    /// We currently do NOT have <c>g_a</c> available at the managed layer
    /// (it's an exchange artifact owned by the native crypto vault), so the
    /// constructor accepts the fingerprint directly and emits a
    /// <i>diagnostic</i> 4-emoji rendering derived from the 64-bit
    /// fingerprint alone. A future revision will switch to the canonical
    /// <c>SHA-256(auth_key || g_a)</c> derivation once the crypto port
    /// surfaces it; the public type stays the same.
    /// </para>
    ///
    /// <para>
    /// The emoji table is a 333-glyph subset shared across Telegram clients;
    /// we currently ship a small representative sample, clearly documented
    /// as TEMPORARY.
    /// </para>
    /// </summary>
    public sealed class EmojiKey
    {
        // 333-glyph table (Telegram's canonical secret-chat emoji set). We
        // currently ship a deterministic 64-glyph subset that produces
        // stable, distinguishable renderings without bloating the assembly.
        // A future revision will replace this with the full 333 entries to
        // match other Telegram clients glyph-for-glyph.
        private static readonly string[] s_glyphs = new string[]
        {
            "smile", "heart", "star", "moon", "sun", "cloud", "rain", "snow",
            "tree", "leaf", "rose", "tulip", "apple", "lemon", "cherry", "grape",
            "cat", "dog", "fox", "bear", "panda", "lion", "tiger", "horse",
            "fish", "whale", "dolphin", "turtle", "bird", "owl", "eagle", "penguin",
            "car", "bus", "train", "plane", "boat", "rocket", "bike", "ship",
            "ball", "trophy", "medal", "drum", "guitar", "piano", "key", "lock",
            "fire", "water", "earth", "wind", "bolt", "flame", "wave", "mountain",
            "book", "pen", "art", "film", "camera", "phone", "lamp", "gem"
        };

        private readonly string[] _glyphs;
        private readonly KeyFingerprint _source;

        internal EmojiKey(KeyFingerprint source, string[] glyphs)
        {
            if (glyphs == null) throw new ArgumentNullException("glyphs");
            if (glyphs.Length < 4 || glyphs.Length > 8)
                throw new ArgumentException("emoji key must be 4..8 glyphs", "glyphs");
            _source = source;
            _glyphs = (string[])glyphs.Clone();
        }

        /// <summary>The fingerprint this rendering was derived from.</summary>
        public KeyFingerprint SourceFingerprint { get { return _source; } }

        /// <summary>
        /// Glyph names ordered for left-to-right display. Always 4..8 entries.
        /// Each entry is a stable English token the UI maps to its localized
        /// emoji asset (the managed layer doesn't bake in glyph drawables —
        /// presentation owns that).
        /// </summary>
        public IList<string> Glyphs
        {
            get
            {
                // Defensive copy so callers can't mutate the private array.
                var copy = new string[_glyphs.Length];
                Array.Copy(_glyphs, copy, _glyphs.Length);
                return copy;
            }
        }

        public int Length { get { return _glyphs.Length; } }

        public override string ToString()
        {
            var sb = new StringBuilder(64);
            sb.Append("EmojiKey[");
            sb.Append(_source);
            sb.Append("]:");
            for (int i = 0; i < _glyphs.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(_glyphs[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Current derivation: pull 4 glyphs from the 64-bit fingerprint by
        /// chunking it into 4 little-endian uint16 windows. Deterministic and
        /// good enough for visual verification, but NOT the same glyphs a
        /// stock Telegram client would render — the full
        /// SHA-256(auth_key||g_a) derivation is planned.
        /// </summary>
        public static EmojiKey FromFingerprint(KeyFingerprint fingerprint)
        {
            ulong v = unchecked((ulong)fingerprint.Value);
            var glyphs = new string[4];
            for (int i = 0; i < 4; i++)
            {
                ushort chunk = (ushort)((v >> (i * 16)) & 0xFFFF);
                glyphs[i] = s_glyphs[chunk % s_glyphs.Length];
            }
            return new EmojiKey(fingerprint, glyphs);
        }
    }
}
