// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// Sqlite3Native — minimal P/Invoke surface against the SQLite WP8.1 SDK
// redist. The native sqlite3.dll ships with the Vianigram.App appx via the
// SQLite.WP81 ExtensionSDK reference, so the loader resolves "sqlite3" by
// name at runtime in-package.

using System;
using System.Runtime.InteropServices;

namespace Vianigram.Storage.Infrastructure.Sqlite
{
    /// <summary>
    /// Minimal subset of the SQLite C API the object store needs.
    /// Calling convention is <see cref="CallingConvention.Cdecl"/> per the
    /// public sqlite3 ABI. All strings cross the boundary as UTF-8 byte
    /// arrays; this matches <c>sqlite3_*_v2</c> conventions and avoids any
    /// charset surprise from <c>CharSet.Auto</c>.
    /// </summary>
    internal static class Sqlite3Native
    {
        // Result codes (subset). See https://www.sqlite.org/rescode.html.
        public const int SQLITE_OK = 0;
        public const int SQLITE_ROW = 100;
        public const int SQLITE_DONE = 101;

        // Open flags (subset).
        public const int SQLITE_OPEN_READWRITE = 0x00000002;
        public const int SQLITE_OPEN_CREATE = 0x00000004;
        public const int SQLITE_OPEN_FULLMUTEX = 0x00010000;

        // sqlite3_bind_*: pass SQLITE_TRANSIENT so the engine copies the buffer
        // before returning. Cast of -1 to IntPtr matches the C macro
        // (((sqlite3_destructor_type)-1)).
        public static readonly IntPtr SQLITE_TRANSIENT = new IntPtr(-1);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int sqlite3_open_v2(
            [MarshalAs(UnmanagedType.LPStr)] string filename,
            out IntPtr db,
            int flags,
            IntPtr zVfs);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_close_v2(IntPtr db);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_prepare_v2(
            IntPtr db,
            byte[] zSql,
            int nByte,
            out IntPtr ppStmt,
            IntPtr pzTail);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_step(IntPtr stmt);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_reset(IntPtr stmt);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_finalize(IntPtr stmt);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_exec(
            IntPtr db,
            byte[] zSql,
            IntPtr callback,
            IntPtr cbArg,
            IntPtr errmsg);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_bind_text(
            IntPtr stmt, int idx, byte[] zData, int nData, IntPtr xDel);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_bind_blob(
            IntPtr stmt, int idx, byte[] zData, int nData, IntPtr xDel);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_bind_int64(IntPtr stmt, int idx, long value);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr sqlite3_column_text(IntPtr stmt, int icol);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_column_bytes(IntPtr stmt, int icol);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr sqlite3_column_blob(IntPtr stmt, int icol);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        public static extern long sqlite3_column_int64(IntPtr stmt, int icol);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr sqlite3_errmsg(IntPtr db);

        // Helpers ------------------------------------------------------------

        /// <summary>UTF-8 encodes a managed string and appends a NUL terminator.</summary>
        public static byte[] Utf8Z(string s)
        {
            if (s == null) s = string.Empty;
            int n = System.Text.Encoding.UTF8.GetByteCount(s);
            byte[] buf = new byte[n + 1];
            if (n > 0) System.Text.Encoding.UTF8.GetBytes(s, 0, s.Length, buf, 0);
            buf[n] = 0;
            return buf;
        }

        /// <summary>Reads a NUL-terminated UTF-8 string from native memory.</summary>
        public static string PtrToStringUtf8(IntPtr p)
        {
            if (p == IntPtr.Zero) return null;
            int len = 0;
            while (Marshal.ReadByte(p, len) != 0) len++;
            if (len == 0) return string.Empty;
            byte[] buf = new byte[len];
            Marshal.Copy(p, buf, 0, len);
            return System.Text.Encoding.UTF8.GetString(buf, 0, len);
        }
    }
}
