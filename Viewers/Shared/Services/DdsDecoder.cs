using SkiaSharp;
using System.Runtime.InteropServices;

namespace MauiMultimedia.Viewers.Shared.Services;

/// <summary>
/// DDS texture decoder (SkiaSharp cannot decode DDS). Extracted from the Image
/// viewer into the shared library so Model3D (and any other viewer) can decode
/// DDS textures without taking a dependency on the Image viewer. Pure SkiaSharp
/// + BCL, no MAUI/platform code.
/// </summary>
public static class DdsDecoder
{
        // ═══════════ DDS Decoder (SkiaSharp can't decode DDS) ═══════════

        private const uint DdsMagic = 0x20534444; // "DDS "
        private const uint DdpfFourCC = 0x00000004;
        private const uint DdpfRgb = 0x00000040;
        private const uint DdpfAlphaPixels = 0x00000001;

        /// <summary>
        /// Decodes a DDS file to a PNG data URI. Handles DXT1/BC1, DXT3/BC2, DXT5/BC3,
        /// BC4, BC5, BC7 (via DX10 header), DXT2/DXT4 (as DXT3/5), and uncompressed
        /// 32-bit RGBA/BGRA. Returns null on failure.
        /// </summary>
        public static (string? dataUri, int width, int height) DecodeDds(string filePath)
        {
            try
            {
                var data = File.ReadAllBytes(filePath);
                if (data.Length < 128) return (null, 0, 0);
                if (BitConverter.ToUInt32(data, 0) != DdsMagic) return (null, 0, 0);

                int height = (int)BitConverter.ToUInt32(data, 12);
                int width = (int)BitConverter.ToUInt32(data, 16);
                uint pfFlags = BitConverter.ToUInt32(data, 80);
                uint fourCC = BitConverter.ToUInt32(data, 84);
                int bitCount = (int)BitConverter.ToUInt32(data, 88);

                if (width <= 0 || height <= 0 || width > 16384 || height > 16384)
                    return (null, 0, 0);

                byte[] rgba = new byte[width * height * 4];

                if ((pfFlags & DdpfFourCC) != 0)
                {
                    // BC-compressed formats
                    string cc = new string(new char[] {
                        (char)(fourCC & 0xFF), (char)((fourCC >> 8) & 0xFF),
                        (char)((fourCC >> 16) & 0xFF), (char)((fourCC >> 24) & 0xFF) });

                    int dataOff = 128;
                    bool isDx10 = cc == "DX10";
                    if (isDx10)
                    {
                        // DX10 extended header: additional 20 bytes at offset 128
                        int dxgiFormat = (int)BitConverter.ToUInt32(data, 128);
                        dataOff = 148;
                        switch (dxgiFormat)
                        {
                            case 71: DecodeDxt1(data, dataOff, width, height, rgba); break;  // BC1
                            case 74: DecodeDxt3(data, dataOff, width, height, rgba); break;  // BC2
                            case 77: DecodeDxt5(data, dataOff, width, height, rgba); break;  // BC3
                            case 80: DecodeBc4(data, dataOff, width, height, rgba); break;   // BC4
                            case 83: DecodeBc5(data, dataOff, width, height, rgba); break;   // BC5
                            case 98: case 99: DecodeBc7(data, dataOff, width, height, rgba); break;
                            default: return (null, 0, 0); // BC6H etc. unsupported
                        }
                    }
                    else
                    {
                        switch (cc)
                        {
                            case "DXT1": DecodeDxt1(data, dataOff, width, height, rgba); break;
                            case "DXT2": DecodeDxt3(data, dataOff, width, height, rgba); break; // DXT2 ≈ DXT3
                            case "DXT3": DecodeDxt3(data, dataOff, width, height, rgba); break;
                            case "DXT4": DecodeDxt5(data, dataOff, width, height, rgba); break; // DXT4 ≈ DXT5
                            case "DXT5": DecodeDxt5(data, dataOff, width, height, rgba); break;
                            default: return (null, 0, 0);
                        }
                    }

                    // Flip vertically — DDS stores rows bottom-to-top for BC
                    FlipVertical(rgba, width, height);
                }
                else if ((pfFlags & DdpfRgb) != 0 && bitCount == 32)
                {
                    // Uncompressed 32-bit RGBA/BGRA
                    int dataOff = 128;
                    uint rMask = BitConverter.ToUInt32(data, 92);
                    uint gMask = BitConverter.ToUInt32(data, 96);
                    uint bMask = BitConverter.ToUInt32(data, 100);
                    uint aMask = BitConverter.ToUInt32(data, 104);
                    bool isBgra = (bMask == 0x000000FF && rMask == 0x00FF0000);
                    bool isRgba = (rMask == 0x000000FF && bMask == 0x00FF0000);

                    if (!isBgra && !isRgba) return (null, 0, 0);

                    int srcStride = width * 4;
                    for (int y = 0; y < height; y++)
                    {
                        int srcRow = dataOff + (height - 1 - y) * srcStride;
                        int dstRow = y * width * 4;
                        if (isBgra)
                        {
                            for (int x = 0; x < width; x++)
                            {
                                int si = srcRow + x * 4;
                                rgba[dstRow + x * 4] = data[si + 2];     // R
                                rgba[dstRow + x * 4 + 1] = data[si + 1]; // G
                                rgba[dstRow + x * 4 + 2] = data[si];     // B
                                rgba[dstRow + x * 4 + 3] = data[si + 3]; // A
                            }
                        }
                        else
                        {
                            Buffer.BlockCopy(data, srcRow, rgba, dstRow, srcStride);
                        }
                    }
                }
                else
                {
                    return (null, 0, 0); // unsupported format
                }

                // Encode to PNG via SkiaSharp
                using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
                System.Runtime.InteropServices.Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
                using var image = SKImage.FromBitmap(bitmap);
                using var pngData = image.Encode(SKEncodedImageFormat.Png, 100);
                var dataUri = "data:image/png;base64," + Convert.ToBase64String(pngData.ToArray());
                return (dataUri, width, height);
            }
            catch
            {
                return (null, 0, 0);
            }
        }

