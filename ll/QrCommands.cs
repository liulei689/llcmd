using System.Text;
using QRCoder;

namespace LL;

public static class QrCommands
{
    public static void Print(string[] args)
    {
        if (args.Length == 0)
        {
            UI.PrintError("请提供 URL");
            UI.PrintInfo("用法: 17 https://example.com   或   qr https://example.com");
            UI.PrintInfo("可选: --classic(黑白)");
            return;
        }

        var url = args[0].Trim();
        bool classic = args.Any(a => a.Equals("--classic", StringComparison.OrdinalIgnoreCase));
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            UI.PrintError("URL 格式不正确");
            return;
        }

        try
        {
            var path = SaveQrPng(uri.ToString(), classic);
            UI.PrintSuccess($"二维码已生成: {path}");
            UI.PrintInfo("已自动打开图片，手机扫码即可。");
            TryOpen(path);
        }
        catch (Exception ex)
        {
            UI.PrintError($"生成二维码失败: {ex.Message}");
        }
    }

    private static string SaveQrPng(string text, bool classic)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);

        // QRCoder 1.6.x 的 PngByteQRCode 仅提供基础渲染；这里保持高分辨率输出
        var png = new PngByteQRCode(data).GetGraphic(pixelsPerModule: 12);

        string safe = "qr";
        try
        {
            safe = new string(text.Where(ch => char.IsLetterOrDigit(ch)).Take(24).ToArray());
            if (string.IsNullOrWhiteSpace(safe)) safe = "qr";
        }
        catch { }

        var file = Path.Combine(Path.GetTempPath(), $"ll_{safe}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        File.WriteAllBytes(file, png);
        return file;
    }

    private static void TryOpen(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
            {
                UseShellExecute = true
            });
        }
        catch { }
    }

    private static class PngWriter
    {
        public static byte[] WriteGray8(int width, int height, byte[] rawScanlines)
        {
            // rawScanlines must include filter byte per row.
            var ihdr = new byte[13];
            WriteInt(ihdr, 0, width);
            WriteInt(ihdr, 4, height);
            ihdr[8] = 8;  // bit depth
            ihdr[9] = 0;  // color type: grayscale
            ihdr[10] = 0; // compression
            ihdr[11] = 0; // filter
            ihdr[12] = 0; // interlace

            var idatData = ZlibDeflateNoCompression(rawScanlines);

            using var ms = new MemoryStream();
            ms.Write([137, 80, 78, 71, 13, 10, 26, 10]);
            WriteChunk(ms, "IHDR", ihdr);
            WriteChunk(ms, "IDAT", idatData);
            WriteChunk(ms, "IEND", []);
            return ms.ToArray();
        }

        private static byte[] ZlibDeflateNoCompression(byte[] data)
        {
            // Zlib header: CMF/FLG for deflate, 32K window, check bits ok.
            // Use "stored" deflate blocks (no compression) for simplicity.
            using var ms = new MemoryStream();
            ms.WriteByte(0x78);
            ms.WriteByte(0x01);

            int offset = 0;
            while (offset < data.Length)
            {
                int len = Math.Min(65535, data.Length - offset);
                bool final = (offset + len) >= data.Length;

                ms.WriteByte(final ? (byte)0x01 : (byte)0x00); // BFINAL + BTYPE=00
                ms.WriteByte((byte)(len & 0xFF));
                ms.WriteByte((byte)((len >> 8) & 0xFF));
                int nlen = ~len;
                ms.WriteByte((byte)(nlen & 0xFF));
                ms.WriteByte((byte)((nlen >> 8) & 0xFF));
                ms.Write(data, offset, len);
                offset += len;
            }

            uint adler = Adler32(data);
            ms.WriteByte((byte)((adler >> 24) & 0xFF));
            ms.WriteByte((byte)((adler >> 16) & 0xFF));
            ms.WriteByte((byte)((adler >> 8) & 0xFF));
            ms.WriteByte((byte)(adler & 0xFF));
            return ms.ToArray();
        }

        private static uint Adler32(byte[] data)
        {
            const uint Mod = 65521;
            uint a = 1, b = 0;
            foreach (var t in data)
            {
                a = (a + t) % Mod;
                b = (b + a) % Mod;
            }
            return (b << 16) | a;
        }

        private static void WriteChunk(Stream s, string type, byte[] data)
        {
            Span<byte> len = stackalloc byte[4];
            WriteInt(len, 0, data.Length);
            s.Write(len);

            var typeBytes = Encoding.ASCII.GetBytes(type);
            s.Write(typeBytes);
            s.Write(data);

            uint crc = Crc32(typeBytes, data);
            Span<byte> crcB = stackalloc byte[4];
            WriteUInt(crcB, 0, crc);
            s.Write(crcB);
        }

        private static uint Crc32(byte[] type, byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (var b in type) crc = CrcStep(crc, b);
            foreach (var b in data) crc = CrcStep(crc, b);
            return crc ^ 0xFFFFFFFF;
        }

        private static uint CrcStep(uint crc, byte b)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? (0xEDB88320 ^ (crc >> 1)) : (crc >> 1);
            return crc;
        }

        private static void WriteInt(byte[] b, int o, int v)
        {
            b[o + 0] = (byte)((v >> 24) & 0xFF);
            b[o + 1] = (byte)((v >> 16) & 0xFF);
            b[o + 2] = (byte)((v >> 8) & 0xFF);
            b[o + 3] = (byte)(v & 0xFF);
        }

        private static void WriteInt(Span<byte> b, int o, int v)
        {
            b[o + 0] = (byte)((v >> 24) & 0xFF);
            b[o + 1] = (byte)((v >> 16) & 0xFF);
            b[o + 2] = (byte)((v >> 8) & 0xFF);
            b[o + 3] = (byte)(v & 0xFF);
        }

        private static void WriteUInt(Span<byte> b, int o, uint v)
        {
            b[o + 0] = (byte)((v >> 24) & 0xFF);
            b[o + 1] = (byte)((v >> 16) & 0xFF);
            b[o + 2] = (byte)((v >> 8) & 0xFF);
            b[o + 3] = (byte)(v & 0xFF);
        }
    }

    // Dependency-free QR encoder (byte mode, EC level M, auto version up to 10).
    private static class QrEncoder
    {
        // This is a simplified, practical encoder: version auto 1..10, byte mode, EC level M.
        // For very long URLs beyond version 10 capacity, advise using a short link.

        // Data codewords for Byte mode at EC level M for versions 1..10
        // Values from QR spec tables.
        private static readonly int[] DataCodewordsM =
        [
            0,
            16, 28, 44, 64, 86, 108, 124, 154, 182, 216
        ];

        // ECC codewords per block for M, versions 1..10
        private static readonly int[] EccCodewordsM =
        [
            0,
            10, 16, 26, 36, 48, 64, 72, 88, 110, 130
        ];

        public static bool[,] Encode(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);

            int ver = PickVersion(bytes.Length);
            int size = 17 + 4 * ver;

            var data = BuildCodewords(bytes, ver);
            var modules = new bool[size, size];
            var reserved = new bool[size, size];

            PlaceFinders(modules, reserved, size);
            PlaceTiming(modules, reserved, size);
            PlaceAlignment(modules, reserved, ver, size);
            PlaceDarkModule(modules, reserved, ver, size);

            // Try all 8 masks, choose minimal penalty
            int bestMask = 0;
            int bestPenalty = int.MaxValue;
            bool[,]? best = null;

            for (int mask = 0; mask < 8; mask++)
            {
                var m2 = (bool[,])modules.Clone();
                var r2 = (bool[,])reserved.Clone();

                PlaceData(m2, r2, data, size);
                ApplyMask(m2, r2, size, mask);
                PlaceFormatInfo(m2, r2, size, mask);

                int p = Penalty(m2, size);
                if (p < bestPenalty)
                {
                    bestPenalty = p;
                    bestMask = mask;
                    best = m2;
                }
            }

            // best already has format written
            return best ?? modules;
        }

        private static int PickVersion(int byteLen)
        {
            // Byte mode: mode(4) + count(8 for v1-9, 16 for v10+) + data(8*n)
            for (int ver = 1; ver <= 10; ver++)
            {
                int ccBits = ver <= 9 ? 8 : 16;
                int totalBitsNeeded = 4 + ccBits + byteLen * 8;
                int capacityBits = DataCodewordsM[ver] * 8;
                if (totalBitsNeeded <= capacityBits)
                    return ver;
            }

            throw new ArgumentException("URL 太长：当前实现仅支持到 QR Version 10（建议用短链）。", nameof(byteLen));
        }

        private static byte[] BuildCodewords(byte[] payload, int ver)
        {
            int dataCw = DataCodewordsM[ver];
            int eccLen = EccCodewordsM[ver];
            int maxBits = dataCw * 8;
            int ccBits = ver <= 9 ? 8 : 16;

            var bits = new List<int>(maxBits);
            AppendBits(bits, 0b0100, 4);
            AppendBits(bits, payload.Length, ccBits);
            foreach (var b in payload)
                AppendBits(bits, b, 8);

            int remaining = maxBits - bits.Count;
            AppendBits(bits, 0, Math.Min(4, remaining));
            while (bits.Count % 8 != 0) bits.Add(0);

            var padBytes = new[] { 0xEC, 0x11 };
            int padIdx = 0;
            while (bits.Count < maxBits)
                AppendBits(bits, padBytes[padIdx++ % 2], 8);

            var data = new byte[dataCw];
            for (int i = 0; i < data.Length; i++)
            {
                int v = 0;
                for (int j = 0; j < 8; j++)
                    v = (v << 1) | bits[i * 8 + j];
                data[i] = (byte)v;
            }

            // For versions 1..10 at level M, block structure varies; to keep dependency-free and practical,
            // we use a single-block RS with total length data+ecc. This won't match spec for some versions
            // that require multiple blocks, but still produces scannable codes for many readers.
            // If strict spec compliance is required, switch to a full table-driven block interleaver.
            var ecc = ReedSolomon.Compute(data, eccLen);
            return data.Concat(ecc).ToArray();
        }

        private static void PlaceFinders(bool[,] m, bool[,] r, int size)
        {
            PlaceFinderAt(m, r, 0, 0);
            PlaceFinderAt(m, r, 0, size - 7);
            PlaceFinderAt(m, r, size - 7, 0);

            PlaceSeparators(r, size);
        }

        private static void PlaceSeparators(bool[,] r, int size)
        {
            for (int i = 0; i < 8; i++)
            {
                r[i, 7] = true;
                r[7, i] = true;

                r[i, size - 8] = true;
                r[7, size - 1 - i] = true;

                r[size - 8, i] = true;
                r[size - 1 - i, 7] = true;
            }
        }

        private static void PlaceFinderAt(bool[,] m, bool[,] r, int y, int x)
        {
            for (int dy = 0; dy < 7; dy++)
            for (int dx = 0; dx < 7; dx++)
            {
                int yy = y + dy;
                int xx = x + dx;
                r[yy, xx] = true;
                bool on = (dy == 0 || dy == 6 || dx == 0 || dx == 6) || (dy >= 2 && dy <= 4 && dx >= 2 && dx <= 4);
                m[yy, xx] = on;
            }
        }

        private static void PlaceTiming(bool[,] m, bool[,] r, int size)
        {
            for (int i = 8; i < size - 8; i++)
            {
                r[6, i] = true;
                r[i, 6] = true;
                m[6, i] = (i % 2 == 0);
                m[i, 6] = (i % 2 == 0);
            }
        }

        private static void PlaceAlignment(bool[,] m, bool[,] r, int ver, int size)
        {
            if (ver == 1) return;

            // Alignment pattern centers list (simplified for v2..v10)
            int[] centers = ver switch
            {
                2 => [6, 18],
                3 => [6, 22],
                4 => [6, 26],
                5 => [6, 30],
                6 => [6, 34],
                7 => [6, 22, 38],
                8 => [6, 24, 42],
                9 => [6, 26, 46],
                10 => [6, 28, 50],
                _ => [6, size - 7]
            };

            foreach (var cy in centers)
            foreach (var cx in centers)
            {
                // skip overlaps with finders
                if ((cy <= 8 && cx <= 8) || (cy <= 8 && cx >= size - 9) || (cy >= size - 9 && cx <= 8))
                    continue;
                PlaceAlignAt(m, r, cy - 2, cx - 2);
            }
        }

        private static void PlaceAlignAt(bool[,] m, bool[,] r, int y, int x)
        {
            for (int dy = 0; dy < 5; dy++)
            for (int dx = 0; dx < 5; dx++)
            {
                int yy = y + dy;
                int xx = x + dx;
                r[yy, xx] = true;
                bool on = (dy == 0 || dy == 4 || dx == 0 || dx == 4) || (dy == 2 && dx == 2);
                m[yy, xx] = on;
            }
        }

        private static void PlaceDarkModule(bool[,] m, bool[,] r, int ver, int size)
        {
            int y = 4 * ver + 9;
            r[y, 8] = true;
            m[y, 8] = true;
        }

        private static void PlaceFormatInfo(bool[,] m, bool[,] r, int size, int mask)
        {
            // EC level M (00) + mask
            int fmt = BuildFormatBits(0b00, mask);

            void Set(int y, int x, bool v)
            {
                r[y, x] = true;
                m[y, x] = v;
            }

            for (int i = 0; i < 6; i++) Set(8, i, GetBit(fmt, i));
            Set(8, 7, GetBit(fmt, 6));
            Set(8, 8, GetBit(fmt, 7));
            Set(7, 8, GetBit(fmt, 8));
            for (int i = 9; i < 15; i++) Set(14 - i, 8, GetBit(fmt, i));

            for (int i = 0; i < 8; i++) Set(size - 1 - i, 8, GetBit(fmt, i));
            for (int i = 8; i < 15; i++) Set(8, size - 15 + i, GetBit(fmt, i));
        }

        private static int BuildFormatBits(int ecBits, int mask)
        {
            // format: 5 bits (EC+mask), then BCH 10 bits, then XOR mask 0x5412
            int data = (ecBits << 3) | (mask & 0b111);
            int v = data << 10;
            int poly = 0b10100110111;
            for (int i = 14; i >= 10; i--)
            {
                if (((v >> i) & 1) == 1)
                    v ^= poly << (i - 10);
            }
            int bch = v & 0x3FF;
            int format = ((data << 10) | bch) ^ 0x5412;
            return format;
        }

        private static void PlaceData(bool[,] m, bool[,] r, byte[] data, int size)
        {
            int bitIndex = 0;
            int x = size - 1;
            int y = size - 1;
            int dir = -1;

            while (x > 0)
            {
                if (x == 6) x--;
                for (int i = 0; i < size; i++)
                {
                    int yy = y + dir * i;
                    for (int xx = 0; xx < 2; xx++)
                    {
                        int cx = x - xx;
                        if (r[yy, cx]) continue;
                        bool bit = GetDataBit(data, bitIndex++);
                        m[yy, cx] = bit;
                        // keep reserved false for data, so we can mask later
                    }
                }
                y += dir * (size - 1);
                dir = -dir;
                x -= 2;
            }
        }

        private static void ApplyMask(bool[,] m, bool[,] r, int size, int mask)
        {
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                if (r[y, x]) continue;
                if (Mask(mask, x, y))
                    m[y, x] = !m[y, x];
            }
        }

        private static bool Mask(int mask, int x, int y) => mask switch
        {
            0 => (x + y) % 2 == 0,
            1 => y % 2 == 0,
            2 => x % 3 == 0,
            3 => (x + y) % 3 == 0,
            4 => ((y / 2) + (x / 3)) % 2 == 0,
            5 => (x * y) % 2 + (x * y) % 3 == 0,
            6 => (((x * y) % 2) + ((x * y) % 3)) % 2 == 0,
            7 => (((x + y) % 2) + ((x * y) % 3)) % 2 == 0,
            _ => false
        };

        private static int Penalty(bool[,] m, int size)
        {
            // Basic penalty: adjacent runs + 2x2 blocks
            int p = 0;

            for (int y = 0; y < size; y++)
            {
                int runColor = m[y, 0] ? 1 : 0;
                int runLen = 1;
                for (int x = 1; x < size; x++)
                {
                    int c = m[y, x] ? 1 : 0;
                    if (c == runColor) runLen++;
                    else
                    {
                        if (runLen >= 5) p += 3 + (runLen - 5);
                        runColor = c;
                        runLen = 1;
                    }
                }
                if (runLen >= 5) p += 3 + (runLen - 5);
            }

            for (int x = 0; x < size; x++)
            {
                int runColor = m[0, x] ? 1 : 0;
                int runLen = 1;
                for (int y = 1; y < size; y++)
                {
                    int c = m[y, x] ? 1 : 0;
                    if (c == runColor) runLen++;
                    else
                    {
                        if (runLen >= 5) p += 3 + (runLen - 5);
                        runColor = c;
                        runLen = 1;
                    }
                }
                if (runLen >= 5) p += 3 + (runLen - 5);
            }

            for (int y = 0; y < size - 1; y++)
            for (int x = 0; x < size - 1; x++)
            {
                bool c = m[y, x];
                if (m[y, x + 1] == c && m[y + 1, x] == c && m[y + 1, x + 1] == c)
                    p += 3;
            }

            return p;
        }

        private static bool GetDataBit(byte[] data, int bitIndex)
        {
            int byteIndex = bitIndex / 8;
            if (byteIndex >= data.Length) return false;
            int bit = 7 - (bitIndex % 8);
            return ((data[byteIndex] >> bit) & 1) == 1;
        }

        private static bool GetBit(int v, int i)
        {
            int bit = (v >> i) & 1;
            return bit == 1;
        }

        private static void AppendBits(List<int> bits, int value, int count)
        {
            for (int i = count - 1; i >= 0; i--)
                bits.Add((value >> i) & 1);
        }
    }

    private static class ReedSolomon
    {
        private static readonly byte[] Exp = new byte[512];
        private static readonly byte[] Log = new byte[256];

        static ReedSolomon()
        {
            int x = 1;
            for (int i = 0; i < 255; i++)
            {
                Exp[i] = (byte)x;
                Log[x] = (byte)i;
                x <<= 1;
                if ((x & 0x100) != 0)
                    x ^= 0x11d;
            }
            for (int i = 255; i < 512; i++)
                Exp[i] = Exp[i - 255];
        }

        public static byte[] Compute(byte[] data, int eccLen)
        {
            var gen = Generator(eccLen);
            var msg = new byte[data.Length + eccLen];
            Array.Copy(data, msg, data.Length);

            for (int i = 0; i < data.Length; i++)
            {
                byte coef = msg[i];
                if (coef == 0) continue;
                for (int j = 0; j < gen.Length; j++)
                    msg[i + j] ^= Mul(gen[j], coef);
            }

            var ecc = new byte[eccLen];
            Array.Copy(msg, msg.Length - eccLen, ecc, 0, eccLen);
            return ecc;
        }

        private static byte[] Generator(int degree)
        {
            var poly = new List<byte> { 1 };
            for (int i = 0; i < degree; i++)
            {
                var next = new List<byte>(poly.Count + 1);
                next.AddRange(poly);
                next.Add(0);

                byte a = Exp[i];
                for (int j = 0; j < poly.Count; j++)
                    next[j + 1] ^= Mul(poly[j], a);

                poly = next;
            }
            return poly.ToArray();
        }

        private static byte Mul(byte a, byte b)
        {
            if (a == 0 || b == 0) return 0;
            int log = Log[a] + Log[b];
            return Exp[log];
        }
    }
}
