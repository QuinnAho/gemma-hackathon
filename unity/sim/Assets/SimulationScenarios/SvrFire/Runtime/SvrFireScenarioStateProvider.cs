using System;
using System.Collections.Generic;
using GemmaHackathon.SimulationFramework;

namespace GemmaHackathon.SimulationScenarios.SvrFire
{
    internal sealed class SvrFireScenarioStateProvider : ISimulationStateProvider, ISimulationKpiProvider
    {
        private readonly SvrFireScenarioState _state;
        private readonly Func<float> _clock;

        public SvrFireScenarioStateProvider(SvrFireScenarioState state, Func<float> clock)
        {
            _state = state;
            _clock = clock;
        }

        public SimulationStateSnapshot CaptureState()
        {
            return _state == null
                ? new SimulationStateSnapshot()
                : _state.CreateSimulationStateSnapshot(GetElapsedSeconds());
        }

        public SimulationKpiSnapshot CaptureKpis()
        {
            return _state == null
                ? new SimulationKpiSnapshot()
                : _state.CaptureKpis(GetElapsedSeconds());
        }

        public SvrFireScenarioStatusSnapshot CaptureStatus()
        {
            return _state == null
                ? new SvrFireScenarioStatusSnapshot()
                : _state.CaptureStatusSnapshot(GetElapsedSeconds());
        }

        public IReadOnlyList<SimulationChecklistItem> CaptureChecklist()
        {
            return _state == null
                ? Array.Empty<SimulationChecklistItem>()
                : _state.GetChecklistSnapshot(GetElapsedSeconds());
        }

        private float GetElapsedSeconds()
        {
            return _clock == null ? 0f : _clock();
        }
    }
}
