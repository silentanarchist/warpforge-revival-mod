using System;
using System.IO;
using System.IO.Compression;

namespace WarpforgeRevival
{
    /// <summary>
    /// Picture files written by the mod's own code. Unity's encoders (EncodeToPNG / EncodeToJPG) are not
    /// used: on a Pixel 6 Pro they crashed the loader's .NET. Pixels come in as rows of bytes, top row
    /// first: RGB (3 bytes a pixel) for JPEG, gray+alpha (2) or RGBA (4) for PNG.
    /// </summary>
    internal static class ImageEncoders
    {
        // ------------------------------------------------------------------ JPEG (baseline, 4:2:0)
        private static readonly int[] ZigZag =
        {
             0, 1, 5, 6,14,15,27,28,  2, 4, 7,13,16,26,29,42,  3, 8,12,17,25,30,41,43,  9,11,18,24,31,40,44,53,
            10,19,23,32,39,45,52,54, 20,22,33,38,46,51,55,60, 21,34,37,47,50,56,59,61, 35,36,48,49,57,58,62,63,
        };
        private static readonly int[] LumQ =
        {
            16,11,10,16,24,40,51,61, 12,12,14,19,26,58,60,55, 14,13,16,24,40,57,69,56, 14,17,22,29,51,87,80,62,
            18,22,37,56,68,109,103,77, 24,35,55,64,81,104,113,92, 49,64,78,87,103,121,120,101, 72,92,95,98,112,100,103,99,
        };
        private static readonly int[] ChromQ =
        {
            17,18,24,47,99,99,99,99, 18,21,26,66,99,99,99,99, 24,26,56,99,99,99,99,99, 47,66,99,99,99,99,99,99,
            99,99,99,99,99,99,99,99, 99,99,99,99,99,99,99,99, 99,99,99,99,99,99,99,99, 99,99,99,99,99,99,99,99,
        };
        // Huffman tables of the JPEG standard (Annex K): code counts per length 1..16, then the values
        private static readonly byte[] DcLumCounts = { 0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0 };
        private static readonly byte[] DcLumValues = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
        private static readonly byte[] DcChromCounts = { 0, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0 };
        private static readonly byte[] DcChromValues = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
        private static readonly byte[] AcLumCounts = { 0, 2, 1, 3, 3, 2, 4, 3, 5, 5, 4, 4, 0, 0, 1, 0x7d };
        private static readonly byte[] AcLumValues =
        {
            0x01,0x02,0x03,0x00,0x04,0x11,0x05,0x12,0x21,0x31,0x41,0x06,0x13,0x51,0x61,0x07,0x22,0x71,0x14,0x32,0x81,0x91,0xa1,0x08,
            0x23,0x42,0xb1,0xc1,0x15,0x52,0xd1,0xf0,0x24,0x33,0x62,0x72,0x82,0x09,0x0a,0x16,0x17,0x18,0x19,0x1a,0x25,0x26,0x27,0x28,
            0x29,0x2a,0x34,0x35,0x36,0x37,0x38,0x39,0x3a,0x43,0x44,0x45,0x46,0x47,0x48,0x49,0x4a,0x53,0x54,0x55,0x56,0x57,0x58,0x59,
            0x5a,0x63,0x64,0x65,0x66,0x67,0x68,0x69,0x6a,0x73,0x74,0x75,0x76,0x77,0x78,0x79,0x7a,0x83,0x84,0x85,0x86,0x87,0x88,0x89,
            0x8a,0x92,0x93,0x94,0x95,0x96,0x97,0x98,0x99,0x9a,0xa2,0xa3,0xa4,0xa5,0xa6,0xa7,0xa8,0xa9,0xaa,0xb2,0xb3,0xb4,0xb5,0xb6,
            0xb7,0xb8,0xb9,0xba,0xc2,0xc3,0xc4,0xc5,0xc6,0xc7,0xc8,0xc9,0xca,0xd2,0xd3,0xd4,0xd5,0xd6,0xd7,0xd8,0xd9,0xda,0xe1,0xe2,
            0xe3,0xe4,0xe5,0xe6,0xe7,0xe8,0xe9,0xea,0xf1,0xf2,0xf3,0xf4,0xf5,0xf6,0xf7,0xf8,0xf9,0xfa,
        };
        private static readonly byte[] AcChromCounts = { 0, 2, 1, 2, 4, 4, 3, 4, 7, 5, 4, 4, 0, 1, 2, 0x77 };
        private static readonly byte[] AcChromValues =
        {
            0x00,0x01,0x02,0x03,0x11,0x04,0x05,0x21,0x31,0x06,0x12,0x41,0x51,0x07,0x61,0x71,0x13,0x22,0x32,0x81,0x08,0x14,0x42,0x91,
            0xa1,0xb1,0xc1,0x09,0x23,0x33,0x52,0xf0,0x15,0x62,0x72,0xd1,0x0a,0x16,0x24,0x34,0xe1,0x25,0xf1,0x17,0x18,0x19,0x1a,0x26,
            0x27,0x28,0x29,0x2a,0x35,0x36,0x37,0x38,0x39,0x3a,0x43,0x44,0x45,0x46,0x47,0x48,0x49,0x4a,0x53,0x54,0x55,0x56,0x57,0x58,
            0x59,0x5a,0x63,0x64,0x65,0x66,0x67,0x68,0x69,0x6a,0x73,0x74,0x75,0x76,0x77,0x78,0x79,0x7a,0x82,0x83,0x84,0x85,0x86,0x87,
            0x88,0x89,0x8a,0x92,0x93,0x94,0x95,0x96,0x97,0x98,0x99,0x9a,0xa2,0xa3,0xa4,0xa5,0xa6,0xa7,0xa8,0xa9,0xaa,0xb2,0xb3,0xb4,
            0xb5,0xb6,0xb7,0xb8,0xb9,0xba,0xc2,0xc3,0xc4,0xc5,0xc6,0xc7,0xc8,0xc9,0xca,0xd2,0xd3,0xd4,0xd5,0xd6,0xd7,0xd8,0xd9,0xda,
            0xe2,0xe3,0xe4,0xe5,0xe6,0xe7,0xe8,0xe9,0xea,0xf2,0xf3,0xf4,0xf5,0xf6,0xf7,0xf8,0xf9,0xfa,
        };
        private static readonly double[] Aasf = { 1.0, 1.387039845, 1.306562965, 1.175875602, 1.0, 0.785694958, 0.541196100, 0.275899379 };