        private static void DecodeDxt1(byte[] src, int off, int w, int h, byte[] dst)
        {
            int bw = Math.Max(1, (w + 3) / 4);
            int bh = Math.Max(1, (h + 3) / 4);
            var block = new byte[4 * 4 * 4];
            for (int by = 0; by < bh; by++)
            {
                for (int bx = 0; bx < bw; bx++)
                {
                    DecodeDxt1Block(src, off + (by * bw + bx) * 8, block);
                    int px = bx * 4, py = by * 4;
                    for (int yy = 0; yy < 4 && py + yy < h; yy++)
                        for (int xx = 0; xx < 4 && px + xx < w; xx++)
                        {
                            int di = ((py + yy) * w + (px + xx)) * 4;
                            int si = (yy * 4 + xx) * 4;
                            dst[di] = block[si]; dst[di + 1] = block[si + 1];
                            dst[di + 2] = block[si + 2]; dst[di + 3] = block[si + 3];
                        }
                }
            }
        }

        private static void DecodeDxt1Block(byte[] src, int off, byte[] block)
        {
            ushort c0 = (ushort)(src[off] | (src[off + 1] << 8));
            ushort c1 = (ushort)(src[off + 2] | (src[off + 3] << 8));
            uint bits = (uint)(src[off + 4] | (src[off + 5] << 8) | (src[off + 6] << 16) | (src[off + 7] << 24));

            byte r0, g0, b0, r1, g1, b1;
            Rgb565(c0, out r0, out g0, out b0);
            Rgb565(c1, out r1, out g1, out b1);

            byte[] colors = new byte[4 * 4];
            colors[0] = r0; colors[1] = g0; colors[2] = b0; colors[3] = 255;
            colors[4] = r1; colors[5] = g1; colors[6] = b1; colors[7] = 255;

            if (c0 > c1)
            {
                colors[8] = (byte)((2 * r0 + r1) / 3); colors[9] = (byte)((2 * g0 + g1) / 3); colors[10] = (byte)((2 * b0 + b1) / 3); colors[11] = 255;
                colors[12] = (byte)((r0 + 2 * r1) / 3); colors[13] = (byte)((g0 + 2 * g1) / 3); colors[14] = (byte)((b0 + 2 * b1) / 3); colors[15] = 255;
            }
            else
            {
                colors[8] = (byte)((r0 + r1) / 2); colors[9] = (byte)((g0 + g1) / 2); colors[10] = (byte)((b0 + b1) / 2); colors[11] = 255;
                colors[12] = 0; colors[13] = 0; colors[14] = 0; colors[15] = 0;
            }

            for (int i = 0; i < 16; i++)
            {
                int idx = (int)((bits >> (i * 2)) & 3);
                int di = (i / 4 * 4 + i % 4) * 4;
                block[di] = colors[idx * 4];
                block[di + 1] = colors[idx * 4 + 1];
                block[di + 2] = colors[idx * 4 + 2];
                block[di + 3] = colors[idx * 4 + 3];
            }
        }

        private static void DecodeDxt3(byte[] src, int off, int w, int h, byte[] dst)
        {
            int bw = Math.Max(1, (w + 3) / 4);
            int bh = Math.Max(1, (h + 3) / 4);
            var block = new byte[4 * 4 * 4];
            for (int by = 0; by < bh; by++)
            {
                for (int bx = 0; bx < bw; bx++)
                {
                    int blockOff = off + (by * bw + bx) * 16;
                    // Alpha (8 bytes of 4-bit alpha)
                    ulong alpha = (ulong)src[blockOff] | ((ulong)src[blockOff + 1] << 8) |
                                  ((ulong)src[blockOff + 2] << 16) | ((ulong)src[blockOff + 3] << 24) |
                                  ((ulong)src[blockOff + 4] << 32) | ((ulong)src[blockOff + 5] << 40) |
                                  ((ulong)src[blockOff + 6] << 48) | ((ulong)src[blockOff + 7] << 56);
                    DecodeDxt1Block(src, blockOff + 8, block);
                    // Apply alpha
                    for (int i = 0; i < 16; i++)
                    {
                        int aVal = (int)((alpha >> (i * 4)) & 0xF);
                        block[i * 4 + 3] = (byte)(aVal | (aVal << 4));
                    }

                    int px = bx * 4, py = by * 4;
                    for (int yy = 0; yy < 4 && py + yy < h; yy++)
                        for (int xx = 0; xx < 4 && px + xx < w; xx++)
                        {
                            int di = ((py + yy) * w + (px + xx)) * 4;
                            int si = (yy * 4 + xx) * 4;
                            dst[di] = block[si]; dst[di + 1] = block[si + 1];
                            dst[di + 2] = block[si + 2]; dst[di + 3] = block[si + 3];
                        }
                }
            }
        }

        private static void DecodeDxt5(byte[] src, int off, int w, int h, byte[] dst)
        {
            int bw = Math.Max(1, (w + 3) / 4);
            int bh = Math.Max(1, (h + 3) / 4);
            var block = new byte[4 * 4 * 4];
            for (int by = 0; by < bh; by++)
            {
                for (int bx = 0; bx < bw; bx++)
                {
                    int blockOff = off + (by * bw + bx) * 16;
                    byte a0 = src[blockOff];
                    byte a1 = src[blockOff + 1];
                    ulong aBits = (ulong)src[blockOff + 2] | ((ulong)src[blockOff + 3] << 8) |
                                  ((ulong)src[blockOff + 4] << 16) | ((ulong)src[blockOff + 5] << 24) |
                                  ((ulong)src[blockOff + 6] << 32) | ((ulong)src[blockOff + 7] << 40);

                    DecodeDxt1Block(src, blockOff + 8, block);

                    // Alpha decoding
                    byte[] alphas = new byte[8];
                    alphas[0] = a0; alphas[1] = a1;
                    if (a0 > a1)
                    {
                        for (int i = 2; i < 8; i++)
                            alphas[i] = (byte)(((8 - i) * a0 + (i - 1) * a1) / 7);
                    }
                    else
                    {
                        for (int i = 2; i < 6; i++)
                            alphas[i] = (byte)(((6 - i) * a0 + (i - 1) * a1) / 5);
                        alphas[6] = 0; alphas[7] = 255;
                    }

                    for (int i = 0; i < 16; i++)
                    {
                        int aIdx = (int)((aBits >> (i * 3)) & 7);
                        block[i * 4 + 3] = alphas[aIdx];
                    }

                    int px = bx * 4, py = by * 4;
                    for (int yy = 0; yy < 4 && py + yy < h; yy++)
                        for (int xx = 0; xx < 4 && px + xx < w; xx++)
                        {
                            int di = ((py + yy) * w + (px + xx)) * 4;
                            int si = (yy * 4 + xx) * 4;
                            dst[di] = block[si]; dst[di + 1] = block[si + 1];
                            dst[di + 2] = block[si + 2]; dst[di + 3] = block[si + 3];
                        }
                }
            }
        }

