using System;
using System.Globalization;
using GemmaHackathon.SimulationFramework;

namespace GemmaHackathon.SimulationScenarios.SvrFire
{
    internal sealed class ScriptedSvrFireCompletionModel : ISimulationCompletionModel
    {
        private readonly SvrFireScenarioStateProvider _stateProvider;

        public ScriptedSvrFireCompletionModel(SvrFireScenarioStateProvider stateProvider)
        {
            _stateProvider = stateProvider;
        }

        public void Reset()
        {
        }

        public string CompleteJson(
            string messagesJson,
            string optionsJson = null,
            string toolsJson = null,
            Action<string, uint> tokenCallback = null,
            byte[] pcm16Mono = null,
            int responseBufferBytes = 0)
        {
            var status = _stateProvider == null
                ? new SvrFireScenarioStatusSnapshot()
                : _stateProvider.CaptureStatus();

            if (string.Equals(status.SessionState, SvrFireScenarioValues.SessionStateReady, StringComparison.Ordinal))
            {
                return BuildResponseCompletion(
                    "Assessment ready. Press Start Sim to begin the deterministic office fire drill.");
            }

            if (string.Equals(status.SessionState, SvrFireScenarioValues.SessionStateRunning, StringComparison.Ordinal))
            {
                return BuildResponseCompletion(
                    "Assessment is running in deterministic mode. Use the alarm and participant controls to continue the drill.");
            }

            return BuildResponseCompletion(BuildFollowUpSummary(status));
        }

        private static string BuildFollowUpSummary(SvrFireScenarioStatusSnapshot status)
        {
            var safeStatus = status ?? new SvrFireScenarioStatusSnapshot();
            var assessment = safeStatus.Assessment ?? new AssessmentResult();
            return "Assessment complete. Phase " +
                   (safeStatus.Phase ?? string.Empty) +
                   ". Location " +
                   (safeStatus.ParticipantLocation ?? string.Empty) +
                   ". Readiness " +
                   assessment.TotalPoints.ToString(CultureInfo.InvariantCulture) +
                   "/" +
                   assessment.MaxPoints.ToString(CultureInfo.InvariantCulture) +
                   " (" +
                   (assessment.Band ?? string.Empty) +
                   ").";
        }

        private static string BuildResponseCompletion(string response)
        {
            return "{\"success\":true,\"error\":null,\"cloud_handoff\":false,\"response\":\"" +
                   SvrFireJson.Escape(response) +
                   "\",\"function_calls\":[],\"segments\":[],\"confidence\":0.97}";
        }
    }
}
