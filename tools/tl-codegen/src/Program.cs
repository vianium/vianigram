// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Vianigram.TlCodegen
{
    /// <summary>
    /// Console entry point.
    ///
    /// Usage:
    ///   tl-codegen.exe --schema input.tl --header out.h --source out.cpp [--namespace vianigram::tl] [--layer 214]
    /// </summary>
    internal static class Program
    {
        // Wave 2-A logging: emit the same [HH:MM:SS.fff Component] message
        // format the rest of Vianigram uses. The CLI writes to stdout / stderr
        // rather than Debug.WriteLine so progress is visible to the user.
        private const string Component = "tl-codegen";

        private static void LogInfo(string message)
        {
            Console.WriteLine(Format(message));
        }

        private static void LogError(string message)
        {
            Console.Error.WriteLine(Format(message));
        }

        private static string Format(string message)
        {
            var ts = DateTime.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            return "[" + ts + " " + Component + "] " + (message ?? string.Empty);
        }

        private static int Main(string[] args)
        {
            try
            {
                var opts = ParseArgs(args);
                if (opts == null)
                {
                    PrintUsage();
                    return 2;
                }

                LogInfo("reading schema: " + opts.SchemaPath);
                string schemaText = File.ReadAllText(opts.SchemaPath);

                var lexer = new TlLexer(schemaText);
                var tokens = lexer.Tokenize();
                LogInfo("tokenized: " + tokens.Count + " tokens");

                var parser = new TlParser(tokens);
                var schema = parser.Parse();
                int realTypes = 0;
                foreach (var t in schema.Types)
                    if (!t.IsPrimitiveAlias && !t.IsVectorMagicLine) realTypes++;
                LogInfo("parsed: " + realTypes + " types, " + schema.Functions.Count + " functions");

                var emitter = new TlEmitter(schema, opts.HeaderPath, opts.SourcePath, opts.Namespace, opts.Layer);
                string headerText = emitter.EmitHeader();
                string sourceText = emitter.EmitSource();

                EnsureDirectory(opts.HeaderPath);
                EnsureDirectory(opts.SourcePath);
                File.WriteAllText(opts.HeaderPath, headerText);
                File.WriteAllText(opts.SourcePath, sourceText);

                LogInfo("wrote header: " + opts.HeaderPath +
                        " (" + headerText.Length + " bytes)");
                LogInfo("wrote source: " + opts.SourcePath +
                        " (" + sourceText.Length + " bytes)");
                return 0;
            }
            catch (Exception ex)
            {
                LogError("FAILED: " + ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
                return 1;
            }
        }

        private sealed class Options
        {
            public string SchemaPath;
            public string HeaderPath;
            public string SourcePath;
            public string Namespace = "vianigram::tl";
            public int Layer = 214;
        }

        private static Options ParseArgs(string[] args)
        {
            if (args == null || args.Length == 0) return null;
            var opts = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "--schema":    opts.SchemaPath = NextArg(args, ref i, a); break;
                    case "--header":    opts.HeaderPath = NextArg(args, ref i, a); break;
                    case "--source":    opts.SourcePath = NextArg(args, ref i, a); break;
                    case "--namespace": opts.Namespace = NextArg(args, ref i, a); break;
                    case "--layer":     opts.Layer = int.Parse(NextArg(args, ref i, a)); break;
                    case "-h":
                    case "--help":      return null;
                    default:
                        LogError("unknown arg: " + a);
                        return null;
                }
            }
            if (string.IsNullOrEmpty(opts.SchemaPath) || string.IsNullOrEmpty(opts.HeaderPath) || string.IsNullOrEmpty(opts.SourcePath))
                return null;
            return opts;
        }

        private static string NextArg(string[] args, ref int i, string flag)
        {
            if (i + 1 >= args.Length) throw new ArgumentException("missing value for " + flag);
            return args[++i];
        }

        private static void EnsureDirectory(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        private static void PrintUsage()
        {
            Console.WriteLine("tl-codegen — Telegram TL schema -> C++ codegen for Vianigram.Core.Tl");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  tl-codegen.exe --schema <input.tl> --header <out.h> --source <out.cpp>");
            Console.WriteLine("                 [--namespace vianigram::tl::layer214] [--layer 214]");
        }
    }
}
