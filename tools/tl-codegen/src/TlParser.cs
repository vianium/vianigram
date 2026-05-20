// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;

namespace Vianigram.TlCodegen
{
    /// <summary>
    /// Parses a token stream produced by <see cref="TlLexer"/> into a <see cref="TlSchema"/>.
    /// The grammar implemented is the practical subset used by Telegram layer 214 schemas.
    /// </summary>
    public sealed class TlParser
    {
        private readonly List<TlToken> _tokens;
        private int _pos;
        private bool _inFunctions;

        public TlParser(List<TlToken> tokens)
        {
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            _pos = 0;
        }

        public TlSchema Parse()
        {
            var schema = new TlSchema();
            while (Peek().Kind != TlTokenKind.EndOfFile)
            {
                var t = Peek();
                if (t.Kind == TlTokenKind.SectionFunctions)
                {
                    Advance();
                    _inFunctions = true;
                    continue;
                }
                if (t.Kind == TlTokenKind.SectionTypes)
                {
                    Advance();
                    _inFunctions = false;
                    continue;
                }

                if (_inFunctions)
                    schema.Functions.Add(ParseFunction());
                else
                    schema.Types.Add(ParseType());
            }
            return schema;
        }

        // type-decl := identifier ('#' hex)? ('{' generic-arg-list '}')? field-list '=' result-type ';'
        private TlType ParseType()
        {
            var type = new TlType();
            type.Name = ExpectQualifiedIdentifier();

            if (Peek().Kind == TlTokenKind.ConstructorId)
            {
                type.ConstructorId = Advance().NumericValue;
            }

            // generic params: {t:Type}
            while (Peek().Kind == TlTokenKind.LBrace)
            {
                SkipGenericArgs();
            }

            // Detect the canonical vector#1cb5c415 declaration shape:
            //   vector#1cb5c415 {t:Type} # [ t ] = Vector t;
            // We tolerate the body and emit nothing for it (the codegen has built-in vector support).
            if (type.Name == "vector" && type.ConstructorId == 0x1cb5c415u)
            {
                SkipUntilSemicolon();
                type.IsVectorMagicLine = true;
                return type;
            }

            // Field list until '='
            while (Peek().Kind != TlTokenKind.Equals && Peek().Kind != TlTokenKind.EndOfFile)
            {
                // Tolerate stray '?' between primitives like `int ? = Int;`
                if (Peek().Kind == TlTokenKind.Question)
                {
                    Advance();
                    continue;
                }
                if (Peek().Kind == TlTokenKind.LBracket || Peek().Kind == TlTokenKind.RBracket
                    || Peek().Kind == TlTokenKind.Bang)
                {
                    Advance();
                    continue;
                }
                var field = ParseField();
                if (field != null) type.Fields.Add(field);
            }

            Expect(TlTokenKind.Equals);
            type.ResultType = ExpectQualifiedIdentifier();
            // tolerate trailing generic params on result type: '= Vector t;'
            while (Peek().Kind != TlTokenKind.Semicolon && Peek().Kind != TlTokenKind.EndOfFile)
            {
                Advance();
            }
            Expect(TlTokenKind.Semicolon);

            // Heuristic: a "primitive alias" is a line like `int ? = Int;` with no constructor id and no fields.
            if (type.ConstructorId == 0 && type.Fields.Count == 0)
                type.IsPrimitiveAlias = true;

            return type;
        }

        private TlFunction ParseFunction()
        {
            var f = new TlFunction();
            f.Name = ExpectQualifiedIdentifier();
            if (Peek().Kind == TlTokenKind.ConstructorId)
                f.ConstructorId = Advance().NumericValue;

            while (Peek().Kind == TlTokenKind.LBrace)
                SkipGenericArgs();

            while (Peek().Kind != TlTokenKind.Equals && Peek().Kind != TlTokenKind.EndOfFile)
            {
                if (Peek().Kind == TlTokenKind.Question || Peek().Kind == TlTokenKind.LBracket
                    || Peek().Kind == TlTokenKind.RBracket || Peek().Kind == TlTokenKind.Bang)
                {
                    Advance();
                    continue;
                }
                var field = ParseField();
                if (field != null) f.Arguments.Add(field);
            }

            Expect(TlTokenKind.Equals);
            f.ResultType = ExpectQualifiedIdentifier();
            while (Peek().Kind != TlTokenKind.Semicolon && Peek().Kind != TlTokenKind.EndOfFile)
                Advance();
            Expect(TlTokenKind.Semicolon);
            return f;
        }

