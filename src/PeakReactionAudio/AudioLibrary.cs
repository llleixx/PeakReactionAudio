using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BepInEx.Logging;
using PeakReactionAudio.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace PeakReactionAudio;

internal sealed class AudioAsset
{
    public AudioAsset(string path, int rate, float[] mono) { Path = path; SourceRate = rate; Mono = mono; Variants[rate] = mono; }
    public string Path { get; }
    public int SourceRate { get; }
    public float[] Mono { get; }
    public Dictionary<int, float[]> Variants { get; } = new Dictionary<int, float[]>();
}

internal sealed class AudioLibrary
{
    private readonly ManualLogSource _log;
    private readonly List<AudioAsset>[] _clips = new List<AudioAsset>[Limits.ReactionCount];
    private readonly AudioAsset?[] _previous = new AudioAsset?[Limits.ReactionCount];
    private readonly System.Random _random = new System.Random();
    private readonly PcmBudget _budget = new PcmBudget();
    private readonly Queue<int> _rates = new Queue<int>();
    private readonly HashSet<int> _requestedRates = new HashSet<int>();
    private readonly HashSet<int> _preparedRates = new HashSet<int>();
    public AudioLibrary(ManualLogSource log)
    { _log = log; for (int i = 0; i < _clips.Length; i++) _clips[i] = new List<AudioAsset>(); }
    public bool Ready { get; private set; }

    public IEnumerator Load(string root)
    {
        foreach (ReactionId id in Enum.GetValues(typeof(ReactionId)))
        {
            string directory = System.IO.Path.Combine(root, id.ToString());
            Directory.CreateDirectory(directory);
            string[] files = Directory.GetFiles(directory);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                AudioType type;
                switch (System.IO.Path.GetExtension(file).ToLowerInvariant())
                { case ".wav": type = AudioType.WAV; break; case ".ogg": type = AudioType.OGGVORBIS; break;
                  case ".mp3": type = AudioType.MPEG; break; default: continue; }
                UnityWebRequest? request = null;
                try { request = UnityWebRequestMultimedia.GetAudioClip(new Uri(System.IO.Path.GetFullPath(file)).AbsoluteUri, type); }
                catch (Exception ex) { _log.LogWarning($"Cannot open {file}: {ex.Message}"); }
                if (request == null) continue;
                using (request)
                {
                    yield return request.SendWebRequest();
                    if (request.result != UnityWebRequest.Result.Success)
                    { _log.LogWarning($"Skipped audio {file}: {request.error}"); continue; }
                    AudioClip? clip = null;
                    try
                    {
                        clip = DownloadHandlerAudioClip.GetContent(request);
                        if (clip == null || clip.samples <= 0 || clip.channels <= 0 || clip.frequency <= 0 ||
                            (double)clip.samples / clip.frequency > Limits.MaxClipSeconds)
                            throw new InvalidDataException("Invalid audio or duration exceeds 60 seconds.");
                        long rawBytes = (long)clip.samples * clip.channels * sizeof(float);
                        long monoBytes = (long)clip.samples * sizeof(float);
                        if (rawBytes > Limits.CacheBytes || !_budget.CanFit(monoBytes))
                            throw new InvalidDataException("128 MiB PCM budget exceeded.");
                        var raw = new float[checked(clip.samples * clip.channels)];
                        if (!clip.GetData(raw, 0)) throw new InvalidDataException("Unity could not read decoded PCM.");
                        float[] mono = Pcm.Downmix(raw, clip.channels);
                        _clips[(int)id].Add(new AudioAsset(file, clip.frequency, mono));
                        _budget.Reserve(monoBytes);
                    }
                    catch (Exception ex) { _log.LogWarning($"Skipped audio {file}: {ex.Message}"); }
                    finally { if (clip != null) UnityEngine.Object.Destroy(clip); }
                }
            }
            _log.LogInfo($"{id}: {_clips[(int)id].Count} valid audio file(s).");
        }
        Ready = true;
        _log.LogInfo($"Audio scan completed; {_budget.UsedBytes / 1024} KiB source PCM. No audio is sent until a local event fires.");
    }

    public void RequestRate(int rate) { if (rate > 0 && _requestedRates.Add(rate)) _rates.Enqueue(rate); }
    public bool IsPrepared(int rate) => _preparedRates.Contains(rate);
    public IEnumerator PrepareRequestedRates()
    {
        while (true)
        {
            if (!Ready || _rates.Count == 0) { yield return null; continue; }
            int rate = _rates.Dequeue();
            yield return PrepareRate(rate);
            _preparedRates.Add(rate);
            _log.LogInfo($"Reaction PCM prepared: {rate} Hz; cache={_budget.UsedBytes / 1024} KiB.");
        }
    }
    private IEnumerator PrepareRate(int rate)
    {
        if (rate <= 0) yield break;
        foreach (List<AudioAsset> list in _clips)
        foreach (AudioAsset clip in list)
        {
            if (clip.Variants.ContainsKey(rate)) continue;
            long expectedBytes = (long)Math.Max(1, Math.Round((double)clip.Mono.Length * rate / clip.SourceRate)) * sizeof(float);
            if (!_budget.CanFit(expectedBytes))
            { _log.LogWarning($"Cannot prepare {rate} Hz PCM (cache full): {clip.Path}"); continue; }
            // Conversion is pure PCM work. Keep long/custom clips from blocking
            // Unity's update thread and interrupting vanilla microphone service.
            Task<float[]> conversion = Task.Run(() => Pcm.Resample(clip.Mono, clip.SourceRate, rate));
            while (!conversion.IsCompleted) yield return null;
            if (conversion.IsFaulted || conversion.IsCanceled)
            { _log.LogWarning($"Cannot resample {clip.Path}: {conversion.Exception?.GetBaseException().Message}"); continue; }
            float[] samples = conversion.Result;
            clip.Variants.Add(rate, samples);
            _budget.Reserve((long)samples.Length * sizeof(float));
        }
    }

    public AudioAsset? Select(ReactionId id, int rate)
    {
        List<AudioAsset> list = _clips[(int)id];
        int count = 0, previousIndex = -1;
        foreach (AudioAsset clip in list)
            if (clip.Variants.ContainsKey(rate))
            {
                if (clip == _previous[(int)id]) previousIndex = count;
                count++;
            }
        if (count == 0) return null;
        int choice = ClipChoice.Next(count, previousIndex, _random);
        foreach (AudioAsset clip in list)
        {
            if (!clip.Variants.ContainsKey(rate)) continue;
            if (choice-- == 0) return clip;
        }
        return null;
    }
    public void Accepted(ReactionId id, AudioAsset asset) => _previous[(int)id] = asset;
}
