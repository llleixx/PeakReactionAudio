namespace PeakReactionAudio.Core;

internal readonly struct TransmissionGate
{
    public TransmissionGate(bool requestedMicrophone, bool communicationAllowed, bool hasEffect)
    {
        MicrophoneAllowed = communicationAllowed && requestedMicrophone;
        TransmitEnabled = communicationAllowed && (requestedMicrophone || hasEffect);
    }
    public bool MicrophoneAllowed { get; }
    public bool TransmitEnabled { get; }
}

internal sealed class DetectionOverride
{
    private bool _voiceDetection;
    private bool _dspVad;
    public bool Active { get; private set; }
    public void Enter(bool voiceDetection, bool dspVad)
    {
        if (Active) return;
        _voiceDetection = voiceDetection; _dspVad = dspVad; Active = true;
    }
    public bool Release(out bool voiceDetection, out bool dspVad)
    {
        voiceDetection = _voiceDetection; dspVad = _dspVad;
        if (!Active) return false;
        Active = false;
        return true;
    }
}
