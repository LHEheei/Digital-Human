using System;
using System.IO;
using System.Text;

namespace SrtpAvatar
{
    public static class Pcm16Wav
    {
        public static byte[] EncodeMono(float[] samples, int sampleRate)
        {
            if (samples == null || samples.Length == 0 || sampleRate <= 0)
            {
                return Array.Empty<byte>();
            }

            var dataLength = samples.Length * 2;
            using var stream = new MemoryStream(44 + dataLength);
            using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataLength);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(sampleRate);
            writer.Write(sampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataLength);
            for (var i = 0; i < samples.Length; i++)
            {
                var value = (short)Math.Round(Math.Max(-1f, Math.Min(1f, samples[i])) * short.MaxValue);
                writer.Write(value);
            }

            return stream.ToArray();
        }

        public static float[] ExtractLatestMono(float[] interleaved, int channels, int writePositionFrames, int requestedFrames)
        {
            if (interleaved == null || channels <= 0 || requestedFrames <= 0 || interleaved.Length < channels)
            {
                return Array.Empty<float>();
            }

            var ringFrames = interleaved.Length / channels;
            var count = Math.Min(requestedFrames, ringFrames);
            var start = (writePositionFrames - count + ringFrames) % ringFrames;
            var mono = new float[count];
            for (var frame = 0; frame < count; frame++)
            {
                var sourceFrame = (start + frame) % ringFrames;
                var total = 0f;
                for (var channel = 0; channel < channels; channel++)
                {
                    total += interleaved[sourceFrame * channels + channel];
                }

                mono[frame] = total / channels;
            }

            return mono;
        }
    }
}