        // ═══════════ BC4 (single-channel, 8 bytes/block) ═══════════

        private static void DecodeBc4(byte[] src, int off, int w, int h, byte[] dst)
        {
            // BC4 = one channel (red) using the same 3-bit index algorithm as DXT5 alpha
            int bw = Math.Max(1, (w + 3) / 4);
            int bh = Math.Max(1, (h + 3) / 4);
            var block = new byte[4 * 4];
            for (int by = 0; by < bh; by++)
            {
                for (int bx = 0; bx < bw; bx++)
                {
                    int blockOff = off + (by * bw + bx) * 8;
                    byte r0 = src[blockOff], r1 = src[blockOff + 1];
                    ulong bits = (ulong)src[blockOff + 2] | ((ulong)src[blockOff + 3] << 8) |
                                 ((ulong)src[blockOff + 4] << 16) | ((ulong)src[blockOff + 5] << 24) |
                                 ((ulong)src[blockOff + 6] << 32) | ((ulong)src[blockOff + 7] << 40);

                    byte[] vals = new byte[8];
                    vals[0] = r0; vals[1] = r1;
                    if (r0 > r1)
                        for (int i = 2; i < 8; i++)
                            vals[i] = (byte)(((8 - i) * r0 + (i - 1) * r1) / 7);
                    else
                    {
                        for (int i = 2; i < 6; i++)
                            vals[i] = (byte)(((6 - i) * r0 + (i - 1) * r1) / 5);
                        vals[6] = 0; vals[7] = 255;
                    }

                    for (int i = 0; i < 16; i++)
                    {
                        int idx = (int)((bits >> (i * 3)) & 7);
                        block[i] = vals[idx];
                    }

                    int px = bx * 4, py = by * 4;
                    for (int yy = 0; yy < 4 && py + yy < h; yy++)
                        for (int xx = 0; xx < 4 && px + xx < w; xx++)
                        {
                            int di = ((py + yy) * w + (px + xx)) * 4;
                            int si = yy * 4 + xx;
                            byte v = block[si];
                            dst[di] = v; dst[di + 1] = v; dst[di + 2] = v; dst[di + 3] = 255;
                        }
                }
            }
        }

        // ═══════════ BC5 (two-channel normal map, 16 bytes/block) ═══════════

        private static void DecodeBc5(byte[] src, int off, int w, int h, byte[] dst)
        {
            // BC5 = two BC4 blocks: first for R, second for G. B=128, A=255.
            int bw = Math.Max(1, (w + 3) / 4);
            int bh = Math.Max(1, (h + 3) / 4);
            for (int by = 0; by < bh; by++)
            {
                for (int bx = 0; bx < bw; bx++)
                {
                    int blockOff = off + (by * bw + bx) * 16;
                    // Decode red channel from first BC4 block
                    byte r0 = src[blockOff], r1 = src[blockOff + 1];
                    ulong rBits = Read48(src, blockOff + 2);
                    byte[] rVals = EvalBc4Endpoints(r0, r1);

                    // Decode green channel from second BC4 block  
                    byte g0 = src[blockOff + 8], g1 = src[blockOff + 9];
                    ulong gBits = Read48(src, blockOff + 10);
                    byte[] gVals = EvalBc4Endpoints(g0, g1);

                    int px = bx * 4, py = by * 4;
                    for (int i = 0; i < 16; i++)
                    {
                        int ri = (int)((rBits >> (i * 3)) & 7);
                        int gi = (int)((gBits >> (i * 3)) & 7);
                        int xx = i % 4, yy = i / 4;
                        if (px + xx < w && py + yy < h)
                        {
                            int di = ((py + yy) * w + (px + xx)) * 4;
                            dst[di] = rVals[ri];
                            dst[di + 1] = gVals[gi];
                            dst[di + 2] = 128;     // default blue for normal maps
                            dst[di + 3] = 255;
                        }
                    }
                }
            }
        }

        private static byte[] EvalBc4Endpoints(byte e0, byte e1)
        {
            var v = new byte[8];
            v[0] = e0; v[1] = e1;
            if (e0 > e1)
                for (int i = 2; i < 8; i++)
                    v[i] = (byte)(((8 - i) * e0 + (i - 1) * e1) / 7);
            else
            {
                for (int i = 2; i < 6; i++)
                    v[i] = (byte)(((6 - i) * e0 + (i - 1) * e1) / 5);
                v[6] = 0; v[7] = e0 == 0 && e1 == 0 ? (byte)0 : (byte)255;
            }
            return v;
        }

        private static ulong Read48(byte[] src, int off)
        {
            return (ulong)src[off] | ((ulong)src[off + 1] << 8) |
                   ((ulong)src[off + 2] << 16) | ((ulong)src[off + 3] << 24) |
                   ((ulong)src[off + 4] << 32) | ((ulong)src[off + 5] << 40);
        }

        // ═══════════ BC7 (high-quality, 16 bytes/block, 8 modes) ═══════════

        private static void DecodeBc7(byte[] src, int off, int w, int h, byte[] dst)
        {
            int bw = Math.Max(1, (w + 3) / 4);
            int bh = Math.Max(1, (h + 3) / 4);
            for (int by = 0; by < bh; by++)
            {
                for (int bx = 0; bx < bw; bx++)
                {
                    int blockOff = off + (by * bw + bx) * 16;
                    int[] rgba = new int[4 * 4 * 4];
                    DecodeBc7Block(src, blockOff, rgba);
                    int px = bx * 4, py = by * 4;
                    for (int yy = 0; yy < 4 && py + yy < h; yy++)
                        for (int xx = 0; xx < 4 && px + xx < w; xx++)
                        {
                            int di = ((py + yy) * w + (px + xx)) * 4;
                            int si = (yy * 4 + xx) * 4;
                            dst[di] = (byte)rgba[si];
                            dst[di + 1] = (byte)rgba[si + 1];
                            dst[di + 2] = (byte)rgba[si + 2];
                            dst[di + 3] = (byte)rgba[si + 3];
                        }
                }
            }
        }

