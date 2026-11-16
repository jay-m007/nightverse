using System;

namespace Nightverse.Services
{
    /// <summary>
    /// Turns a raw WASAPI buffer into mono samples in -1..1. Windows hands back different formats depending on
    /// the sound device (32-bit float is usual, but 16, 24 and 32-bit integer all turn up, and surround cards
    /// send more than two channels), so every common layout is handled instead of being skipped.
    /// Dependency-free so it can be tested offline.
    /// </summary>
    public static class SampleConverter
    {
        /// <summary>Returns the number of mono frames written to <paramref name="dst"/>.</summary>
        public static int ToMono(byte[] src, int bytes, int channels, int bitsPerSample, bool isFloat, double[] dst)
        {
            if (src == null || dst == null || bytes <= 0) return 0;
            int ch = Math.Max(1, channels);
            int bps = bitsPerSample / 8;
            if (bps < 2 || bps > 4) return 0;
            int frames = Math.Min(bytes / (bps * ch), dst.Length);
            int pos = 0;
            for (int f = 0; f < frames; f++)
            {
                double sum = 0;
                for (int c = 0; c < ch; c++)
                {
                    sum += isFloat
                        ? (bps == 4 ? BitConverter.ToSingle(src, pos) : 0)
                        : bps switch
                        {
                            2 => BitConverter.ToInt16(src, pos) / 32768.0,
                            3 => ((src[pos] | (src[pos + 1] << 8) | ((sbyte)src[pos + 2] << 16))) / 8388608.0,
                            _ => BitConverter.ToInt32(src, pos) / 2147483648.0,
                        };
                    pos += bps;
                }
                dst[f] = sum / ch;
            }
            return frames;
        }
    }
}
