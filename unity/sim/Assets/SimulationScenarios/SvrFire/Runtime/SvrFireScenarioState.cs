using System;
using System.Collections.Generic;
using System.Globalization;
using GemmaHackathon.SimulationFramework;

namespace GemmaHackathon.SimulationScenarios.SvrFire
{
    internal sealed partial class SvrFireScenarioState
    {
        private readonly object _syncRoot = new object();
        private readonly Action<AuditEvent> _auditEventSink;
        private readonly List<AuditEvent> _auditEvents = new List<AuditEvent>();
        private readonly List<SimulationActionRecord> _actions = new List<SimulationActionRecord>();
        private readonly HashSet<string> _criticalErrorCodes = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _routeAvailability =
            new Dictionary<string, bool>(StringComparer.Ordinal);

        private AuditSessionRecord _sessionRecord = new AuditSessionRecord();
        private string _sessionState = SvrFireScenarioValues.SessionStateReady;
        private string _phase = SvrFireScenarioValues.PhaseNormal;
        private bool _alarmActive;
        private float? _alarmTriggeredAtSeconds;
        private string _participantLocation = SvrFireScenarioValues.LocationWorkstation;
        private string _hazardState = SvrFireScenarioValues.HazardNone;
        private string _coworkerState = SvrFireScenarioValues.CoworkerNearby;
        private float? _alarmAcknowledgedAtSeconds;
        private float? _evacuationStartedAtSeconds;
        private string _variantId = SvrFireScenarioValues.DefaultVariantId;
        private string _selectedRouteId = string.Empty;
        private string _lastParticipantAction = string.Empty;
        private string _lastScenarioEvent = string.Empty;
        private string _lastAnnotation = string.Empty;
        private string _lastAssistantResponse = string.Empty;
        private string _lastFreeformInput = string.Empty;

        public SvrFireScenarioState(Action<AuditEvent> auditEventSink)
        {
            _auditEventSink = auditEventSink;
            ResetStateLocked();
        }

        public void ConfigureSession(AuditSessionRecord sessionRecord)
        {
            lock (_syncRoot)
            {
                _sessionRecord = CloneSessionRecord(sessionRecord);
                if (string.IsNullOrWhiteSpace(_sessionRecord.ScenarioVariant))
                {
                    _sessionRecord.ScenarioVariant = SvrFireScenarioValues.DefaultVariantId;
                }

                if (string.IsNullOrWhiteSpace(_sessionRecord.RubricVersion))
                {
                    _sessionRecord.RubricVersion = SvrFireScenarioValues.DefaultRubricVersion;
                }

                if (string.IsNullOrWhiteSpace(_sessionRecord.ScoringVersion))
                {
                    _sessionRecord.ScoringVersion = SvrFireScenarioValues.DefaultScoringVersion;
                }

                ResetStateLocked();
            }
        }

        public void UpdateRuntimeBackend(string runtimeBackend)
        {
            lock (_syncRoot)
            {
                _sessionRecord.RuntimeBackend = runtimeBackend ?? string.Empty;
            }
        }

        public void ResetToReadyState()
        {
            lock (_syncRoot)
            {
                ResetStateLocked();
            }
        }

        public bool StartSession(float elapsedSeconds)
        {
            lock (_syncRoot)
            {
                if (IsSessionRunningLocked())
                {
                    return false;
                }

                ResetStateLocked();
                _sessionState = SvrFireScenarioValues.SessionStateRunning;
                RecordAuditEventLocked(
                    "system",
                    "scenario_start",
                    "system",
                    SvrFireScenarioValues.ScenarioId,
                    "started",
                    AuditEventJson.CreateObject(
                        new KeyValuePair<string, string>("scenario_id", SvrFireScenarioValues.ScenarioId),
                        new KeyValuePair<string, string>("variant_id", _variantId)),
                    elapsedSeconds);
                AddActionLocked("system", "scenario_start", "SVR office fire session started.", elapsedSeconds);
                return true;
            }
        }

        public void SetLastAssistantResponse(string response)
        {
            lock (_syncRoot)
            {
                _lastAssistantResponse = response ?? string.Empty;
            }
        }