        private static void DecodeBc7Block(byte[] src, int off, int[] rgba)
        {
            // Determine mode from leading bits of first byte
            byte b = src[off];
            int mode = b == 0 ? 0 : (b & 0x80) != 0 ? 7 : (b & 0x40) != 0 ? 6 :
                       (b & 0x20) != 0 ? 5 : (b & 0x10) != 0 ? 4 :
                       (b & 0x08) != 0 ? 3 : (b & 0x04) != 0 ? 2 :
                       (b & 0x02) != 0 ? 1 : 0;

            // Read the full 128-bit block
            var bits = new ulong[2];
            bits[0] = BitConverter.ToUInt64(src, off);
            bits[1] = BitConverter.ToUInt64(src, off + 8);

            int bitPos = mode == 0 ? 1 : mode <= 4 ? mode + 1 : mode + 2;

            switch (mode)
            {
                case 0: DecodeBc7Mode0(bits, bitPos, rgba); break;
                case 1: DecodeBc7Mode1(bits, bitPos, rgba); break;
                case 2: DecodeBc7Mode2(bits, bitPos, rgba); break;
                case 3: DecodeBc7Mode3(bits, bitPos, rgba); break;
                case 4: DecodeBc7Mode4(bits, bitPos, rgba); break;
                case 5: DecodeBc7Mode5(bits, bitPos, rgba); break;
                case 6: DecodeBc7Mode6(bits, bitPos, rgba); break;
                case 7: DecodeBc7Mode7(bits, bitPos, rgba); break;
            }
        }

        // ── BC7 Bit extraction helpers ──
        private static int Bc7Bits(ulong[] bits, ref int pos, int n)
        {
            int val = 0;
            for (int i = 0; i < n; i++)
            {
                int idx = pos >> 6;
                int bit = pos & 63;
                val |= (int)((bits[idx] >> bit) & 1) << i;
                pos++;
            }
            return val;
        }

        // BC7 partition tables for 2-subset (64 entries, 16 pixels) and 3-subset (64 entries)
        private static readonly int[] Bc7Partition2 = new int[] {
            0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,
            0,0,1,1,0,0,1,1,0,0,1,1,0,0,1,1, 0,1,0,1,0,1,0,1,0,1,0,1,0,1,0,1,
            0,0,0,1,0,0,0,1,0,0,0,1,0,0,0,1, 1,1,1,0,1,1,1,0,1,1,1,0,1,1,1,0,
            0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,
            0,0,1,1,0,0,1,1,0,0,1,1,0,0,1,1, 0,1,1,0,0,1,1,0,0,1,1,0,0,1,1,0,
            0,0,0,1,0,0,0,1,0,0,0,1,0,0,0,1, 0,0,1,1,0,0,1,1,0,0,1,1,0,0,1,1,
            0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 1,1,0,0,1,1,0,0,1,1,0,0,1,1,0,0,
            0,0,1,1,0,0,1,1,0,0,1,1,0,0,1,1, 0,1,0,1,0,1,0,1,0,1,0,1,0,1,0,1,
            0,0,0,0,1,0,0,0,1,0,0,0,1,0,0,0, 1,1,1,1,0,1,1,1,0,1,1,1,0,1,1,1,
            0,0,0,0,0,1,0,0,0,1,0,0,0,1,0,0, 1,1,1,1,1,0,1,1,1,0,1,1,1,0,1,1,
            0,0,1,1,0,0,1,1,0,0,1,1,0,0,1,1, 1,1,0,0,1,1,0,0,1,1,0,0,1,1,0,0,
            0,0,0,0,0,0,1,0,0,0,1,0,0,0,1,0, 1,1,1,1,1,1,0,1,1,1,0,1,1,1,0,1,
            0,0,1,1,0,0,0,1,0,0,1,1,0,0,0,1, 1,1,0,0,1,1,1,0,1,1,0,0,1,1,1,0,
            0,0,0,0,0,0,0,0,1,0,0,0,1,0,0,0, 1,1,1,1,1,1,1,1,0,1,1,1,0,1,1,1,
            0,0,1,1,0,0,0,0,0,0,1,1,0,0,1,1, 1,1,1,0,1,1,1,1,1,1,1,0,1,1,0,0,
            0,0,0,0,0,0,0,0,0,1,0,0,0,0,0,0, 1,1,1,1,1,1,1,1,1,0,1,1,1,1,1,1,
            0,0,1,1,0,0,0,1,0,0,0,0,0,0,0,0, 1,1,1,0,1,1,1,0,1,1,1,1,1,1,1,1,
            0,0,0,0,0,1,0,0,0,0,0,0,0,0,0,0, 1,1,1,1,1,0,1,1,1,1,1,1,1,1,1,1,
            0,0,0,0,0,0,0,1,0,0,0,0,0,0,0,0, 1,1,1,1,1,1,1,0,1,1,1,1,1,1,1,1,
            0,0,0,0,0,0,0,0,0,0,0,0,1,0,0,0, 1,1,1,1,1,1,1,1,1,1,1,1,0,1,1,1,
            0,0,0,0,0,0,0,0,0,0,1,1,0,0,1,1, 1,1,1,1,1,1,1,1,1,1,0,0,1,1,0,0,
            0,0,0,0,0,0,0,0,0,0,0,0,0,0,1,0, 1,1,1,1,1,1,1,1,1,1,1,1,1,1,0,1,
            0,0,1,1,0,0,0,0,0,0,0,0,0,0,0,0, 1,1,0,0,1,1,1,1,1,1,1,1,1,1,1,1,
            0,0,0,0,1,0,0,0,0,0,0,0,0,0,0,0, 1,1,1,1,0,1,1,1,1,1,1,1,1,1,1,1,
            0,0,0,0,0,1,1,0,0,0,0,0,0,0,0,0, 1,1,1,1,1,0,0,1,1,1,1,1,1,1,1,1,
            0,0,0,0,0,0,0,0,0,0,0,1,0,0,0,0, 1,1,1,1,1,1,1,1,1,1,1,0,1,1,1,1,
            0,0,0,0,0,0,0,0,0,0,0,0,0,1,1,0, 1,1,1,1,1,1,1,1,1,1,1,1,1,0,0,1,
            0,0,0,0,0,0,0,0,1,1,0,0,0,0,0,0, 1,1,1,1,1,1,1,1,0,0,1,1,1,1,1,1,
            0,0,1,1,0,0,0,0,0,0,0,0,0,0,0,0, 1,1,1,0,1,1,1,1,1,1,1,1,1,1,1,1,
            0,0,0,0,0,0,0,0,0,0,0,0,0,0,1,1, 1,1,1,1,1,1,1,1,1,1,1,1,1,1,0,0,
            0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1, 1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,0,
            0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,0,
            0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1
        };

