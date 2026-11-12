using System;
using System.Threading;
using PeakReactionAudio.Core;
using UnityEngine;

namespace PeakReactionAudio;

internal sealed class LocalMonitor : IDisposable
{
    private readonly AudioSource _source;
    private readonly AudioClip _clip;
    private float _gain = 0.5f;
    private int _fault;
    public LocalMonitor(GameObject host, MixBus bus, int sampleRate)
    {
        Bus = bus;
        _source = host.AddComponent<AudioSource>();
        try
        {
            _source.playOnAwake = false;
            _source.loop = true;
            _source.spatialBlend = 0;
            _clip = AudioClip.Create("PeakReactionAudio monitor (effects only)", sampleRate, 1,
                sampleRate, true, Read);
            _source.clip = _clip;
            _source.Play();
        }
        catch { Dispose(); throw; }
    }
    public MixBus Bus { get; }
    public bool Faulted => Volatile.Read(ref _fault) != 0;
    public void SetGain(float gain) => Volatile.Write(ref _gain, gain);
    private void Read(float[] data)
    {
        try { Bus.Render(data, 1, false, Volatile.Read(ref _gain)); }
        catch { Array.Clear(data, 0, data.Length); Volatile.Write(ref _fault, 1); }
    }
    public void Dispose()
    {
        Bus.Clear();
        if (_source != null) { _source.Stop(); UnityEngine.Object.Destroy(_source); }
        if (_clip != null) UnityEngine.Object.Destroy(_clip);
    }
}
