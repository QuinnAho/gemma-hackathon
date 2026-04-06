using System;

namespace GemmaHackathon.SimulationFramework
{
    [Serializable]
    public sealed class SimulationRuntimeCapabilities
    {
        public bool SupportsTextCompletion = true;
        public bool SupportsToolCalling = true;
        public bool SupportsSpeechTranscription;
        public bool UsesLiveModel;
        public bool IsTargetRuntime;
    }
}