        // BC7 3-subset partition table (compressed - first 16 entries shown, the rest are patterns from the spec)
        // Full 64-entry table for 3-subset with 16 values each
        private static readonly int[] Bc7Partition3 = new int[] {
            0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1, 2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,
            0,0,1,1,0,0,1,1,0,0,1,1,0,0,1,1, 0,1,1,2,0,1,1,2,0,1,1,2,0,1,1,2, 0,1,2,2,0,1,2,2,0,1,2,2,0,1,2,2,
            0,0,0,1,0,0,0,1,0,0,0,1,0,0,0,1, 1,1,1,0,1,1,1,0,1,1,1,0,1,1,1,0, 2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,
            0,0,1,1,0,0,1,1,0,0,1,1,0,0,1,1, 0,0,1,1,0,0,1,1,0,0,1,1,0,0,1,1, 0,1,2,2,0,1,2,2,0,1,2,2,0,1,2,2,
            0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,1,1,2,0,1,1,2,0,1,1,2,0,1,1,2, 0,0,2,2,0,0,2,2,0,0,2,2,0,0,2,2,
            0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1, 0,0,0,2,0,0,0,2,0,0,0,2,0,0,0,2,
            0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,2,2,0,0,2,2,0,0,2,2,0,0,2,2,
            0,0,1,1,0,0,0,0,0,0,0,0,0,0,0,0, 1,1,0,0,1,1,0,0,0,0,0,0,0,0,0,0, 2,2,2,2,2,2,2,2,1,1,1,1,1,1,1,1,
            0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0,0,0,0,0,0,2,2,
            0,0,1,1,0,0,0,0,0,0,0,0,0,0,0,0, 0,1,1,0,0,1,1,0,1,1,0,0,1,1,0,0, 2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,
            0,0,0,1,0,0,0,1,0,0,0,1,0,0,0,1, 0,0,1,1,0,0,1,1,0,0,1,1,0,0,1,1, 2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,
            0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,0,2,0,0,0,2,0,0,0,2,0,0,0,2, 0,1,2,2,0,1,2,2,0,1,2,2,0,1,2,2,
            0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,1,1,1,0,1,1,1,0,1,1,1,0,1,1,1,
            0,1,1,2,0,1,1,2,0,1,1,2,0,1,1,2, 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,
            0,0,0,1,0,0,0,1,0,0,1,1,0,0,1,1, 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 2,2,2,2,1,1,1,1,2,2,2,2,1,1,1,1,
            0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,0,0,1,0,0,0,1,0,0,0,1,0,0,0, 1,1,1,1,0,1,1,1,1,1,1,1,0,1,1,1,
            0,0,0,0,0,0,0,0,0,1,1,0,0,1,1,0, 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,
            0,0,0,0,0,0,0,0,0,0,0,0,1,0,0,0, 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 1,1,1,1,1,1,1,1,1,1,1,1,0,1,1,1,
            0,0,1,1,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,0,1,0,0,0,1,0,0,0,1,0,0,0,1, 2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,
            0,0,1,1,0,0,1,1,0,0,0,1,0,0,0,1, 0,0,0,1,0,0,0,1,0,0,0,1,0,0,0,1, 1,0,0,0,1,0,0,0,1,0,0,0,1,0,0,0,
            0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1,
            0,0,0,0,1,0,0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 1,1,1,1,0,1,1,1,1,1,1,1,1,1,1,1,
            0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,1,0,0,0,0,0,0,0,0, 2,2,2,2,2,2,2,1,2,2,2,2,2,2,2,2,
            0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0,0,0,0,0,0,1,0,
            0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0,0,0,0,1,0,0,0,
            0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,2,2,2,0,2,2,2,0,2,2,2,0,2,2,2,
            0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 1,1,1,1,0,0,0,0,0,0,0,0,0,0,0,0,
            0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,2,2,0,0,2,2,0,0,2,2,0,0,2,2,
            0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,2,
            0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,0,1,0,0,0,1,0,0,0,1,0,0,0,1,
            0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,1,1,1,0,0,0,0,0,1,1,1,
            0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0,0,0,0,0,1,1,0,
            0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,0,2,0,0,0,2,0,0,0,2,0,0,0,2,
            0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,1,2,0,0,1,2,0,0,1,2,0,0,1,2,
            0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,
        };

        // BC7 mode-specific decoders
        // Each mode has: numSubsets, hasAlpha, endpointBits, pBits, indexBits
        // We use a unified approach: read endpoints, expand, interpolate

