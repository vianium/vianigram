// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Text;

namespace Vianigram.App.Helpers
{
    internal static class QrCodeGenerator
    {
        private const int EccFormatBitsM = 0;
        private static readonly int[] GfExp = new int[512];
        private static readonly int[] GfLog = new int[256];
        private static readonly QrVersionInfo[] Versions = BuildVersions();

        static QrCodeGenerator()
        {
            int value = 1;
            for (int i = 0; i < 255; i++)
            {
                GfExp[i] = value;
                GfLog[value] = i;
                value <<= 1;
                if ((value & 0x100) != 0)
                {
                    value ^= 0x11D;
                }
            }

            for (int i = 255; i < 512; i++)
            {
                GfExp[i] = GfExp[i - 255];
            }
        }

        public static bool[,] EncodeText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                throw new ArgumentException("QR text is empty");
            }

            byte[] data = Encoding.UTF8.GetBytes(text);
            int version = SelectVersion(data.Length);
            QrVersionInfo versionInfo = Versions[version];
            int[] codewords = BuildFinalCodewords(data, versionInfo);

            bool[,] modules = BuildBaseMatrix(version, codewords);
            bool[,] functionModules = BuildFunctionMatrix(version);

            int bestMask = 0;
            int bestPenalty = int.MaxValue;
            bool[,] bestMatrix = null;

            for (int mask = 0; mask < 8; mask++)
            {
                bool[,] candidate = CloneMatrix(modules);
                ApplyMask(candidate, functionModules, mask);
                DrawFormatBits(candidate, functionModules, mask);

                int penalty = CalculatePenalty(candidate);
                if (penalty < bestPenalty)
                {
                    bestPenalty = penalty;
                    bestMask = mask;
                    bestMatrix = candidate;
                }
            }

            if (bestMatrix == null)
            {
                throw new InvalidOperationException("QR mask selection failed");
            }