        private sealed class Huff
        {
            public readonly int[] Code = new int[256], Len = new int[256];
            public Huff(byte[] counts, byte[] values)
            {
                int code = 0, pos = 0;
                for (int k = 0; k < 16; k++)
                {
                    for (int j = 0; j < counts[k]; j++) { Code[values[pos]] = code; Len[values[pos]] = k + 1; pos++; code++; }
                    code <<= 1;
                }
            }
        }
        private static readonly Huff HDcL = new Huff(DcLumCounts, DcLumValues), HAcL = new Huff(AcLumCounts, AcLumValues),
                                     HDcC = new Huff(DcChromCounts, DcChromValues), HAcC = new Huff(AcChromCounts, AcChromValues);

        private sealed class Bits
        {
            private readonly Stream s; private int acc, n;
            public Bits(Stream s) { this.s = s; }
            public void Put(int value, int length)
            {
                for (int i = length - 1; i >= 0; i--)
                {
                    acc = (acc << 1) | ((value >> i) & 1);
                    if (++n == 8) { s.WriteByte((byte)acc); if (acc == 0xFF) s.WriteByte(0); acc = 0; n = 0; }
                }
            }
            public void Flush() { if (n > 0) Put((1 << (8 - n)) - 1, 8 - n); }
        }

        /// <summary>RGB rows, top row first, as a baseline JPEG (quality 1-100).</summary>
        public static byte[] Jpeg(byte[] rgb, int width, int height, int quality = 85)
        {
            quality = Math.Max(1, Math.Min(100, quality));
            int sf = quality < 50 ? 5000 / quality : 200 - quality * 2;
            var qY = new byte[64]; var qC = new byte[64];
            var fY = new double[64]; var fC = new double[64];
            for (int i = 0; i < 64; i++)
            {
                qY[ZigZag[i]] = (byte)Math.Max(1, Math.Min(255, (LumQ[i] * sf + 50) / 100));
                qC[ZigZag[i]] = (byte)Math.Max(1, Math.Min(255, (ChromQ[i] * sf + 50) / 100));
            }
            for (int r = 0, i = 0; r < 8; r++)
                for (int c = 0; c < 8; c++, i++)
                {
                    fY[i] = 1.0 / (qY[ZigZag[i]] * Aasf[r] * Aasf[c] * 8.0);
                    fC[i] = 1.0 / (qC[ZigZag[i]] * Aasf[r] * Aasf[c] * 8.0);
                }

            var ms = new MemoryStream(width * height / 4 + 1024);
            void W(params int[] b) { foreach (int x in b) ms.WriteByte((byte)x); }
            void W16(int v) { ms.WriteByte((byte)(v >> 8)); ms.WriteByte((byte)v); }
            W(0xFF, 0xD8);                                                         // start
            W(0xFF, 0xE0); W16(16); W('J', 'F', 'I', 'F', 0, 1, 1, 0); W16(1); W16(1); W(0, 0);
            W(0xFF, 0xDB); W16(132); W(0); ms.Write(qY, 0, 64); W(1); ms.Write(qC, 0, 64);
            W(0xFF, 0xC0); W16(17); W(8); W16(height); W16(width); W(3, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1);
            W(0xFF, 0xC4); W16(2 + 4 * 17 + DcLumValues.Length + AcLumValues.Length + DcChromValues.Length + AcChromValues.Length);
            foreach (var (id, counts, values) in new[] { (0x00, DcLumCounts, DcLumValues), (0x10, AcLumCounts, AcLumValues),
                                                         (0x01, DcChromCounts, DcChromValues), (0x11, AcChromCounts, AcChromValues) })
            { W(id); ms.Write(counts, 0, 16); ms.Write(values, 0, values.Length); }
            W(0xFF, 0xDA); W16(12); W(3, 1, 0x00, 2, 0x11, 3, 0x11, 0, 0x3F, 0);

            var bits = new Bits(ms);
            var y = new double[4][]; for (int k = 0; k < 4; k++) y[k] = new double[64];
            var cb = new double[64]; var cr = new double[64];
            int dcY = 0, dcCb = 0, dcCr = 0;
            for (int my = 0; my < height; my += 16)
                for (int mx = 0; mx < width; mx += 16)
                {
                    Array.Clear(cb, 0, 64); Array.Clear(cr, 0, 64);
                    for (int py = 0; py < 16; py++)
                        for (int px = 0; px < 16; px++)
                        {
                            int sx = Math.Min(mx + px, width - 1), sy = Math.Min(my + py, height - 1), o = (sy * width + sx) * 3;
                            double R = rgb[o], G = rgb[o + 1], B = rgb[o + 2];
                            y[(py >> 3) * 2 + (px >> 3)][(py & 7) * 8 + (px & 7)] = 0.299 * R + 0.587 * G + 0.114 * B - 128.0;
                            int ci = (py >> 1) * 8 + (px >> 1);
                            cb[ci] += (-0.16874 * R - 0.33126 * G + 0.5 * B) * 0.25;
                            cr[ci] += (0.5 * R - 0.41869 * G - 0.08131 * B) * 0.25;
                        }
                    for (int k = 0; k < 4; k++) dcY = Block(bits, y[k], fY, dcY, HDcL, HAcL);
                    dcCb = Block(bits, cb, fC, dcCb, HDcC, HAcC);
                    dcCr = Block(bits, cr, fC, dcCr, HDcC, HAcC);
                }
            bits.Flush();
            W(0xFF, 0xD9);
            return ms.ToArray();
        }