        public void SetLastFreeformInput(string input)
        {
            lock (_syncRoot)
            {
                _lastFreeformInput = input ?? string.Empty;
            }
        }

        public void RecordRuntimeNote(string actor, string verb, string details, float elapsedSeconds)
        {
            lock (_syncRoot)
            {
                AddActionLocked(actor, verb, details, elapsedSeconds);
            }
        }

        public ParticipantAction RecordParticipantAction(ParticipantAction action, float elapsedSeconds)
        {
            var safeAction = NormalizeParticipantAction(action);
            lock (_syncRoot)
            {
                if (!IsSessionRunningLocked())
                {
                    return null;
                }

                EnsureTimeBasedCriticalFailuresLocked(elapsedSeconds);
                ApplyParticipantActionLocked(safeAction, elapsedSeconds);
                return safeAction.Clone();
            }
        }

        public bool TriggerAlarmEscalation(string hazardState, string announcement, float elapsedSeconds)
        {
            lock (_syncRoot)
            {
                if (!IsSessionRunningLocked() || _alarmActive)
                {
                    return false;
                }

                ApplyHazardEscalationLocked(
                    hazardState,
                    announcement,
                    elapsedSeconds,
                    "system",
                    "alarm_triggered",
                    "system",
                    "triggered");
                return true;
            }
        }

        public SvrFireScenarioStatusSnapshot CaptureStatusSnapshot(float elapsedSeconds)
        {
            lock (_syncRoot)
            {
                var snapshot = CaptureScenarioSnapshotLocked(elapsedSeconds);
                return new SvrFireScenarioStatusSnapshot
                {
                    SessionState = snapshot.SessionState,
                    Phase = snapshot.Phase,
                    ParticipantLocation = snapshot.ParticipantLocation,
                    HazardState = snapshot.HazardState,
                    CoworkerState = snapshot.CoworkerState,
                    LastParticipantAction = snapshot.LastParticipantAction,
                    LastScenarioEvent = snapshot.LastScenarioEvent,
                    LastAnnotation = snapshot.LastAnnotation,
                    LastAssistantResponse = snapshot.LastAssistantResponse,
                    LastFreeformInput = snapshot.LastFreeformInput,
                    AuditEventCount = snapshot.AuditEvents.Count,
                    ReadinessScore = SvrFireReadinessScorer.Calculate(snapshot)
                };
            }
        }

        public IReadOnlyList<SimulationChecklistItem> GetChecklistSnapshot(float elapsedSeconds)
        {
            lock (_syncRoot)
            {
                return SvrFireReadinessScorer.BuildChecklist(CaptureScenarioSnapshotLocked(elapsedSeconds));
            }
        }

        public IReadOnlyList<SimulationActionRecord> GetActionsSnapshot()
        {
            lock (_syncRoot)
            {
                var clone = new List<SimulationActionRecord>(_actions.Count);
                for (var i = 0; i < _actions.Count; i++)
                {
                    clone.Add(CloneAction(_actions[i]));
                }

                return clone;
            }
        }