        private static void DecodeBc7Mode0(ulong[] bits, int pos, int[] rgba)
        {
            // Mode 0: 3 subsets, RGB 4.4.4, no alpha, P-bit
            int pbits = Bc7Bits(bits, ref pos, 4);   // partition
            int part = Bc7Bits(bits, ref pos, 4);
            int ep0 = Bc7Bits(bits, ref pos, 4) | (Bc7Bits(bits, ref pos, 4) << 4) | (Bc7Bits(bits, ref pos, 4) << 8);
            int ep1 = Bc7Bits(bits, ref pos, 4) | (Bc7Bits(bits, ref pos, 4) << 4) | (Bc7Bits(bits, ref pos, 4) << 8);
            int ep2 = Bc7Bits(bits, ref pos, 4) | (Bc7Bits(bits, ref pos, 4) << 4) | (Bc7Bits(bits, ref pos, 4) << 8);
            int ep3 = Bc7Bits(bits, ref pos, 4) | (Bc7Bits(bits, ref pos, 4) << 4) | (Bc7Bits(bits, ref pos, 4) << 8);
            int ep4 = Bc7Bits(bits, ref pos, 4) | (Bc7Bits(bits, ref pos, 4) << 4) | (Bc7Bits(bits, ref pos, 4) << 8);
            int ep5 = Bc7Bits(bits, ref pos, 4) | (Bc7Bits(bits, ref pos, 4) << 4) | (Bc7Bits(bits, ref pos, 4) << 8);
            // 6 P-bits: read the last bit of each endpoint pair
            int pbit0e0 = Bc7Bits(bits, ref pos, 1);
            int pbit0e1 = Bc7Bits(bits, ref pos, 1);
            int pbit1e0 = Bc7Bits(bits, ref pos, 1);
            int pbit1e1 = Bc7Bits(bits, ref pos, 1);
            int pbit2e0 = Bc7Bits(bits, ref pos, 1);
            int pbit2e1 = Bc7Bits(bits, ref pos, 1);

            // Expand endpoints with P-bit
            Bc7ExpandEp(ep0, pbit0e0, out int r0e0, out int g0e0, out int b0e0);
            Bc7ExpandEp(ep1, pbit0e1, out int r0e1, out int g0e1, out int b0e1);
            Bc7ExpandEp(ep2, pbit1e0, out int r1e0, out int g1e0, out int b1e0);
            Bc7ExpandEp(ep3, pbit1e1, out int r1e1, out int g1e1, out int b1e1);
            Bc7ExpandEp(ep4, pbit2e0, out int r2e0, out int g2e0, out int b2e0);
            Bc7ExpandEp(ep5, pbit2e1, out int r2e1, out int g2e1, out int b2e1);

            // Index bits follow
            int[] subsets = new int[16];
            for (int i = 0; i < 16; i++)
                subsets[i] = Bc7Partition3[part * 16 + i] & 3;

            for (int i = 0; i < 16; i++)
            {
                int idx = Bc7Bits(bits, ref pos, 3);
                int s = subsets[i];
                int re0, ge0, be0, re1, ge1, be1;
                if (s == 0) { re0 = r0e0; ge0 = g0e0; be0 = b0e0; re1 = r0e1; ge1 = g0e1; be1 = b0e1; }
                else if (s == 1) { re0 = r1e0; ge0 = g1e0; be0 = b1e0; re1 = r1e1; ge1 = g1e1; be1 = b1e1; }
                else { re0 = r2e0; ge0 = g2e0; be0 = b2e0; re1 = r2e1; ge1 = g2e1; be1 = b2e1; }
                int di = i * 4;
                rgba[di] = Bc7Interp(re0, re1, idx, 3, false);
                rgba[di + 1] = Bc7Interp(ge0, ge1, idx, 3, false);
                rgba[di + 2] = Bc7Interp(be0, be1, idx, 3, false);
                rgba[di + 3] = 255;
            }
        }

        private static void Bc7ExpandEp(int ep, int pbit, out int r, out int g, out int b)
        {
            r = ((ep & 0xF) << 4) | pbit;
            g = (((ep >> 4) & 0xF) << 4) | pbit;
            b = (((ep >> 8) & 0xF) << 4) | pbit;
        }

        private static int Bc7Interp(int e0, int e1, int idx, int idxBits, bool alpha)
        {
            int n = (1 << idxBits) - 1;
            if (idx == 0) return e0;
            if (idx == n) return e1;
            if (!alpha) return ((e0 * (n - idx) + e1 * idx) + n / 2) / n;
            return ((e0 * (n - idx) + e1 * idx)) / n;
        }

        private static readonly int[] Bc7Subset2Lookup = { 2, 3, 3, 4, 4, 1, 4, 4 };
        private static void DecodeBc7Mode1(ulong[] bits, int pos, int[] rgba)
        {
            // Mode 1: 2 subsets, RGB 6.6.6, no alpha, shared P-bit per subset
            int part = Bc7Bits(bits, ref pos, 6);
            // Endpoints: 3 components × 2 endpoints × 2 subsets × 6 bits = 72 bits for RGB
            int[] ep = new int[12]; // 2 subsets × 2 endpoints × 3 components
            for (int i = 0; i < 12; i++)
                ep[i] = Bc7Bits(bits, ref pos, 6);
            // P-bits: 2 subsets × 2 endpoints ÷ 2 = 2 P-bits (shared across each subset's endpoints)
            int pbit0 = Bc7Bits(bits, ref pos, 1);
            int pbit1 = Bc7Bits(bits, ref pos, 1);

            // Expand
            for (int i = 0; i < 6; i++) ep[i] = (ep[i] << 2) | pbit0;
            for (int i = 6; i < 12; i++) ep[i] = (ep[i] << 2) | pbit1;

            var subsets = new int[16];
            for (int i = 0; i < 16; i++)
                subsets[i] = Bc7Partition2[part * 16 + i];

            for (int i = 0; i < 16; i++)
            {
                int idx = Bc7Bits(bits, ref pos, 3);
                int s = subsets[i];
                int baseE = s * 6;
                int di = i * 4;
                rgba[di] = Bc7Interp(ep[baseE], ep[baseE + 3], idx, 3, false);
                rgba[di + 1] = Bc7Interp(ep[baseE + 1], ep[baseE + 4], idx, 3, false);
                rgba[di + 2] = Bc7Interp(ep[baseE + 2], ep[baseE + 5], idx, 3, false);
                rgba[di + 3] = 255;
            }
        }

        private static void DecodeBc7Mode2(ulong[] bits, int pos, int[] rgba)
        {
            // Mode 2: 3 subsets, RGB 5.5.5, no alpha, no P-bit
            int part = Bc7Bits(bits, ref pos, 6);
            int[] ep = new int[18]; // 3 subsets × 2 endpoints × 3 components
            for (int i = 0; i < 18; i++)
                ep[i] = Bc7Bits(bits, ref pos, 5);
            // Expand 5→8
            for (int i = 0; i < 18; i++)
                ep[i] = (ep[i] << 3) | (ep[i] >> 2);

            var subsets = new int[16];
            for (int i = 0; i < 16; i++)
                subsets[i] = Bc7Partition3[part * 16 + i] & 3;

            for (int i = 0; i < 16; i++)
            {
                int idx = Bc7Bits(bits, ref pos, 3);
                int s = subsets[i];
                int baseE = s * 6;
                int di = i * 4;
                rgba[di] = Bc7Interp(ep[baseE], ep[baseE + 3], idx, 3, false);
                rgba[di + 1] = Bc7Interp(ep[baseE + 1], ep[baseE + 4], idx, 3, false);
                rgba[di + 2] = Bc7Interp(ep[baseE + 2], ep[baseE + 5], idx, 3, false);
                rgba[di + 3] = 255;
            }
        }

