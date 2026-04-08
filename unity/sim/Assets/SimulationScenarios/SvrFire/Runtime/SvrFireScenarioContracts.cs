using System.Collections.Generic;
using GemmaHackathon.SimulationFramework;

namespace GemmaHackathon.SimulationScenarios.SvrFire
{
    internal sealed class SvrFireReadinessScore
    {
        public int TotalPoints;
        public int MaxPoints;
        public string Band = string.Empty;
        public Dictionary<string, int> MetricScores = new Dictionary<string, int>(System.StringComparer.Ordinal);
        public List<string> CriticalFailures = new List<string>();
    }

    internal sealed class SvrFireScenarioSnapshot
    {
        public AuditSessionRecord SessionRecord = new AuditSessionRecord();
        public string SessionState = SvrFireScenarioValues.SessionStateReady;
        public string Phase = string.Empty;
        public bool AlarmActive;
        public float? AlarmTriggeredAtSeconds;
        public string ParticipantLocation = string.Empty;
        public string HazardState = string.Empty;
        public Dictionary<string, bool> RouteAvailability = new Dictionary<string, bool>(System.StringComparer.Ordinal);
        public string CoworkerState = string.Empty;
        public float? AlarmAcknowledgedAtSeconds;
        public float? EvacuationStartedAtSeconds;
        public string VariantId = string.Empty;
        public string SelectedRouteId = string.Empty;
        public string LastParticipantAction = string.Empty;
        public string LastScenarioEvent = string.Empty;
        public string LastAnnotation = string.Empty;
        public string LastAssistantResponse = string.Empty;
        public string LastFreeformInput = string.Empty;
        public float ElapsedSeconds;
        public List<AuditEvent> AuditEvents = new List<AuditEvent>();
        public List<string> CriticalErrorCodes = new List<string>();
    }

    internal sealed class SvrFireScenarioStatusSnapshot
    {
        public string SessionState = SvrFireScenarioValues.SessionStateReady;
        public string Phase = string.Empty;
        public string ParticipantLocation = string.Empty;
        public string HazardState = string.Empty;
        public string CoworkerState = string.Empty;
        public string LastParticipantAction = string.Empty;
        public string LastScenarioEvent = string.Empty;
        public string LastAnnotation = string.Empty;
        public string LastAssistantResponse = string.Empty;
        public string LastFreeformInput = string.Empty;
        public int AuditEventCount;
        public SvrFireReadinessScore ReadinessScore = new SvrFireReadinessScore();
    }
}
