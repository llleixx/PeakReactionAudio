using System;

namespace PeakReactionAudio.Core;

internal static class Pcm
{
    public static float[] Downmix(float[] input, int channels)
    {
        if (channels <= 0 || input.Length == 0 || input.Length % channels != 0) throw new ArgumentException("Invalid PCM shape.");
        var output = new float[input.Length / channels];
        for (int f = 0; f < output.Length; f++)
        {
            double sum = 0;
            for (int c = 0; c < channels; c++) sum += Limits.Clamp(input[f * channels + c], -1, 1, 0);
            output[f] = (float)(sum / channels);
        }
        return output;
    }

    // Windowed-sinc conversion also low-pass filters when reducing sample rate.
    // Called on a worker while preparing assets, never in a voice callback.
    public static float[] Resample(float[] mono, int sourceRate, int targetRate)
    {
        if (sourceRate <= 0 || targetRate <= 0 || mono.Length == 0) throw new ArgumentException("Invalid sample rate.");
        if (sourceRate == targetRate) return mono;
        int length = checked((int)Math.Max(1, Math.Round((double)mono.Length * targetRate / sourceRate)));
        var output = new float[length];
        double ratio = (double)sourceRate / targetRate;
        double cutoff = Math.Min(1d, (double)targetRate / sourceRate) * 0.94d;
        const int radius = 24;
        for (int i = 0; i < length; i++)
        {
            double position = i * ratio, sum = 0, weightSum = 0;
            int center = (int)position;
            for (int j = center - radius + 1; j <= center + radius; j++)
            {
                if (j < 0 || j >= mono.Length) continue;
                double distance = position - j;
                if (Math.Abs(distance) >= radius) continue;
                double x = Math.PI * distance * cutoff;
                double sinc = Math.Abs(x) < 1e-10 ? 1 : Math.Sin(x) / x;
                double weight = cutoff * sinc * (0.5 + 0.5 * Math.Cos(Math.PI * distance / radius));
                sum += mono[j] * weight;
                weightSum += weight;
            }
            output[i] = Limits.Clamp((float)(Math.Abs(weightSum) > 1e-10 ? sum / weightSum : 0), -1, 1, 0);
        }
        return output;
    }
}