        private static void Dct(double[] d)
        {
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < 8; i++)
                {
                    int s = pass == 0 ? 1 : 8, b = pass == 0 ? i * 8 : i;      // rows first, then columns
                    double d0 = d[b], d1 = d[b + s], d2 = d[b + 2 * s], d3 = d[b + 3 * s], d4 = d[b + 4 * s], d5 = d[b + 5 * s], d6 = d[b + 6 * s], d7 = d[b + 7 * s];
                    double t0 = d0 + d7, t7 = d0 - d7, t1 = d1 + d6, t6 = d1 - d6, t2 = d2 + d5, t5 = d2 - d5, t3 = d3 + d4, t4 = d3 - d4;
                    double t10 = t0 + t3, t13 = t0 - t3, t11 = t1 + t2, t12 = t1 - t2;
                    d[b] = t10 + t11; d[b + 4 * s] = t10 - t11;
                    double z1 = (t12 + t13) * 0.707106781;
                    d[b + 2 * s] = t13 + z1; d[b + 6 * s] = t13 - z1;
                    t10 = t4 + t5; t11 = t5 + t6; t12 = t6 + t7;
                    double z5 = (t10 - t12) * 0.382683433, z2 = 0.541196100 * t10 + z5, z4 = 1.306562965 * t12 + z5, z3 = t11 * 0.707106781;
                    double z11 = t7 + z3, z13 = t7 - z3;
                    d[b + 5 * s] = z13 + z2; d[b + 3 * s] = z13 - z2; d[b + s] = z11 + z4; d[b + 7 * s] = z11 - z4;
                }
        }

