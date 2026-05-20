// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Vianigram.TlCodegen
{
    /// <summary>Token kinds emitted by <see cref="TlLexer"/>.</summary>
    public enum TlTokenKind
    {
        Identifier,        // foo, auth.sentCode, Vector
        ConstructorId,     // #1cb5c415  (32-bit hex value, no leading '#')
        Number,            // 0, 1, 32, ...
        Equals,            // =
        Colon,             // :
        Semicolon,         // ;
        Question,          // ?
        Dot,               // .
        Hash,              // # (used as the type marker for the flags field)
        LBracket,          // [
        RBracket,          // ]
        LBrace,            // {
        RBrace,            // }
        LAngle,            // <
        RAngle,            // >
        Pipe,              // |
        Percent,           // %
        Bang,              // !
        SectionFunctions,  // ---functions---
        SectionTypes,      // ---types---
        EndOfFile
    }

    public sealed class TlToken
    {
        public TlTokenKind Kind { get; }
        public string Text { get; }
        public int Line { get; }
        public int Column { get; }
        public uint NumericValue { get; }

        public TlToken(TlTokenKind kind, string text, int line, int column, uint numericValue = 0)
        {
            Kind = kind;
            Text = text;
            Line = line;
            Column = column;
            NumericValue = numericValue;
        }

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture,
                "{0}({1}) @ {2}:{3}", Kind, Text, Line, Column);
    }

    /// <summary>
    /// Tokenizer for the Telegram Type Language grammar.
    /// Implements enough of https://core.telegram.org/mtproto/TL to support layer 214.
    /// </summary>
    public sealed class TlLexer
    {
        private readonly string _source;
        private int _pos;
        private int _line;
        private int _col;

        public TlLexer(string source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _pos = 0;
            _line = 1;
            _col = 1;
        }

        public List<TlToken> Tokenize()
        {
            var tokens = new List<TlToken>();
            while (true)
            {
                SkipTrivia();
                if (_pos >= _source.Length)
                {
                    tokens.Add(new TlToken(TlTokenKind.EndOfFile, string.Empty, _line, _col));
                    return tokens;
                }
                tokens.Add(NextToken());
            }
        }

        private TlToken NextToken()
        {
            int startLine = _line;
            int startCol = _col;
            char c = Peek();

            // Section markers: ---functions--- and ---types---
            if (c == '-' && Match("---"))
            {
                int sectStart = _pos;
                Advance(3);
                var sb = new StringBuilder();
                while (_pos < _source.Length && IsIdentifierChar(Peek()))
                {
                    sb.Append(Peek());
                    Advance(1);
                }
                string name = sb.ToString();
                if (Match("---"))
                {
                    Advance(3);
                    if (name == "functions")
                        return new TlToken(TlTokenKind.SectionFunctions, "---functions---", startLine, startCol);
                    if (name == "types")
                        return new TlToken(TlTokenKind.SectionTypes, "---types---", startLine, startCol);
                    throw new TlLexException($"Unknown section marker '---{name}---'", startLine, startCol);
                }
                throw new TlLexException("Malformed section marker (expected closing '---')", startLine, startCol);
            }

            switch (c)
            {
                case '=': Advance(1); return new TlToken(TlTokenKind.Equals, "=", startLine, startCol);
                case ':': Advance(1); return new TlToken(TlTokenKind.Colon, ":", startLine, startCol);
                case ';': Advance(1); return new TlToken(TlTokenKind.Semicolon, ";", startLine, startCol);
                case '?': Advance(1); return new TlToken(TlTokenKind.Question, "?", startLine, startCol);
                case '.': Advance(1); return new TlToken(TlTokenKind.Dot, ".", startLine, startCol);
                case '[': Advance(1); return new TlToken(TlTokenKind.LBracket, "[", startLine, startCol);
                case ']': Advance(1); return new TlToken(TlTokenKind.RBracket, "]", startLine, startCol);
                case '{': Advance(1); return new TlToken(TlTokenKind.LBrace, "{", startLine, startCol);
                case '}': Advance(1); return new TlToken(TlTokenKind.RBrace, "}", startLine, startCol);
                case '<': Advance(1); return new TlToken(TlTokenKind.LAngle, "<", startLine, startCol);
                case '>': Advance(1); return new TlToken(TlTokenKind.RAngle, ">", startLine, startCol);
                case '|': Advance(1); return new TlToken(TlTokenKind.Pipe, "|", startLine, startCol);
                case '%': Advance(1); return new TlToken(TlTokenKind.Percent, "%", startLine, startCol);
                case '!': Advance(1); return new TlToken(TlTokenKind.Bang, "!", startLine, startCol);
            }

            // # — either a 32-bit hex constructor id (#1cb5c415) or a bare hash token (flags:#)
            if (c == '#')
            {
                Advance(1);
                if (_pos < _source.Length && IsHexDigit(Peek()))
                {
                    var sb = new StringBuilder();
                    while (_pos < _source.Length && IsHexDigit(Peek()))
                    {
                        sb.Append(Peek());
                        Advance(1);
                    }
                    string hex = sb.ToString();
                    if (hex.Length == 0 || hex.Length > 8)
                        throw new TlLexException($"Invalid constructor id '#{hex}'", startLine, startCol);
                    uint value = uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    return new TlToken(TlTokenKind.ConstructorId, hex, startLine, startCol, value);
                }
                return new TlToken(TlTokenKind.Hash, "#", startLine, startCol);
            }

            if (char.IsDigit(c))
            {
                var sb = new StringBuilder();
                while (_pos < _source.Length && char.IsDigit(Peek()))
                {
                    sb.Append(Peek());
                    Advance(1);
                }
                string num = sb.ToString();
                uint val = uint.Parse(num, CultureInfo.InvariantCulture);
                return new TlToken(TlTokenKind.Number, num, startLine, startCol, val);
            }

            if (IsIdentifierStart(c))
            {
                var sb = new StringBuilder();
                while (_pos < _source.Length && IsIdentifierChar(Peek()))
                {
                    sb.Append(Peek());
                    Advance(1);
                }
                return new TlToken(TlTokenKind.Identifier, sb.ToString(), startLine, startCol);
            }

            throw new TlLexException($"Unexpected character '{c}' (U+{((int)c):X4})", startLine, startCol);
        }

        private void SkipTrivia()
        {
            while (_pos < _source.Length)
            {
                char c = Peek();
                if (char.IsWhiteSpace(c))
                {
                    Advance(1);
                    continue;
                }
                if (c == '/' && _pos + 1 < _source.Length)
                {
                    char n = _source[_pos + 1];
                    if (n == '/')
                    {
                        // line comment
                        while (_pos < _source.Length && Peek() != '\n') Advance(1);
                        continue;
                    }
                    if (n == '*')
                    {
                        // block comment
                        Advance(2);
                        while (_pos < _source.Length && !(Peek() == '*' && _pos + 1 < _source.Length && _source[_pos + 1] == '/'))
                            Advance(1);
                        if (_pos < _source.Length) Advance(2);
                        continue;
                    }
                }
                break;
            }
        }

        private char Peek() => _source[_pos];

        private void Advance(int n)
        {
            for (int i = 0; i < n && _pos < _source.Length; i++)
            {
                if (_source[_pos] == '\n')
                {
                    _line++;
                    _col = 1;
                }
                else
                {
                    _col++;
                }
                _pos++;
            }
        }

        private bool Match(string s)
        {
            if (_pos + s.Length > _source.Length) return false;
            for (int i = 0; i < s.Length; i++)
                if (_source[_pos + i] != s[i]) return false;
            return true;
        }

        private static bool IsIdentifierStart(char c) =>
            c == '_' || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        private static bool IsIdentifierChar(char c) =>
            IsIdentifierStart(c) || (c >= '0' && c <= '9');

        private static bool IsHexDigit(char c) =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    }

    public sealed class TlLexException : Exception
    {
        public int Line { get; }
        public int Column { get; }
        public TlLexException(string message, int line, int column)
            : base($"{message} at {line}:{column}")
        {
            Line = line;
            Column = column;
        }
    }
}