        public SimulationStateSnapshot CreateSimulationStateSnapshot(float elapsedSeconds)
        {
            lock (_syncRoot)
            {
                var snapshot = CaptureScenarioSnapshotLocked(elapsedSeconds);
                var stateSnapshot = new SimulationStateSnapshot();
                stateSnapshot.SimulationId = SvrFireScenarioValues.SimulationId;
                stateSnapshot.ScenarioId = SvrFireScenarioValues.ScenarioId;
                stateSnapshot.ElapsedSeconds = snapshot.ElapsedSeconds;

                stateSnapshot.Entries.Add(CreateStringEntry("session", "variant_id", snapshot.VariantId));
                stateSnapshot.Entries.Add(CreateStringEntry("session", "session_state", snapshot.SessionState));
                stateSnapshot.Entries.Add(CreateStringEntry("status", "phase", snapshot.Phase));
                stateSnapshot.Entries.Add(CreateBoolEntry("status", "alarm_active", snapshot.AlarmActive));
                stateSnapshot.Entries.Add(CreateStringEntry("status", "participant_location", snapshot.ParticipantLocation));
                stateSnapshot.Entries.Add(CreateStringEntry("status", "hazard_state", snapshot.HazardState));
                stateSnapshot.Entries.Add(CreateStringEntry("status", "coworker_state", snapshot.CoworkerState));
                stateSnapshot.Entries.Add(CreateStringEntry("status", "selected_route", snapshot.SelectedRouteId));
                stateSnapshot.Entries.Add(CreateStringEntry("status", "last_participant_action", snapshot.LastParticipantAction));
                stateSnapshot.Entries.Add(CreateStringEntry("status", "last_scenario_event", snapshot.LastScenarioEvent));
                stateSnapshot.Entries.Add(CreateStringEntry("status", "last_annotation", snapshot.LastAnnotation));
                stateSnapshot.Entries.Add(CreateNullableFloatEntry("timing", "alarm_triggered_at_seconds", snapshot.AlarmTriggeredAtSeconds));
                stateSnapshot.Entries.Add(CreateNullableFloatEntry("timing", "alarm_acknowledged_at_seconds", snapshot.AlarmAcknowledgedAtSeconds));
                stateSnapshot.Entries.Add(CreateNullableFloatEntry("timing", "evacuation_started_at_seconds", snapshot.EvacuationStartedAtSeconds));
                stateSnapshot.Entries.Add(CreateObjectEntry("routes", "availability", BuildRouteAvailabilityJson(snapshot.RouteAvailability)));
                stateSnapshot.Entries.Add(CreateNumberEntry("audit", "event_count", snapshot.AuditEvents.Count));

                var checklist = SvrFireReadinessScorer.BuildChecklist(snapshot);
                for (var i = 0; i < checklist.Count; i++)
                {
                    stateSnapshot.Checklist.Add(CloneChecklistItem(checklist[i]));
                }

                for (var i = 0; i < _actions.Count; i++)
                {
                    stateSnapshot.RecentActions.Add(CloneAction(_actions[i]));
                }

                return stateSnapshot;
            }
        }

        public SimulationKpiSnapshot CaptureKpis(float elapsedSeconds)
        {
            lock (_syncRoot)
            {
                var snapshot = CaptureScenarioSnapshotLocked(elapsedSeconds);
                var score = SvrFireReadinessScorer.Calculate(snapshot);
                var checklist = SvrFireReadinessScorer.BuildChecklist(snapshot);

                var completedChecklistCount = 0;
                for (var i = 0; i < checklist.Count; i++)
                {
                    if (checklist[i] != null && checklist[i].Completed)
                    {
                        completedChecklistCount++;
                    }
                }

                var kpis = new SimulationKpiSnapshot();
                kpis.Entries.Add(CreateNumberKpi("readiness_score", "Readiness Score", score.TotalPoints));
                kpis.Entries.Add(CreateNumberKpi("readiness_max", "Readiness Max", score.MaxPoints));
                kpis.Entries.Add(CreateStringKpi("readiness_band", "Readiness Band", score.Band));
                kpis.Entries.Add(CreateNumberKpi("audit_event_count", "Audit Events", snapshot.AuditEvents.Count));
                kpis.Entries.Add(CreateNumberKpi("critical_failure_count", "Critical Failures", score.CriticalFailures.Count));
                kpis.Entries.Add(CreateNumberKpi("checklist_completed", "Checklist Completed", completedChecklistCount));
                kpis.Entries.Add(CreateNumberKpi("checklist_total", "Checklist Total", checklist.Count));
                kpis.Entries.Add(CreateStringKpi("participant_location", "Participant Location", snapshot.ParticipantLocation));
                kpis.Entries.Add(CreateStringKpi("hazard_state", "Hazard State", snapshot.HazardState));
                return kpis;
            }
        }

