using System;
using System.Threading;

namespace PeakReactionAudio.Core;

// Immutable PCM is shared; each bus owns its own event cursors. The bounded lock
// transfers commands from the game thread; no Unity calls, IO or allocations here.
internal sealed class MixBus
{
    private sealed class Slot { public float[]? Samples; public int Cursor; public float Gain; }
    private readonly object _sync = new object();
    private readonly Slot[] _slots = new Slot[Limits.ReactionCount];
    private readonly int _sampleRate;
    private int _active;
    public MixBus(int sampleRate)
    {
        _sampleRate = sampleRate;
        for (int i = 0; i < _slots.Length; i++) _slots[i] = new Slot();
    }
    public int ActiveCount => Volatile.Read(ref _active);
    public bool IsActive(ReactionId id) { lock (_sync) return _slots[(int)id].Samples != null; }
    public bool TryStart(ReactionId id, float[] samples, float gain)
    {
        if (samples.Length == 0) return false;
        lock (_sync)
        {
            Slot slot = _slots[(int)id];
            if (slot.Samples != null) return false;
            slot.Samples = samples; slot.Cursor = 0; slot.Gain = gain;
            Volatile.Write(ref _active, _active + 1);
            return true;
        }
    }
    public void Clear()
    {
        lock (_sync)
        {
            foreach (Slot slot in _slots) { slot.Samples = null; slot.Cursor = 0; }
            Volatile.Write(ref _active, 0);
        }
    }
    public void Render(float[] buffer, int channels, bool microphoneAllowed, float masterGain)
    {
        if (channels <= 0 || buffer.Length % channels != 0) throw new ArgumentException("Invalid output shape.");
        lock (_sync)
        {
            int count = _active;
            if (count == 0)
            {
                if (!microphoneAllowed) Array.Clear(buffer, 0, buffer.Length);
                return;
            }
            float busGain = masterGain / (float)Math.Sqrt(count);
            int fadeIn = Math.Max(1, _sampleRate / 200); // 5ms
            int fadeOut = Math.Max(1, _sampleRate / 50); // 20ms
            float peak = 0;
            for (int frame = 0; frame < buffer.Length / channels; frame++)
            {
                float effect = 0;
                foreach (Slot slot in _slots)
                {
                    float[]? samples = slot.Samples;
                    if (samples == null) continue;
                    int cursor = slot.Cursor;
                    float envelope = Math.Min(1f, Math.Min((cursor + 1f) / fadeIn, (samples.Length - cursor) / (float)fadeOut));
                    effect += samples[cursor] * slot.Gain * envelope;
                    if (++slot.Cursor == samples.Length) { slot.Samples = null; _active--; }
                }
                effect *= busGain;
                for (int c = 0; c < channels; c++)
                {
                    int index = frame * channels + c;
                    float mic = microphoneAllowed ? Limits.Clamp(buffer[index], -1, 1, 0) : 0;
                    buffer[index] = mic + effect;
                    peak = Math.Max(peak, Math.Abs(buffer[index]));
                }
            }
            if (peak > 0.98f)
            {
                float gain = 0.98f / peak;
                for (int i = 0; i < buffer.Length; i++) buffer[i] *= gain;
            }
            Volatile.Write(ref _active, _active);
        }
    }
}