            DrawFormatBits(bestMatrix, functionModules, bestMask);
            return bestMatrix;
        }

        private static int SelectVersion(int byteLength)
        {
            for (int version = 1; version < Versions.Length; version++)
            {
                QrVersionInfo info = Versions[version];
                int charCountBits = version <= 9 ? 8 : 16;
                int requiredBits = 4 + charCountBits + byteLength * 8;
                if (requiredBits <= info.DataCodewords * 8)
                {
                    return version;
                }
            }

            throw new InvalidOperationException("QR payload is too large for the built-in encoder");
        }

        private static int[] BuildFinalCodewords(byte[] data, QrVersionInfo info)
        {
            BitBuffer bits = new BitBuffer();
            bits.AppendBits(0x4, 4);
            bits.AppendBits(data.Length, info.Version <= 9 ? 8 : 16);

            for (int i = 0; i < data.Length; i++)
            {
                bits.AppendBits(data[i], 8);
            }

            int capacityBits = info.DataCodewords * 8;
            int terminatorBits = Math.Min(4, capacityBits - bits.Length);
            bits.AppendBits(0, terminatorBits);

            while ((bits.Length & 7) != 0)
            {
                bits.AppendBit(false);
            }

            List<int> dataCodewords = bits.ToCodewords();
            int pad = 0xEC;
            while (dataCodewords.Count < info.DataCodewords)
            {
                dataCodewords.Add(pad);
                pad = pad == 0xEC ? 0x11 : 0xEC;
            }

            List<int[]> dataBlocks = SplitDataBlocks(dataCodewords, info.BlockDataCodewords);
            List<int[]> eccBlocks = new List<int[]>();

            for (int i = 0; i < dataBlocks.Count; i++)
            {
                eccBlocks.Add(ComputeReedSolomonRemainder(dataBlocks[i], info.EccCodewordsPerBlock));
            }

            List<int> result = new List<int>();
            int maxDataLength = 0;
            for (int i = 0; i < dataBlocks.Count; i++)
            {
                if (dataBlocks[i].Length > maxDataLength)
                {
                    maxDataLength = dataBlocks[i].Length;
                }
            }

            for (int index = 0; index < maxDataLength; index++)
            {
                for (int block = 0; block < dataBlocks.Count; block++)
                {
                    if (index < dataBlocks[block].Length)
                    {
                        result.Add(dataBlocks[block][index]);
                    }
                }
            }

            for (int index = 0; index < info.EccCodewordsPerBlock; index++)
            {
                for (int block = 0; block < eccBlocks.Count; block++)
                {
                    result.Add(eccBlocks[block][index]);
                }
            }

            return result.ToArray();
        }

        private static List<int[]> SplitDataBlocks(List<int> codewords, int[] blockLengths)
        {
            List<int[]> blocks = new List<int[]>();
            int offset = 0;

            for (int block = 0; block < blockLengths.Length; block++)
            {
                int length = blockLengths[block];
                int[] data = new int[length];
                for (int i = 0; i < length; i++)
                {
                    data[i] = codewords[offset + i];
                }

                blocks.Add(data);
                offset += length;
            }

            return blocks;
        }

        private static bool[,] BuildBaseMatrix(int version, int[] codewords)
        {
            int size = version * 4 + 17;
            bool[,] modules = new bool[size, size];
            bool[,] functionModules = BuildFunctionMatrix(version);

            DrawFunctionPatterns(modules, functionModules, version);
            DrawCodewords(modules, functionModules, codewords);
            return modules;
        }

        private static bool[,] BuildFunctionMatrix(int version)
        {
            int size = version * 4 + 17;
            bool[,] modules = new bool[size, size];
            bool[,] functionModules = new bool[size, size];
            DrawFunctionPatterns(modules, functionModules, version);
            return functionModules;
        }

        private static void DrawFunctionPatterns(bool[,] modules, bool[,] functionModules, int version)
        {
            int size = modules.GetLength(0);

            DrawFinderPattern(modules, functionModules, 3, 3);
            DrawFinderPattern(modules, functionModules, size - 4, 3);
            DrawFinderPattern(modules, functionModules, 3, size - 4);

            for (int i = 8; i < size - 8; i++)
            {
                bool dark = (i & 1) == 0;
                SetFunctionModule(modules, functionModules, i, 6, dark);
                SetFunctionModule(modules, functionModules, 6, i, dark);
            }

            int[] positions = GetAlignmentPatternPositions(version);
            for (int i = 0; i < positions.Length; i++)
            {
                for (int j = 0; j < positions.Length; j++)
                {
                    int x = positions[i];
                    int y = positions[j];
                    bool nearTop = y == 6;
                    bool nearLeft = x == 6;
                    bool nearRight = x == size - 7;
                    if ((nearTop && nearLeft) || (nearTop && nearRight) || (!nearTop && nearLeft && y == size - 7))
                    {
                        continue;
                    }

                    DrawAlignmentPattern(modules, functionModules, x, y);
                }
            }

            ReserveFormatAreas(functionModules, size);
            SetFunctionModule(modules, functionModules, 8, size - 8, true);

            if (version >= 7)
            {
                DrawVersionBits(modules, functionModules, version);
            }
        }

        private static void DrawFinderPattern(bool[,] modules, bool[,] functionModules, int centerX, int centerY)
        {
            for (int dy = -4; dy <= 4; dy++)
            {
                for (int dx = -4; dx <= 4; dx++)
                {
                    int x = centerX + dx;
                    int y = centerY + dy;
                    if (!IsInside(modules, x, y))
                    {
                        continue;
                    }

                    int distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    bool dark = distance == 3 || distance <= 1;
                    SetFunctionModule(modules, functionModules, x, y, dark);
                }
            }
        }

        private static void DrawAlignmentPattern(bool[,] modules, bool[,] functionModules, int centerX, int centerY)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    int distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    bool dark = distance == 2 || distance == 0;
                    SetFunctionModule(modules, functionModules, centerX + dx, centerY + dy, dark);
                }
            }
        }

        private static void ReserveFormatAreas(bool[,] functionModules, int size)
        {
            for (int i = 0; i <= 8; i++)
            {
                if (i != 6)
                {
                    functionModules[8, i] = true;
                    functionModules[i, 8] = true;
                }
            }

            for (int i = 0; i < 8; i++)
            {
                functionModules[size - 1 - i, 8] = true;
            }

            for (int i = 8; i < 15; i++)
            {
                functionModules[8, size - 15 + i] = true;
            }

            functionModules[8, size - 8] = true;
        }

        private static void DrawVersionBits(bool[,] modules, bool[,] functionModules, int version)
        {
            int size = modules.GetLength(0);
            int bits = GetVersionBits(version);

            for (int i = 0; i < 18; i++)
            {
                bool dark = GetBit(bits, i);
                int a = size - 11 + i % 3;
                int b = i / 3;
                SetFunctionModule(modules, functionModules, a, b, dark);
                SetFunctionModule(modules, functionModules, b, a, dark);
            }
        }

        private static void DrawCodewords(bool[,] modules, bool[,] functionModules, int[] codewords)
        {
            int size = modules.GetLength(0);
            int bitIndex = 0;
            int totalBits = codewords.Length * 8;
            bool upward = true;

            for (int right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6)
                {
                    right--;
                }

                for (int vertical = 0; vertical < size; vertical++)
                {
                    int y = upward ? size - 1 - vertical : vertical;
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        if (functionModules[y, x])
                        {
                            continue;
                        }

                        bool dark = false;
                        if (bitIndex < totalBits)
                        {
                            int codeword = codewords[bitIndex >> 3];
                            dark = ((codeword >> (7 - (bitIndex & 7))) & 1) != 0;
                            bitIndex++;
                        }

                        modules[y, x] = dark;
                    }
                }

                upward = !upward;
            }
        }

        private static void ApplyMask(bool[,] modules, bool[,] functionModules, int mask)
        {
            int size = modules.GetLength(0);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (!functionModules[y, x] && GetMaskBit(mask, x, y))
                    {
                        modules[y, x] = !modules[y, x];
                    }
                }
            }
        }

        private static void DrawFormatBits(bool[,] modules, bool[,] functionModules, int mask)
        {
            int size = modules.GetLength(0);
            int bits = GetFormatBits(mask);

            for (int i = 0; i <= 5; i++)
            {
                SetFunctionModule(modules, functionModules, 8, i, GetBit(bits, i));
            }

            SetFunctionModule(modules, functionModules, 8, 7, GetBit(bits, 6));
            SetFunctionModule(modules, functionModules, 8, 8, GetBit(bits, 7));
            SetFunctionModule(modules, functionModules, 7, 8, GetBit(bits, 8));

            for (int i = 9; i < 15; i++)
            {
                SetFunctionModule(modules, functionModules, 14 - i, 8, GetBit(bits, i));
            }

            for (int i = 0; i < 8; i++)
            {
                SetFunctionModule(modules, functionModules, size - 1 - i, 8, GetBit(bits, i));
            }

            for (int i = 8; i < 15; i++)
            {
                SetFunctionModule(modules, functionModules, 8, size - 15 + i, GetBit(bits, i));
            }

            SetFunctionModule(modules, functionModules, 8, size - 8, true);
        }

        private static int[] ComputeReedSolomonRemainder(int[] data, int degree)
        {
            int[] generator = BuildGeneratorPolynomial(degree);
            int[] result = new int[degree];

            for (int i = 0; i < data.Length; i++)
            {
                int factor = data[i] ^ result[0];
                for (int j = 0; j < degree - 1; j++)
                {
                    result[j] = result[j + 1];
                }

                result[degree - 1] = 0;

                for (int j = 0; j < degree; j++)
                {
                    result[j] ^= GfMultiply(generator[j + 1], factor);
                }
            }

            return result;
        }

        private static int[] BuildGeneratorPolynomial(int degree)
        {
            int[] result = new int[] { 1 };

            for (int i = 0; i < degree; i++)
            {
                int[] next = new int[result.Length + 1];
                for (int j = 0; j < result.Length; j++)
                {
                    next[j] ^= GfMultiply(result[j], 1);
                    next[j + 1] ^= GfMultiply(result[j], GfExp[i]);
                }

                result = next;
            }

            return result;
        }

        private static int GfMultiply(int x, int y)
        {
            if (x == 0 || y == 0)
            {
                return 0;
            }

            return GfExp[GfLog[x] + GfLog[y]];
        }

        private static int CalculatePenalty(bool[,] modules)
        {
            return PenaltyRuns(modules) + PenaltyBlocks(modules) + PenaltyFinderLike(modules) + PenaltyBalance(modules);
        }

        private static int PenaltyRuns(bool[,] modules)
        {
            int size = modules.GetLength(0);
            int penalty = 0;

            for (int y = 0; y < size; y++)
            {
                bool runColor = modules[y, 0];
                int runLength = 1;
                for (int x = 1; x < size; x++)
                {
                    if (modules[y, x] == runColor)
                    {
                        runLength++;
                    }
                    else
                    {
                        if (runLength >= 5)
                        {
                            penalty += runLength - 2;
                        }

                        runColor = modules[y, x];
                        runLength = 1;
                    }
                }

                if (runLength >= 5)
                {
                    penalty += runLength - 2;
                }
            }

            for (int x = 0; x < size; x++)
            {
                bool runColor = modules[0, x];
                int runLength = 1;
                for (int y = 1; y < size; y++)
                {
                    if (modules[y, x] == runColor)
                    {
                        runLength++;
                    }
                    else
                    {
                        if (runLength >= 5)
                        {
                            penalty += runLength - 2;
                        }

                        runColor = modules[y, x];
                        runLength = 1;
                    }
                }

                if (runLength >= 5)
                {
                    penalty += runLength - 2;
                }
            }

            return penalty;
        }

        private static int PenaltyBlocks(bool[,] modules)
        {
            int size = modules.GetLength(0);
            int penalty = 0;

            for (int y = 0; y < size - 1; y++)
            {
                for (int x = 0; x < size - 1; x++)
                {
                    bool color = modules[y, x];
                    if (modules[y, x + 1] == color && modules[y + 1, x] == color && modules[y + 1, x + 1] == color)
                    {
                        penalty += 3;
                    }
                }
            }

            return penalty;
        }

        private static int PenaltyFinderLike(bool[,] modules)
        {
            int size = modules.GetLength(0);
            int penalty = 0;
            int pattern1 = 0x5D0;
            int pattern2 = 0x05D;

            for (int y = 0; y < size; y++)
            {
                int bits = 0;
                for (int x = 0; x < size; x++)
                {
                    bits = ((bits << 1) & 0x7FF) | (modules[y, x] ? 1 : 0);
                    if (x >= 10 && (bits == pattern1 || bits == pattern2))
                    {
                        penalty += 40;
                    }
                }
            }

            for (int x = 0; x < size; x++)
            {
                int bits = 0;
                for (int y = 0; y < size; y++)
                {
                    bits = ((bits << 1) & 0x7FF) | (modules[y, x] ? 1 : 0);
                    if (y >= 10 && (bits == pattern1 || bits == pattern2))
                    {
                        penalty += 40;
                    }
                }
            }

            return penalty;
        }

        private static int PenaltyBalance(bool[,] modules)
        {
            int size = modules.GetLength(0);
            int dark = 0;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (modules[y, x])
                    {
                        dark++;
                    }
                }
            }

            int total = size * size;
            int percent = dark * 100 / total;
            int fivePercentSteps = Math.Abs(percent - 50) / 5;
            return fivePercentSteps * 10;
        }

        private static int GetFormatBits(int mask)
        {
            int data = (EccFormatBitsM << 3) | mask;
            int bits = data << 10;
            int generator = 0x537;

            while (GetBchDigit(bits) - GetBchDigit(generator) >= 0)
            {
                bits ^= generator << (GetBchDigit(bits) - GetBchDigit(generator));
            }

            return ((data << 10) | bits) ^ 0x5412;
        }

        private static int GetVersionBits(int version)
        {
            int bits = version << 12;
            int generator = 0x1F25;

            while (GetBchDigit(bits) - GetBchDigit(generator) >= 0)
            {
                bits ^= generator << (GetBchDigit(bits) - GetBchDigit(generator));
            }

            return (version << 12) | bits;
        }

        private static int GetBchDigit(int value)
        {
            int digit = 0;
            while (value != 0)
            {
                digit++;
                value >>= 1;
            }

            return digit;
        }

        private static bool GetMaskBit(int mask, int x, int y)
        {
            switch (mask)
            {
                case 0: return ((x + y) & 1) == 0;
                case 1: return (y & 1) == 0;
                case 2: return x % 3 == 0;
                case 3: return (x + y) % 3 == 0;
                case 4: return (((y / 2) + (x / 3)) & 1) == 0;
                case 5: return ((x * y) % 2 + (x * y) % 3) == 0;
                case 6: return (((x * y) % 2 + (x * y) % 3) & 1) == 0;
                case 7: return (((x + y) % 2 + (x * y) % 3) & 1) == 0;
                default: return false;
            }
        }

        private static int[] GetAlignmentPatternPositions(int version)
        {
            switch (version)
            {
                case 1: return new int[0];
                case 2: return new int[] { 6, 18 };
                case 3: return new int[] { 6, 22 };
                case 4: return new int[] { 6, 26 };
                case 5: return new int[] { 6, 30 };
                case 6: return new int[] { 6, 34 };
                case 7: return new int[] { 6, 22, 38 };
                case 8: return new int[] { 6, 24, 42 };
                case 9: return new int[] { 6, 26, 46 };
                case 10: return new int[] { 6, 28, 50 };
                default: throw new InvalidOperationException("Unsupported QR version " + version);
            }
        }

        private static void SetFunctionModule(bool[,] modules, bool[,] functionModules, int x, int y, bool dark)
        {
            modules[y, x] = dark;
            functionModules[y, x] = true;
        }

        private static bool IsInside(bool[,] modules, int x, int y)
        {
            int size = modules.GetLength(0);
            return x >= 0 && y >= 0 && x < size && y < size;
        }

        private static bool[,] CloneMatrix(bool[,] source)
        {
            int size = source.GetLength(0);
            bool[,] copy = new bool[size, size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    copy[y, x] = source[y, x];
                }
            }

            return copy;
        }

        private static bool GetBit(int value, int bit)
        {
            return ((value >> bit) & 1) != 0;
        }

        private static QrVersionInfo[] BuildVersions()
        {
            QrVersionInfo[] versions = new QrVersionInfo[11];
            versions[1] = new QrVersionInfo(1, 10, new int[] { 16 });
            versions[2] = new QrVersionInfo(2, 16, new int[] { 28 });
            versions[3] = new QrVersionInfo(3, 26, new int[] { 44 });
            versions[4] = new QrVersionInfo(4, 18, new int[] { 32, 32 });
            versions[5] = new QrVersionInfo(5, 24, new int[] { 43, 43 });
            versions[6] = new QrVersionInfo(6, 16, new int[] { 27, 27, 27, 27 });
            versions[7] = new QrVersionInfo(7, 18, new int[] { 31, 31, 31, 31 });
            versions[8] = new QrVersionInfo(8, 22, new int[] { 38, 38, 39, 39 });
            versions[9] = new QrVersionInfo(9, 22, new int[] { 36, 36, 36, 37, 37 });
            versions[10] = new QrVersionInfo(10, 26, new int[] { 43, 43, 43, 43, 44 });
            return versions;
        }

        private sealed class QrVersionInfo
        {
            public readonly int Version;
            public readonly int EccCodewordsPerBlock;
            public readonly int[] BlockDataCodewords;
            public readonly int DataCodewords;

            public QrVersionInfo(int version, int eccCodewordsPerBlock, int[] blockDataCodewords)
            {
                Version = version;
                EccCodewordsPerBlock = eccCodewordsPerBlock;
                BlockDataCodewords = blockDataCodewords;

                int total = 0;
                for (int i = 0; i < blockDataCodewords.Length; i++)
                {
                    total += blockDataCodewords[i];
                }

                DataCodewords = total;
            }
        }

        private sealed class BitBuffer
        {
            private readonly List<bool> _bits = new List<bool>();

            public int Length
            {
                get { return _bits.Count; }
            }

            public void AppendBit(bool bit)
            {
                _bits.Add(bit);
            }

            public void AppendBits(int value, int count)
            {
                for (int i = count - 1; i >= 0; i--)
                {
                    _bits.Add(((value >> i) & 1) != 0);
                }
            }

            public List<int> ToCodewords()
            {
                List<int> result = new List<int>();
                for (int i = 0; i < _bits.Count; i += 8)
                {
                    int value = 0;
                    for (int j = 0; j < 8; j++)
                    {
                        if (_bits[i + j])
                        {
                            value |= 1 << (7 - j);
                        }
                    }

                    result.Add(value);
                }

                return result;
            }
        }
    }
}