        private SvrFireScenarioSnapshot CaptureScenarioSnapshotLocked(float elapsedSeconds)
        {
            EnsureTimeBasedCriticalFailuresLocked(elapsedSeconds);

            var snapshot = new SvrFireScenarioSnapshot();
            snapshot.SessionRecord = CloneSessionRecord(_sessionRecord);
            snapshot.SessionState = _sessionState;
            snapshot.Phase = _phase;
            snapshot.AlarmActive = _alarmActive;
            snapshot.AlarmTriggeredAtSeconds = _alarmTriggeredAtSeconds;
            snapshot.ParticipantLocation = _participantLocation;
            snapshot.HazardState = _hazardState;
            snapshot.CoworkerState = _coworkerState;
            snapshot.AlarmAcknowledgedAtSeconds = _alarmAcknowledgedAtSeconds;
            snapshot.EvacuationStartedAtSeconds = _evacuationStartedAtSeconds;
            snapshot.VariantId = _variantId;
            snapshot.SelectedRouteId = _selectedRouteId;
            snapshot.LastParticipantAction = _lastParticipantAction;
            snapshot.LastScenarioEvent = _lastScenarioEvent;
            snapshot.LastAnnotation = _lastAnnotation;
            snapshot.LastAssistantResponse = _lastAssistantResponse;
            snapshot.LastFreeformInput = _lastFreeformInput;
            snapshot.ElapsedSeconds = elapsedSeconds;

            foreach (var pair in _routeAvailability)
            {
                snapshot.RouteAvailability[pair.Key] = pair.Value;
            }

            for (var i = 0; i < _auditEvents.Count; i++)
            {
                snapshot.AuditEvents.Add(_auditEvents[i].Clone());
            }

            foreach (var code in _criticalErrorCodes)
            {
                snapshot.CriticalErrorCodes.Add(code);
            }

            return snapshot;
        }

        private void RecordAuditEventLocked(
            string source,
            string actionCode,
            string actor,
            string target,
            string outcome,
            string dataJson,
            float elapsedSeconds)
        {
            var auditEvent = new AuditEvent
            {
                SessionId = _sessionRecord.SessionId ?? string.Empty,
                EventId = SimulationRunLogging.CreateIdentifier("audit"),
                ScenarioVariant = string.IsNullOrWhiteSpace(_sessionRecord.ScenarioVariant)
                    ? _variantId
                    : _sessionRecord.ScenarioVariant,
                RubricVersion = _sessionRecord.RubricVersion ?? string.Empty,
                ScoringVersion = _sessionRecord.ScoringVersion ?? string.Empty,
                TimestampUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                ElapsedMilliseconds = (int)Math.Round(elapsedSeconds * 1000f, MidpointRounding.AwayFromZero),
                Phase = _phase ?? string.Empty,
                Source = source ?? string.Empty,
                ActionCode = actionCode ?? string.Empty,
                Actor = actor ?? string.Empty,
                Target = target ?? string.Empty,
                Outcome = outcome ?? string.Empty,
                DataJson = string.IsNullOrWhiteSpace(dataJson) ? "{}" : dataJson
            };

            _auditEvents.Add(auditEvent);
            while (_auditEvents.Count > 256)
            {
                _auditEvents.RemoveAt(0);
            }

            if (_auditEventSink == null)
            {
                return;
            }

            try
            {
                _auditEventSink(auditEvent.Clone());
            }
            catch
            {
            }
        }

        private void ApplyHazardEscalationLocked(
            string hazardState,
            string announcement,
            float elapsedSeconds,
            string source,
            string actionCode,
            string actor,
            string outcome)
        {
            EnsureTimeBasedCriticalFailuresLocked(elapsedSeconds);
            var normalizedHazard = string.IsNullOrWhiteSpace(hazardState)
                ? SvrFireScenarioValues.HazardAlarmAndSmokeExitA
                : hazardState.Trim();

            if (!_alarmActive || !_alarmTriggeredAtSeconds.HasValue)
            {
                _alarmTriggeredAtSeconds = elapsedSeconds;
            }

            _alarmActive = true;
            _phase = SvrFireScenarioValues.PhaseAlarm;
            _hazardState = normalizedHazard;
            if (string.Equals(normalizedHazard, SvrFireScenarioValues.HazardAlarmAndSmokeExitA, StringComparison.Ordinal))
            {
                _routeAvailability["exit_a"] = false;
                _routeAvailability["exit_b"] = true;
            }
            else if (string.Equals(normalizedHazard, SvrFireScenarioValues.HazardAlarmAndSmokeExitB, StringComparison.Ordinal))
            {
                _routeAvailability["exit_a"] = true;
                _routeAvailability["exit_b"] = false;
            }
            else
            {
                _routeAvailability["exit_a"] = true;
                _routeAvailability["exit_b"] = true;
            }

            if (string.Equals(_coworkerState, SvrFireScenarioValues.CoworkerNearby, StringComparison.Ordinal))
            {
                _coworkerState = SvrFireScenarioValues.CoworkerNeedsHelp;
            }

            var detail = string.IsNullOrWhiteSpace(announcement)
                ? "Hazard escalated to " + normalizedHazard + "."
                : announcement;
            _lastScenarioEvent = detail;

            var actionActor = string.Equals(source, "system", StringComparison.Ordinal)
                ? "system"
                : "ai_tool";
            AddActionLocked(actionActor, actionCode, detail, elapsedSeconds);
            RecordAuditEventLocked(
                source,
                actionCode,
                actor,
                normalizedHazard,
                outcome,
                AuditEventJson.CreateObject(
                    new KeyValuePair<string, string>("hazard_state", normalizedHazard),
                    new KeyValuePair<string, string>("announcement", detail)),
                elapsedSeconds);
        }