        private static void DecodeBc7Mode3(ulong[] bits, int pos, int[] rgba)
        {
            // Mode 3: 2 subsets, RGB 7.7.7 + 1-bit alpha, P-bit per endpoint
            int part = Bc7Bits(bits, ref pos, 6);
            // 2 subsets × 2 endpoints × 4 components (RGBA) × 7 bits
            // But alpha is only 1-bit each
            int[] ep = new int[12]; // R0,G0,B0,R1,G1,B1 for subset 0 and 1
            for (int i = 0; i < 12; i++)
                ep[i] = Bc7Bits(bits, ref pos, 7);
            // Alpha: 2 endpoints × 2 subsets = 4 × 1 bit
            int[] a = new int[4];
            for (int i = 0; i < 4; i++) a[i] = Bc7Bits(bits, ref pos, 1);
            // P-bits: 4 endpoints × 1 bit
            int[] pb = new int[4];
            for (int i = 0; i < 4; i++) pb[i] = Bc7Bits(bits, ref pos, 1);

            // Expand
            for (int i = 0; i < 12; i++) ep[i] = (ep[i] << 1) | pb[i / 3 * 2 + (i % 3 == 0 ? 0 : 0)];

            // Actually, simpler: just handle it with per-endpoint P-bits
            for (int i = 0; i < 4; i++)
            {
                int baseEp = i * 3;
                ep[baseEp] = (ep[baseEp] << 1) | pb[i];     // R
                if (baseEp + 1 < 12) ep[baseEp + 1] = (ep[baseEp + 1] << 1) | pb[i]; // G
                if (baseEp + 2 < 12) ep[baseEp + 2] = (ep[baseEp + 2] << 1) | pb[i]; // B
                a[i] = (a[i] << 7) | (a[i] << 6) | (a[i] << 5); // expand 1→8
            }

            var subsets = new int[16];
            for (int i = 0; i < 16; i++)
                subsets[i] = Bc7Partition2[part * 16 + i];

            for (int i = 0; i < 16; i++)
            {
                int idx = Bc7Bits(bits, ref pos, 2);
                int s = subsets[i];
                int baseE = s * 6;
                int di = i * 4;
                rgba[di] = Bc7Interp(ep[baseE], ep[baseE + 3], idx, 2, false);
                rgba[di + 1] = Bc7Interp(ep[baseE + 1], ep[baseE + 4], idx, 2, false);
                rgba[di + 2] = Bc7Interp(ep[baseE + 2], ep[baseE + 5], idx, 2, false);
                int ai = s * 2;
                rgba[di + 3] = Bc7Interp(a[ai], a[ai + 1], idx, 2, true);
            }
        }

        private static void DecodeBc7Mode4(ulong[] bits, int pos, int[] rgba)
        {
            // Mode 4: 1 subset, RGB 5.5.5 + 6-bit alpha, 2-bit rotation, index selection
            int rot = Bc7Bits(bits, ref pos, 2);
            int idxMode = Bc7Bits(bits, ref pos, 1);
            // Endpoints: 2 × 3 color + 2 alpha = 8 values
            int r0 = Bc7Bits(bits, ref pos, 5), r1 = Bc7Bits(bits, ref pos, 5);
            int g0 = Bc7Bits(bits, ref pos, 5), g1 = Bc7Bits(bits, ref pos, 5);
            int b0 = Bc7Bits(bits, ref pos, 5), b1 = Bc7Bits(bits, ref pos, 5);
            int a0 = Bc7Bits(bits, ref pos, 6), a1 = Bc7Bits(bits, ref pos, 6);

            // Expand
            r0 = (r0 << 3) | (r0 >> 2); r1 = (r1 << 3) | (r1 >> 2);
            g0 = (g0 << 3) | (g0 >> 2); g1 = (g1 << 3) | (g1 >> 2);
            b0 = (b0 << 3) | (b0 >> 2); b1 = (b1 << 3) | (b1 >> 2);
            a0 = (a0 << 2) | (a0 >> 4); a1 = (a1 << 2) | (a1 >> 4);

            int idxBits = idxMode == 0 ? 2 : 3;
            for (int i = 0; i < 16; i++)
            {
                int idxC = Bc7Bits(bits, ref pos, idxBits);
                int idxA = Bc7Bits(bits, ref pos, 2);
                int di = i * 4;
                int r = Bc7Interp(r0, r1, idxC, idxBits, false);
                int g = Bc7Interp(g0, g1, idxC, idxBits, false);
                int b = Bc7Interp(b0, b1, idxC, idxBits, false);
                int a = Bc7Interp(a0, a1, idxA, 2, true);

                // Apply rotation
                if (rot == 1) { rgba[di] = a; rgba[di + 3] = r; }
                else if (rot == 2) { rgba[di + 1] = a; rgba[di + 3] = g; }
                else if (rot == 3) { rgba[di + 2] = a; rgba[di + 3] = b; }
                else { rgba[di] = r; rgba[di + 1] = g; rgba[di + 2] = b; rgba[di + 3] = a; }
            }
        }

