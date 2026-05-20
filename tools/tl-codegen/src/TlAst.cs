// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;

namespace Vianigram.TlCodegen
{
    // ===================================================================
    // AST nodes for parsed .tl schema
    // ===================================================================

    public sealed class TlSchema
    {
        public List<TlType> Types { get; } = new List<TlType>();
        public List<TlFunction> Functions { get; } = new List<TlFunction>();
    }

    /// <summary>
    /// A constructor definition (a "type" line in the .tl schema).
    /// Each TlType maps to one C++ struct emitted by the codegen.
    /// </summary>
    public sealed class TlType
    {
        /// <summary>Lower-case name as written in the schema, e.g. <c>auth.sentCode</c>.</summary>
        public string Name { get; set; }

        /// <summary>32-bit constructor id parsed from the <c>#hex</c> suffix.</summary>
        public uint ConstructorId { get; set; }

        public List<TlField> Fields { get; } = new List<TlField>();

        /// <summary>Result type identifier (right hand side of <c>=</c>).</summary>
        public string ResultType { get; set; }

        /// <summary>True for primitive aliases that have no constructor id (e.g. <c>int ? = Int;</c>).</summary>
        public bool IsPrimitiveAlias { get; set; }

        /// <summary>True when this is a generic vector declaration (<c>vector#1cb5c415 {t:Type} ...</c>).</summary>
        public bool IsVectorMagicLine { get; set; }
    }

    public sealed class TlFunction
    {
        public string Name { get; set; }
        public uint ConstructorId { get; set; }
        public List<TlField> Arguments { get; } = new List<TlField>();
        public string ResultType { get; set; }
    }

    public sealed class TlField
    {
        public string Name { get; set; }
        public TlFieldType Type { get; set; }
        /// <summary>For <c>flags.N?T</c> fields, this is N.</summary>
        public int? FlagBit { get; set; }
        /// <summary>True when this field references the <c>flags</c> bitfield (i.e. <c>flags.N?T</c>).</summary>
        public bool IsOptional => FlagBit.HasValue;
        /// <summary>Name of the flags field this optional refers to (defaults to <c>flags</c>).</summary>
        public string FlagsFieldName { get; set; } = "flags";
        /// <summary>True when this field IS the flags bitfield (declared as <c>flags:#</c>).</summary>
        public bool IsFlagsField { get; set; }
    }

    /// <summary>
    /// Recursive type reference. Covers primitives, named types, vectors and generics.
    /// </summary>
    public sealed class TlFieldType
    {
        /// <summary>Type identifier as written in source (e.g. <c>int</c>, <c>auth.SentCodeType</c>, <c>Vector</c>).</summary>
        public string TypeName { get; set; }

        /// <summary>True when the wire encoding does not include a constructor id.</summary>
        public bool IsBare { get; set; }

        /// <summary>True for <c>Vector&lt;T&gt;</c> fields. Inner is the element type.</summary>
        public bool IsVector { get; set; }

        /// <summary>Element type for vectors / generic instantiations.</summary>
        public TlFieldType Inner { get; set; }

        public override string ToString()
        {
            if (IsVector) return (IsBare ? "vector<" : "Vector<") + Inner + ">";
            return (IsBare ? "%" : string.Empty) + TypeName;
        }
    }
}