        private bool IsSessionRunningLocked()
        {
            return string.Equals(_sessionState, SvrFireScenarioValues.SessionStateRunning, StringComparison.Ordinal);
        }

        private void CompleteSessionLocked(
            string details,
            string target,
            string outcome,
            float elapsedSeconds,
            string dataJson)
        {
            if (string.Equals(_sessionState, SvrFireScenarioValues.SessionStateComplete, StringComparison.Ordinal))
            {
                return;
            }

            _sessionState = SvrFireScenarioValues.SessionStateComplete;
            _phase = SvrFireScenarioValues.PhaseComplete;

            var safeDetails = string.IsNullOrWhiteSpace(details)
                ? "SVR office fire session completed."
                : details.Trim();
            _lastScenarioEvent = safeDetails;
            AddActionLocked("system", "scenario_end", safeDetails, elapsedSeconds);
            RecordAuditEventLocked(
                "system",
                "scenario_end",
                "system",
                string.IsNullOrWhiteSpace(target) ? "scenario" : target,
                string.IsNullOrWhiteSpace(outcome) ? "completed" : outcome,
                string.IsNullOrWhiteSpace(dataJson)
                    ? AuditEventJson.CreateObject(new KeyValuePair<string, string>("details", safeDetails))
                    : dataJson,
                elapsedSeconds);
        }

        private void ResetStateLocked()
        {
            _sessionState = SvrFireScenarioValues.SessionStateReady;
            _phase = SvrFireScenarioValues.PhaseNormal;
            _alarmActive = false;
            _alarmTriggeredAtSeconds = null;
            _participantLocation = SvrFireScenarioValues.LocationWorkstation;
            _hazardState = SvrFireScenarioValues.HazardNone;
            _coworkerState = SvrFireScenarioValues.CoworkerNearby;
            _alarmAcknowledgedAtSeconds = null;
            _evacuationStartedAtSeconds = null;
            _variantId = string.IsNullOrWhiteSpace(_sessionRecord.ScenarioVariant)
                ? SvrFireScenarioValues.DefaultVariantId
                : _sessionRecord.ScenarioVariant;
            _selectedRouteId = string.Empty;
            _lastParticipantAction = string.Empty;
            _lastScenarioEvent = "Normal workstation activity.";
            _lastAnnotation = string.Empty;
            _lastAssistantResponse = string.Empty;
            _lastFreeformInput = string.Empty;
            _auditEvents.Clear();
            _actions.Clear();
            _criticalErrorCodes.Clear();
            _routeAvailability.Clear();
            _routeAvailability["exit_a"] = true;
            _routeAvailability["exit_b"] = true;
        }

        private void AddActionLocked(string actor, string verb, string details, float occurredAtSeconds)
        {
            _actions.Insert(0, new SimulationActionRecord
            {
                Actor = actor ?? string.Empty,
                Verb = verb ?? string.Empty,
                Details = details ?? string.Empty,
                OccurredAtSeconds = occurredAtSeconds
            });

            while (_actions.Count > 24)
            {
                _actions.RemoveAt(_actions.Count - 1);
            }
        }
    }
}