        private static void DecodeBc7Mode5(ulong[] bits, int pos, int[] rgba)
        {
            // Mode 5: 1 subset, RGB 7.7.7 + 8-bit alpha, 2-bit rotation
            int rot = Bc7Bits(bits, ref pos, 2);
            int r0 = Bc7Bits(bits, ref pos, 7), r1 = Bc7Bits(bits, ref pos, 7);
            int g0 = Bc7Bits(bits, ref pos, 7), g1 = Bc7Bits(bits, ref pos, 7);
            int b0 = Bc7Bits(bits, ref pos, 7), b1 = Bc7Bits(bits, ref pos, 7);
            int a0 = Bc7Bits(bits, ref pos, 8), a1 = Bc7Bits(bits, ref pos, 8);

            // Expand
            r0 = (r0 << 1) | (r0 >> 6); r1 = (r1 << 1) | (r1 >> 6);
            g0 = (g0 << 1) | (g0 >> 6); g1 = (g1 << 1) | (g1 >> 6);
            b0 = (b0 << 1) | (b0 >> 6); b1 = (b1 << 1) | (b1 >> 6);

            for (int i = 0; i < 16; i++)
            {
                int idxC = Bc7Bits(bits, ref pos, 2);
                int idxA = Bc7Bits(bits, ref pos, 2);
                int di = i * 4;
                int r = Bc7Interp(r0, r1, idxC, 2, false);
                int g = Bc7Interp(g0, g1, idxC, 2, false);
                int b = Bc7Interp(b0, b1, idxC, 2, false);
                int a = Bc7Interp(a0, a1, idxA, 2, true);

                if (rot == 1) { rgba[di] = a; rgba[di + 3] = r; }
                else if (rot == 2) { rgba[di + 1] = a; rgba[di + 3] = g; }
                else if (rot == 3) { rgba[di + 2] = a; rgba[di + 3] = b; }
                else { rgba[di] = r; rgba[di + 1] = g; rgba[di + 2] = b; rgba[di + 3] = a; }
            }
        }

        private static void DecodeBc7Mode6(ulong[] bits, int pos, int[] rgba)
        {
            // Mode 6: 1 subset, RGB 7.7.7 + 7-bit alpha, no rotation
            int r0 = Bc7Bits(bits, ref pos, 7), r1 = Bc7Bits(bits, ref pos, 7);
            int g0 = Bc7Bits(bits, ref pos, 7), g1 = Bc7Bits(bits, ref pos, 7);
            int b0 = Bc7Bits(bits, ref pos, 7), b1 = Bc7Bits(bits, ref pos, 7);
            int a0 = Bc7Bits(bits, ref pos, 7), a1 = Bc7Bits(bits, ref pos, 7);
            // 1 P-bit per endpoint
            int pbit0 = Bc7Bits(bits, ref pos, 1);
            int pbit1 = Bc7Bits(bits, ref pos, 1);

            r0 = (r0 << 1) | pbit0; r1 = (r1 << 1) | pbit1;
            g0 = (g0 << 1) | pbit0; g1 = (g1 << 1) | pbit1;
            b0 = (b0 << 1) | pbit0; b1 = (b1 << 1) | pbit1;
            a0 = (a0 << 1) | pbit0; a1 = (a1 << 1) | pbit1;

            for (int i = 0; i < 16; i++)
            {
                int idx = Bc7Bits(bits, ref pos, 4);
                int di = i * 4;
                rgba[di] = Bc7Interp(r0, r1, idx, 4, false);
                rgba[di + 1] = Bc7Interp(g0, g1, idx, 4, false);
                rgba[di + 2] = Bc7Interp(b0, b1, idx, 4, false);
                rgba[di + 3] = Bc7Interp(a0, a1, idx, 4, true);
            }
        }

        private static void DecodeBc7Mode7(ulong[] bits, int pos, int[] rgba)
        {
            // Mode 7: 2 subsets, RGB 5.5.5 + 6-bit alpha, 2-bit color index
            int part = Bc7Bits(bits, ref pos, 6);
            int r0 = Bc7Bits(bits, ref pos, 5), r1 = Bc7Bits(bits, ref pos, 5);
            int g0 = Bc7Bits(bits, ref pos, 5), g1 = Bc7Bits(bits, ref pos, 5);
            int b0 = Bc7Bits(bits, ref pos, 5), b1 = Bc7Bits(bits, ref pos, 5);
            int a0 = Bc7Bits(bits, ref pos, 6), a1 = Bc7Bits(bits, ref pos, 6);
            int a2 = Bc7Bits(bits, ref pos, 6), a3 = Bc7Bits(bits, ref pos, 6); // subset 1 alpha

            // Expand
            r0 = (r0 << 3) | (r0 >> 2); r1 = (r1 << 3) | (r1 >> 2);
            g0 = (g0 << 3) | (g0 >> 2); g1 = (g1 << 3) | (g1 >> 2);
            b0 = (b0 << 3) | (b0 >> 2); b1 = (b1 << 3) | (b1 >> 2);
            a0 = (a0 << 2) | (a0 >> 4); a1 = (a1 << 2) | (a1 >> 4);
            a2 = (a2 << 2) | (a2 >> 4); a3 = (a3 << 2) | (a3 >> 4);

            var subsets = new int[16];
            for (int i = 0; i < 16; i++)
                subsets[i] = Bc7Partition2[part * 16 + i];

            for (int i = 0; i < 16; i++)
            {
                int idx = Bc7Bits(bits, ref pos, 2);
                int s = subsets[i];
                int di = i * 4;
                rgba[di] = Bc7Interp(r0, r1, idx, 2, false);
                rgba[di + 1] = Bc7Interp(g0, g1, idx, 2, false);
                rgba[di + 2] = Bc7Interp(b0, b1, idx, 2, false);
                int a0v = s == 0 ? a0 : a2;
                int a1v = s == 0 ? a1 : a3;
                rgba[di + 3] = Bc7Interp(a0v, a1v, idx, 2, true);
            }
        }

        private static void Rgb565(ushort c, out byte r, out byte g, out byte b)
        {
            r = (byte)((c >> 11) * 255 / 31);
            g = (byte)(((c >> 5) & 0x3F) * 255 / 63);
            b = (byte)((c & 0x1F) * 255 / 31);
        }

        private static void FlipVertical(byte[] rgba, int w, int h)
        {
            int rowBytes = w * 4;
            var tmp = new byte[rowBytes];
            for (int y = 0; y < h / 2; y++)
            {
                int top = y * rowBytes;
                int bot = (h - 1 - y) * rowBytes;
                Buffer.BlockCopy(rgba, top, tmp, 0, rowBytes);
                Buffer.BlockCopy(rgba, bot, rgba, top, rowBytes);
                Buffer.BlockCopy(tmp, 0, rgba, bot, rowBytes);
            }
        }

}