        private static int Category(int v) { v = Math.Abs(v); int n = 0; while (v > 0) { n++; v >>= 1; } return n; }
        private static int Amplitude(int v, int cat) => v >= 0 ? v : v + (1 << cat) - 1;

        private static int Block(Bits bits, double[] data, double[] f, int prevDc, Huff dc, Huff ac)
        {
            var d = (double[])data.Clone();
            Dct(d);
            var zz = new int[64];
            for (int i = 0; i < 64; i++) zz[ZigZag[i]] = (int)Math.Round(d[i] * f[i], MidpointRounding.AwayFromZero);
            int diff = zz[0] - prevDc, cat = Category(diff);
            bits.Put(dc.Code[cat], dc.Len[cat]);
            if (cat > 0) bits.Put(Amplitude(diff, cat), cat);
            int end = 63;
            while (end > 0 && zz[end] == 0) end--;
            for (int i = 1; i <= end; i++)
            {
                int zeros = 0;
                while (zz[i] == 0) { zeros++; i++; }
                while (zeros >= 16) { bits.Put(ac.Code[0xF0], ac.Len[0xF0]); zeros -= 16; }
                int c = Category(zz[i]), sym = (zeros << 4) | c;
                bits.Put(ac.Code[sym], ac.Len[sym]);
                bits.Put(Amplitude(zz[i], c), c);
            }
            if (end != 63) bits.Put(ac.Code[0], ac.Len[0]);
            return zz[0];
        }

        // ------------------------------------------------------------------ PNG
        private static readonly uint[] Crc = MakeCrc();
        private static uint[] MakeCrc()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; t[n] = c; }
            return t;
        }

        /// <summary>Rows of gray+alpha (channels 2) or RGBA (channels 4) pixels, top row first, as a PNG.</summary>
        public static byte[] Png(byte[] pixels, int width, int height, int channels)
        {
            int colorType = channels == 2 ? 4 : channels == 4 ? 6 : channels == 3 ? 2 : 0;
            int row = width * channels;
            var raw = new MemoryStream();
            using (var z = new ZLibStream(raw, CompressionLevel.Optimal, true))
            {
                var line = new byte[row + 1];
                for (int y = 0; y < height; y++)
                {
                    // filter "Up": each byte minus the one above (packs smooth pictures far better than no filter)
                    line[0] = (byte)(y == 0 ? 0 : 2);
                    int o = y * row;
                    for (int i = 0; i < row; i++) line[i + 1] = (byte)(pixels[o + i] - (y == 0 ? 0 : pixels[o - row + i]));
                    z.Write(line, 0, line.Length);
                }
            }
            var ms = new MemoryStream();
            ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
            void Chunk(string type, byte[] data)
            {
                var head = new byte[] { (byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length,
                                        (byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3] };
                ms.Write(head, 0, 8); ms.Write(data, 0, data.Length);
                uint c = 0xFFFFFFFF;
                for (int i = 4; i < 8; i++) c = Crc[(c ^ head[i]) & 0xFF] ^ (c >> 8);
                foreach (byte b in data) c = Crc[(c ^ b) & 0xFF] ^ (c >> 8);
                c ^= 0xFFFFFFFF;
                ms.Write(new[] { (byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c }, 0, 4);
            }
            Chunk("IHDR", new byte[] { (byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width,
                                       (byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height, 8, (byte)colorType, 0, 0, 0 });
            Chunk("IDAT", raw.ToArray());
            Chunk("IEND", Array.Empty<byte>());
            return ms.ToArray();
        }
    }
}