        // field := name ':' type-spec
        // type-spec covers: # | T | %T | flags.N?T | Vector<T> | bare!T
        private TlField ParseField()
        {
            // Skip stray hash that appears bare in a field list (rare, defensive).
            if (Peek().Kind == TlTokenKind.Hash)
            {
                Advance();
                return null;
            }

            var nameTok = Expect(TlTokenKind.Identifier);
            string name = nameTok.Text;

            // Some schemas have name like "{x:Type}" (skipped earlier) or compound bare like "x.y" rare here
            if (Peek().Kind == TlTokenKind.Dot)
            {
                while (Peek().Kind == TlTokenKind.Dot)
                {
                    Advance();
                    name += "." + Expect(TlTokenKind.Identifier).Text;
                }
            }

            Expect(TlTokenKind.Colon);

            var field = new TlField { Name = name };

            // flags.N?T
            // Detect by: identifier followed by '.', followed by Number, followed by '?'
            if (Peek().Kind == TlTokenKind.Identifier && PeekAt(1).Kind == TlTokenKind.Dot
                && PeekAt(2).Kind == TlTokenKind.Number && PeekAt(3).Kind == TlTokenKind.Question)
            {
                field.FlagsFieldName = Advance().Text;     // "flags"
                Expect(TlTokenKind.Dot);
                field.FlagBit = (int)Advance().NumericValue;
                Expect(TlTokenKind.Question);
                field.Type = ParseTypeRef();
                return field;
            }

            // flags:# special case — declares a uint32 bitfield
            if (Peek().Kind == TlTokenKind.Hash)
            {
                Advance();
                field.IsFlagsField = true;
                field.Type = new TlFieldType { TypeName = "#", IsBare = true };
                return field;
            }

            field.Type = ParseTypeRef();
            return field;
        }

        // type-ref := ('%' | '!')? identifier ('.' identifier)* ('<' type-ref '>')?
        private TlFieldType ParseTypeRef()
        {
            bool bare = false;
            if (Peek().Kind == TlTokenKind.Percent || Peek().Kind == TlTokenKind.Bang)
            {
                bare = true;
                Advance();
            }

            var idTok = Expect(TlTokenKind.Identifier);
            string name = idTok.Text;
            while (Peek().Kind == TlTokenKind.Dot)
            {
                Advance();
                name += "." + Expect(TlTokenKind.Identifier).Text;
            }

            // Check Vector<T> generic syntax
            if (Peek().Kind == TlTokenKind.LAngle)
            {
                Advance();
                var inner = ParseTypeRef();
                Expect(TlTokenKind.RAngle);
                bool isVector = string.Equals(name, "Vector", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(name, "vector", StringComparison.Ordinal);
                return new TlFieldType
                {
                    TypeName = name,
                    IsBare = bare,
                    IsVector = isVector,
                    Inner = inner,
                };
            }

            return new TlFieldType { TypeName = name, IsBare = bare };
        }

        private string ExpectQualifiedIdentifier()
        {
            string name = Expect(TlTokenKind.Identifier).Text;
            while (Peek().Kind == TlTokenKind.Dot)
            {
                Advance();
                name += "." + Expect(TlTokenKind.Identifier).Text;
            }
            return name;
        }

        private void SkipGenericArgs()
        {
            Expect(TlTokenKind.LBrace);
            int depth = 1;
            while (depth > 0 && Peek().Kind != TlTokenKind.EndOfFile)
            {
                var k = Advance().Kind;
                if (k == TlTokenKind.LBrace) depth++;
                else if (k == TlTokenKind.RBrace) depth--;
            }
        }

        private void SkipUntilSemicolon()
        {
            while (Peek().Kind != TlTokenKind.Semicolon && Peek().Kind != TlTokenKind.EndOfFile)
                Advance();
            if (Peek().Kind == TlTokenKind.Semicolon) Advance();
        }

        private TlToken Peek() => _tokens[_pos];
        private TlToken PeekAt(int offset) =>
            _pos + offset < _tokens.Count ? _tokens[_pos + offset] : _tokens[_tokens.Count - 1];
        private TlToken Advance() => _tokens[_pos++];

        private TlToken Expect(TlTokenKind kind)
        {
            var t = Peek();
            if (t.Kind != kind)
                throw new TlParseException($"Expected {kind} but found {t.Kind} '{t.Text}'", t.Line, t.Column);
            return Advance();
        }
    }

    public sealed class TlParseException : Exception
    {
        public int Line { get; }
        public int Column { get; }
        public TlParseException(string message, int line, int column)
            : base($"{message} at {line}:{column}")
        {
            Line = line;
            Column = column;
        }
    }
}
