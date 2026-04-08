using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GemmaHackathon.SimulationFramework;

namespace GemmaHackathon.SimulationScenarios.SvrFire
{
    internal static class SvrFireScenarioValues
    {
        public const string ScenarioId = "svr-office-fire-evacuation";
        public const string SimulationId = "svr";
        public const string DefaultVariantId = "office_fire_v1";
        public const string DefaultRubricVersion = "svr.fire.rubric.v1";
        public const string DefaultScoringVersion = "svr.fire.score.v1";
        public const string DefaultParticipantAlias = "participant-anonymous";

        public const string PhaseNormal = "normal";
        public const string PhaseAlarm = "alarm";
        public const string PhaseEvacuation = "evacuation";
        public const string PhaseComplete = "complete";

        public const string LocationWorkstation = "workstation";
        public const string LocationExitA = "exit_a";
        public const string LocationExitB = "exit_b";
        public const string LocationSafe = "safe";
        public const string LocationHazard = "hazard_zone";

        public const string HazardNone = "none";
        public const string HazardAlarmOnly = "alarm_only";
        public const string HazardAlarmAndSmokeExitA = "alarm_and_smoke_exit_a";
        public const string HazardAlarmAndSmokeExitB = "alarm_and_smoke_exit_b";

        public const string CoworkerNearby = "nearby";
        public const string CoworkerNeedsHelp = "needs_help";
        public const string CoworkerAssisted = "assisted";
        public const string CoworkerLeftBehind = "left_behind";

        public const string ActionAcknowledgeAlarm = "acknowledge_alarm";
        public const string ActionMoveExitA = "move_exit_a";
        public const string ActionMoveExitB = "move_exit_b";
        public const string ActionHelpCoworker = "help_coworker";
        public const string ActionAbandonScenario = "abandon_scenario";

        public const string CriticalIgnoredAlarm = "ignored_alarm_60s";
        public const string CriticalWrongExit = "wrong_exit_into_fire";
    }

    internal sealed class SvrFireReadinessScore
    {
        public int TotalPoints;
        public int MaxPoints;
        public string Band = string.Empty;
        public Dictionary<string, int> MetricScores = new Dictionary<string, int>(StringComparer.Ordinal);
        public List<string> CriticalFailures = new List<string>();
    }

    internal sealed class SvrFireScenarioSnapshot
    {
        public AuditSessionRecord SessionRecord = new AuditSessionRecord();
        public string Phase = string.Empty;
        public bool AlarmActive;
        public string ParticipantLocation = string.Empty;
        public string HazardState = string.Empty;
        public Dictionary<string, bool> RouteAvailability = new Dictionary<string, bool>(StringComparer.Ordinal);
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

    internal static class SvrFireReadinessScorer
    {
        public static SvrFireReadinessScore Calculate(SvrFireScenarioSnapshot snapshot)
        {
            var safeSnapshot = snapshot ?? new SvrFireScenarioSnapshot();
            var score = new SvrFireReadinessScore();
            score.MaxPoints = 100;

            if (ContainsCriticalFailure(safeSnapshot, SvrFireScenarioValues.CriticalIgnoredAlarm))
            {
                score.TotalPoints = 0;
                score.Band = "fail";
                score.CriticalFailures.Add(SvrFireScenarioValues.CriticalIgnoredAlarm);
                return score;
            }

            if (ContainsCriticalFailure(safeSnapshot, SvrFireScenarioValues.CriticalWrongExit))
            {
                score.TotalPoints = 0;
                score.Band = "fail";
                score.CriticalFailures.Add(SvrFireScenarioValues.CriticalWrongExit);
                return score;
            }

            AddMetric(score, "alarm_recognition", ScoreAlarmRecognition(safeSnapshot));
            AddMetric(score, "evacuation_start", ScoreEvacuationStart(safeSnapshot));
            AddMetric(score, "route_correctness", ScoreRouteCorrectness(safeSnapshot));
            AddMetric(score, "protocol_completion", ScoreProtocolCompletion(safeSnapshot));
            AddMetric(score, "critical_error_penalty", ScoreCriticalErrorPenalty(safeSnapshot));

            var total = 0;
            foreach (var pair in score.MetricScores)
            {
                total += pair.Value;
            }

            if (total < 0)
            {
                total = 0;
            }
            else if (total > score.MaxPoints)
            {
                total = score.MaxPoints;
            }

            score.TotalPoints = total;
            score.Band = total >= 70 ? "pass" : (total >= 50 ? "marginal" : "fail");
            return score;
        }

        public static List<SimulationChecklistItem> BuildChecklist(SvrFireScenarioSnapshot snapshot)
        {
            var safeSnapshot = snapshot ?? new SvrFireScenarioSnapshot();
            var checklist = new List<SimulationChecklistItem>(4);
            checklist.Add(CreateChecklistItem(
                "ack_alarm",
                "Recognize and acknowledge the alarm",
                safeSnapshot.AlarmAcknowledgedAtSeconds.HasValue,
                safeSnapshot.AlarmAcknowledgedAtSeconds.HasValue
                    ? "Acknowledged at " + safeSnapshot.AlarmAcknowledgedAtSeconds.Value.ToString("0.0", CultureInfo.InvariantCulture) + "s"
                    : "Alarm not yet acknowledged."));
            checklist.Add(CreateChecklistItem(
                "start_evacuation",
                "Begin evacuation promptly",
                safeSnapshot.EvacuationStartedAtSeconds.HasValue,
                safeSnapshot.EvacuationStartedAtSeconds.HasValue
                    ? "Started moving at " + safeSnapshot.EvacuationStartedAtSeconds.Value.ToString("0.0", CultureInfo.InvariantCulture) + "s"
                    : "Evacuation has not started."));
            checklist.Add(CreateChecklistItem(
                "choose_safe_route",
                "Choose an available exit route",
                IsSafeRouteChosen(safeSnapshot),
                IsSafeRouteChosen(safeSnapshot)
                    ? "Selected " + (safeSnapshot.SelectedRouteId ?? string.Empty) + "."
                    : "No safe route choice recorded."));
            checklist.Add(CreateChecklistItem(
                "reach_safety",
                "Reach the safe zone",
                string.Equals(safeSnapshot.ParticipantLocation, SvrFireScenarioValues.LocationSafe, StringComparison.Ordinal),
                string.Equals(safeSnapshot.ParticipantLocation, SvrFireScenarioValues.LocationSafe, StringComparison.Ordinal)
                    ? "Participant reached safety."
                    : "Participant is not yet in the safe zone."));
            return checklist;
        }

        private static bool ContainsCriticalFailure(SvrFireScenarioSnapshot snapshot, string code)
        {
            if (snapshot == null || snapshot.CriticalErrorCodes == null || string.IsNullOrWhiteSpace(code))
            {
                return false;
            }

            for (var i = 0; i < snapshot.CriticalErrorCodes.Count; i++)
            {
                if (string.Equals(snapshot.CriticalErrorCodes[i], code, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static int ScoreAlarmRecognition(SvrFireScenarioSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.AlarmAcknowledgedAtSeconds.HasValue)
            {
                return 0;
            }

            var seconds = snapshot.AlarmAcknowledgedAtSeconds.Value;
            if (seconds <= 5f)
            {
                return 25;
            }

            if (seconds <= 10f)
            {
                return 18;
            }

            if (seconds <= 20f)
            {
                return 10;
            }

            return 0;
        }

        private static int ScoreEvacuationStart(SvrFireScenarioSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.EvacuationStartedAtSeconds.HasValue)
            {
                return 0;
            }

            var seconds = snapshot.EvacuationStartedAtSeconds.Value;
            if (seconds <= 10f)
            {
                return 25;
            }

            if (seconds <= 20f)
            {
                return 18;
            }

            if (seconds <= 30f)
            {
                return 10;
            }

            return 0;
        }

        private static int ScoreRouteCorrectness(SvrFireScenarioSnapshot snapshot)
        {
            return IsSafeRouteChosen(snapshot) ? 25 : 0;
        }

        private static int ScoreProtocolCompletion(SvrFireScenarioSnapshot snapshot)
        {
            var checklist = BuildChecklist(snapshot);
            if (checklist.Count == 0)
            {
                return 0;
            }

            var completedSteps = 0;
            for (var i = 0; i < checklist.Count; i++)
            {
                if (checklist[i] != null && checklist[i].Completed)
                {
                    completedSteps++;
                }
            }

            return (int)Math.Round((completedSteps / (double)checklist.Count) * 25d, MidpointRounding.AwayFromZero);
        }

        private static int ScoreCriticalErrorPenalty(SvrFireScenarioSnapshot snapshot)
        {
            if (snapshot == null || snapshot.CriticalErrorCodes == null || snapshot.CriticalErrorCodes.Count == 0)
            {
                return 0;
            }

            return -10 * snapshot.CriticalErrorCodes.Count;
        }

        private static bool IsSafeRouteChosen(SvrFireScenarioSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.SelectedRouteId))
            {
                return false;
            }

            bool available;
            return snapshot.RouteAvailability != null &&
                   snapshot.RouteAvailability.TryGetValue(snapshot.SelectedRouteId, out available) &&
                   available &&
                   string.Equals(snapshot.ParticipantLocation, SvrFireScenarioValues.LocationSafe, StringComparison.Ordinal);
        }

        private static void AddMetric(SvrFireReadinessScore score, string key, int value)
        {
            if (score == null || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            score.MetricScores[key] = value;
        }

        private static SimulationChecklistItem CreateChecklistItem(
            string id,
            string label,
            bool completed,
            string notes)
        {
            return new SimulationChecklistItem
            {
                Id = id ?? string.Empty,
                Label = label ?? string.Empty,
                Completed = completed,
                Notes = notes ?? string.Empty
            };
        }
    }

    internal sealed class SvrFireScenarioState
    {
        private readonly object _syncRoot = new object();
        private readonly Action<AuditEvent> _auditEventSink;
        private readonly List<AuditEvent> _auditEvents = new List<AuditEvent>();
        private readonly List<SimulationActionRecord> _actions = new List<SimulationActionRecord>();
        private readonly HashSet<string> _criticalErrorCodes = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _routeAvailability =
            new Dictionary<string, bool>(StringComparer.Ordinal);

        private AuditSessionRecord _sessionRecord = new AuditSessionRecord();
        private string _phase = SvrFireScenarioValues.PhaseNormal;
        private bool _alarmActive;
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
        private bool _scenarioCompletionRequested;

        public SvrFireScenarioState(Action<AuditEvent> auditEventSink)
        {
            _auditEventSink = auditEventSink;
            ResetStateLocked();
        }

        public void ConfigureSession(AuditSessionRecord sessionRecord, float elapsedSeconds)
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
            }
        }

        public void UpdateRuntimeBackend(string runtimeBackend)
        {
            lock (_syncRoot)
            {
                _sessionRecord.RuntimeBackend = runtimeBackend ?? string.Empty;
            }
        }

        public void ResetForNewSession(float elapsedSeconds)
        {
            lock (_syncRoot)
            {
                ResetStateLocked();
                RecordAuditEventLocked(
                    "system",
                    "scenario_start",
                    "system",
                    SvrFireScenarioValues.ScenarioId,
                    "restarted",
                    AuditEventJson.CreateObject(
                        new KeyValuePair<string, string>("scenario_id", SvrFireScenarioValues.ScenarioId),
                        new KeyValuePair<string, string>("variant_id", _variantId)),
                    elapsedSeconds);
                AddActionLocked("system", "reset_session", "SVR office fire session reset.", elapsedSeconds);
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
                EnsureTimeBasedCriticalFailuresLocked(elapsedSeconds);
                ApplyParticipantActionLocked(safeAction, elapsedSeconds);
                return safeAction.Clone();
            }
        }

        public string DescribeParticipantAction(ParticipantAction action)
        {
            var safeAction = NormalizeParticipantAction(action);
            switch (safeAction.ActionCode)
            {
                case SvrFireScenarioValues.ActionAcknowledgeAlarm:
                    return "Participant acknowledged the fire alarm.";
                case SvrFireScenarioValues.ActionMoveExitA:
                    return "Participant moved toward Exit A.";
                case SvrFireScenarioValues.ActionMoveExitB:
                    return "Participant moved toward Exit B.";
                case SvrFireScenarioValues.ActionHelpCoworker:
                    return "Participant chose to help the nearby coworker.";
                case SvrFireScenarioValues.ActionAbandonScenario:
                    return "Participant abandoned the scenario.";
                default:
                    return "Participant performed action `" + (safeAction.ActionCode ?? string.Empty) + "`.";
            }
        }

        public void RecordScenarioEvent(string description, float elapsedSeconds)
        {
            var safeDescription = description ?? string.Empty;
            lock (_syncRoot)
            {
                EnsureTimeBasedCriticalFailuresLocked(elapsedSeconds);
                _lastScenarioEvent = safeDescription;
                AddActionLocked("system", "scenario_event", safeDescription, elapsedSeconds);
                RecordAuditEventLocked(
                    "system",
                    "scenario_event",
                    "system",
                    SvrFireScenarioValues.ScenarioId,
                    "recorded",
                    AuditEventJson.CreateObject(
                        new KeyValuePair<string, string>("description", safeDescription)),
                    elapsedSeconds);
            }
        }

        public void EscalateHazard(string hazardState, string announcement, float elapsedSeconds)
        {
            lock (_syncRoot)
            {
                EnsureTimeBasedCriticalFailuresLocked(elapsedSeconds);
                var normalizedHazard = string.IsNullOrWhiteSpace(hazardState)
                    ? SvrFireScenarioValues.HazardAlarmAndSmokeExitA
                    : hazardState.Trim();

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
                AddActionLocked("ai_tool", "escalate_hazard", detail, elapsedSeconds);
                RecordAuditEventLocked(
                    "ai_tool",
                    "escalate_hazard",
                    "assistant",
                    normalizedHazard,
                    "applied",
                    AuditEventJson.CreateObject(
                        new KeyValuePair<string, string>("hazard_state", normalizedHazard),
                        new KeyValuePair<string, string>("announcement", detail)),
                    elapsedSeconds);
            }
        }

        public void PromptParticipant(string message, float elapsedSeconds)
        {
            var safeMessage = string.IsNullOrWhiteSpace(message)
                ? "Proceed to the clear exit."
                : message.Trim();

            lock (_syncRoot)
            {
                AddActionLocked("ai_tool", "prompt_participant", safeMessage, elapsedSeconds);
                RecordAuditEventLocked(
                    "ai_tool",
                    "prompt_participant",
                    "assistant",
                    "participant",
                    "delivered",
                    AuditEventJson.CreateObject(
                        new KeyValuePair<string, string>("message", safeMessage)),
                    elapsedSeconds);
            }
        }

        public void ChangeEnvironmentCue(
            string routeId,
            bool? available,
            string coworkerState,
            string note,
            float elapsedSeconds)
        {
            lock (_syncRoot)
            {
                if (!string.IsNullOrWhiteSpace(routeId) && available.HasValue)
                {
                    _routeAvailability[routeId] = available.Value;
                }

                if (!string.IsNullOrWhiteSpace(coworkerState))
                {
                    _coworkerState = coworkerState.Trim();
                }

                var detail = string.IsNullOrWhiteSpace(note)
                    ? "Environment cue updated."
                    : note.Trim();
                _lastScenarioEvent = detail;
                AddActionLocked("ai_tool", "change_environment_cue", detail, elapsedSeconds);
                RecordAuditEventLocked(
                    "ai_tool",
                    "change_environment_cue",
                    "assistant",
                    string.IsNullOrWhiteSpace(routeId) ? "environment" : routeId,
                    "applied",
                    BuildEnvironmentCueData(routeId, available, coworkerState, detail),
                    elapsedSeconds);
            }
        }

        public void AnnotateContext(string note, float elapsedSeconds)
        {
            var safeNote = string.IsNullOrWhiteSpace(note)
                ? "AI context note recorded."
                : note.Trim();

            lock (_syncRoot)
            {
                _lastAnnotation = safeNote;
                AddActionLocked("ai_tool", "annotate_context", safeNote, elapsedSeconds);
                RecordAuditEventLocked(
                    "ai_tool",
                    "annotate_context",
                    "assistant",
                    "audit_context",
                    "recorded",
                    AuditEventJson.CreateObject(
                        new KeyValuePair<string, string>("note", safeNote)),
                    elapsedSeconds);
            }
        }

        public void TransitionPhase(string phase, float elapsedSeconds)
        {
            var safePhase = string.IsNullOrWhiteSpace(phase)
                ? _phase
                : phase.Trim();

            lock (_syncRoot)
            {
                _phase = safePhase;
                AddActionLocked("ai_tool", "transition_phase", safePhase, elapsedSeconds);
                RecordAuditEventLocked(
                    "ai_tool",
                    "transition_phase",
                    "assistant",
                    safePhase,
                    "applied",
                    AuditEventJson.CreateObject(
                        new KeyValuePair<string, string>("phase", safePhase)),
                    elapsedSeconds);
            }
        }

        public void RequestEndScenario(string reason, float elapsedSeconds)
        {
            var safeReason = string.IsNullOrWhiteSpace(reason)
                ? "AI requested scenario completion."
                : reason.Trim();

            lock (_syncRoot)
            {
                EnsureTimeBasedCriticalFailuresLocked(elapsedSeconds);
                var canComplete =
                    string.Equals(_participantLocation, SvrFireScenarioValues.LocationSafe, StringComparison.Ordinal) ||
                    _criticalErrorCodes.Count > 0;
                var outcome = canComplete ? "approved" : "deferred_not_terminal";
                if (canComplete)
                {
                    _scenarioCompletionRequested = true;
                    _phase = SvrFireScenarioValues.PhaseComplete;
                }

                AddActionLocked("ai_tool", "request_end_scenario", safeReason, elapsedSeconds);
                RecordAuditEventLocked(
                    "ai_tool",
                    "request_end_scenario",
                    "assistant",
                    "scenario",
                    outcome,
                    AuditEventJson.CreateObject(
                        new KeyValuePair<string, string>("reason", safeReason)),
                    elapsedSeconds);
            }
        }

        public SvrFireScenarioStatusSnapshot CaptureStatusSnapshot(float elapsedSeconds)
        {
            lock (_syncRoot)
            {
                var snapshot = CaptureScenarioSnapshotLocked(elapsedSeconds);
                return new SvrFireScenarioStatusSnapshot
                {
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
                stateSnapshot.Entries.Add(CreateStringEntry("status", "phase", snapshot.Phase));
                stateSnapshot.Entries.Add(CreateBoolEntry("status", "alarm_active", snapshot.AlarmActive));
                stateSnapshot.Entries.Add(CreateStringEntry("status", "participant_location", snapshot.ParticipantLocation));
                stateSnapshot.Entries.Add(CreateStringEntry("status", "hazard_state", snapshot.HazardState));
                stateSnapshot.Entries.Add(CreateStringEntry("status", "coworker_state", snapshot.CoworkerState));
                stateSnapshot.Entries.Add(CreateStringEntry("status", "selected_route", snapshot.SelectedRouteId));
                stateSnapshot.Entries.Add(CreateStringEntry("status", "last_participant_action", snapshot.LastParticipantAction));
                stateSnapshot.Entries.Add(CreateStringEntry("status", "last_scenario_event", snapshot.LastScenarioEvent));
                stateSnapshot.Entries.Add(CreateStringEntry("status", "last_annotation", snapshot.LastAnnotation));
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

        private void ApplyParticipantActionLocked(ParticipantAction action, float elapsedSeconds)
        {
            var safeAction = action ?? new ParticipantAction();
            var description = DescribeParticipantAction(safeAction);
            _lastParticipantAction = description;
            AddActionLocked("participant", safeAction.ActionCode, description, elapsedSeconds);

            var outcome = "recorded";
            var target = safeAction.Target ?? string.Empty;

            switch (safeAction.ActionCode)
            {
                case SvrFireScenarioValues.ActionAcknowledgeAlarm:
                    if (!_alarmActive)
                    {
                        outcome = "alarm_not_active";
                    }
                    else if (_alarmAcknowledgedAtSeconds.HasValue)
                    {
                        outcome = "already_acknowledged";
                    }
                    else
                    {
                        _alarmAcknowledgedAtSeconds = elapsedSeconds;
                        outcome = "acknowledged";
                    }

                    break;

                case SvrFireScenarioValues.ActionMoveExitA:
                case SvrFireScenarioValues.ActionMoveExitB:
                    if (!_evacuationStartedAtSeconds.HasValue)
                    {
                        _evacuationStartedAtSeconds = elapsedSeconds;
                    }

                    _phase = SvrFireScenarioValues.PhaseEvacuation;
                    _selectedRouteId = string.Equals(
                        safeAction.ActionCode,
                        SvrFireScenarioValues.ActionMoveExitA,
                        StringComparison.Ordinal)
                        ? "exit_a"
                        : "exit_b";
                    _participantLocation = _selectedRouteId;
                    target = _selectedRouteId;

                    bool routeAvailable;
                    if (_routeAvailability.TryGetValue(_selectedRouteId, out routeAvailable) && routeAvailable)
                    {
                        _participantLocation = SvrFireScenarioValues.LocationSafe;
                        _phase = SvrFireScenarioValues.PhaseComplete;
                        _scenarioCompletionRequested = true;
                        outcome = "reached_safety";
                    }
                    else
                    {
                        _participantLocation = SvrFireScenarioValues.LocationHazard;
                        _phase = SvrFireScenarioValues.PhaseComplete;
                        _scenarioCompletionRequested = true;
                        outcome = "entered_hazard";
                        EmitCriticalErrorLocked(
                            SvrFireScenarioValues.CriticalWrongExit,
                            "Participant selected a hazardous exit route.",
                            elapsedSeconds);
                    }

                    break;

                case SvrFireScenarioValues.ActionHelpCoworker:
                    if (string.Equals(_coworkerState, SvrFireScenarioValues.CoworkerNeedsHelp, StringComparison.Ordinal) ||
                        string.Equals(_coworkerState, SvrFireScenarioValues.CoworkerNearby, StringComparison.Ordinal))
                    {
                        _coworkerState = SvrFireScenarioValues.CoworkerAssisted;
                        outcome = "coworker_assisted";
                    }
                    else
                    {
                        outcome = "no_assistance_needed";
                    }

                    target = "coworker";
                    break;

                case SvrFireScenarioValues.ActionAbandonScenario:
                    _phase = SvrFireScenarioValues.PhaseComplete;
                    _scenarioCompletionRequested = true;
                    outcome = "abandoned";
                    target = "scenario";
                    break;

                default:
                    outcome = "unknown_action";
                    break;
            }

            RecordAuditEventLocked(
                "participant",
                "participant_action",
                "participant",
                target,
                outcome,
                safeAction.ToStructuredJson(),
                elapsedSeconds);
        }

        private SvrFireScenarioSnapshot CaptureScenarioSnapshotLocked(float elapsedSeconds)
        {
            EnsureTimeBasedCriticalFailuresLocked(elapsedSeconds);

            var snapshot = new SvrFireScenarioSnapshot();
            snapshot.SessionRecord = CloneSessionRecord(_sessionRecord);
            snapshot.Phase = _phase;
            snapshot.AlarmActive = _alarmActive;
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

        private void EnsureTimeBasedCriticalFailuresLocked(float elapsedSeconds)
        {
            if (_alarmActive &&
                !_alarmAcknowledgedAtSeconds.HasValue &&
                elapsedSeconds >= 60f &&
                !_criticalErrorCodes.Contains(SvrFireScenarioValues.CriticalIgnoredAlarm))
            {
                EmitCriticalErrorLocked(
                    SvrFireScenarioValues.CriticalIgnoredAlarm,
                    "Participant failed to acknowledge the alarm within 60 seconds.",
                    elapsedSeconds);
            }
        }

        private void EmitCriticalErrorLocked(string code, string details, float elapsedSeconds)
        {
            if (string.IsNullOrWhiteSpace(code) || _criticalErrorCodes.Contains(code))
            {
                return;
            }

            _criticalErrorCodes.Add(code);
            AddActionLocked("system", "critical_error", details, elapsedSeconds);
            RecordAuditEventLocked(
                "system",
                "critical_error",
                "system",
                code,
                "recorded",
                AuditEventJson.CreateObject(
                    new KeyValuePair<string, string>("code", code),
                    new KeyValuePair<string, string>("details", details ?? string.Empty)),
                elapsedSeconds);
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

        private void ResetStateLocked()
        {
            _phase = SvrFireScenarioValues.PhaseNormal;
            _alarmActive = false;
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
            _scenarioCompletionRequested = false;
            _auditEvents.Clear();
            _actions.Clear();
            _criticalErrorCodes.Clear();
            _routeAvailability.Clear();
            _routeAvailability["exit_a"] = true;
            _routeAvailability["exit_b"] = true;
        }

        private static ParticipantAction NormalizeParticipantAction(ParticipantAction action)
        {
            var safeAction = action == null ? new ParticipantAction() : action.Clone();
            safeAction.ActionCode = safeAction.ActionCode ?? string.Empty;
            safeAction.Source = string.IsNullOrWhiteSpace(safeAction.Source) ? "ui" : safeAction.Source.Trim();
            safeAction.Target = safeAction.Target ?? string.Empty;
            if (safeAction.Metadata == null)
            {
                safeAction.Metadata = new Dictionary<string, string>(StringComparer.Ordinal);
            }

            return safeAction;
        }

        private static AuditSessionRecord CloneSessionRecord(AuditSessionRecord value)
        {
            var source = value ?? new AuditSessionRecord();
            return new AuditSessionRecord
            {
                SessionId = source.SessionId ?? string.Empty,
                ParticipantAlias = source.ParticipantAlias ?? string.Empty,
                ScenarioVariant = source.ScenarioVariant ?? string.Empty,
                RubricVersion = source.RubricVersion ?? string.Empty,
                ScoringVersion = source.ScoringVersion ?? string.Empty,
                RuntimeBackend = source.RuntimeBackend ?? string.Empty,
                SessionPhase = source.SessionPhase ?? string.Empty
            };
        }

        private static string BuildRouteAvailabilityJson(Dictionary<string, bool> routeAvailability)
        {
            var builder = new StringBuilder(64);
            builder.Append('{');

            if (routeAvailability != null)
            {
                var wroteAny = false;
                foreach (var pair in routeAvailability)
                {
                    if (wroteAny)
                    {
                        builder.Append(',');
                    }

                    builder.Append('"');
                    builder.Append(SvrFireJson.Escape(pair.Key ?? string.Empty));
                    builder.Append("\":");
                    builder.Append(pair.Value ? "true" : "false");
                    wroteAny = true;
                }
            }

            builder.Append('}');
            return builder.ToString();
        }

        private static string BuildEnvironmentCueData(
            string routeId,
            bool? available,
            string coworkerState,
            string note)
        {
            var builder = new StringBuilder(160);
            builder.Append('{');
            var wroteAny = false;

            if (!string.IsNullOrWhiteSpace(routeId))
            {
                AppendJsonPair(builder, "route_id", routeId, ref wroteAny);
            }

            if (available.HasValue)
            {
                if (wroteAny)
                {
                    builder.Append(',');
                }

                builder.Append("\"available\":");
                builder.Append(available.Value ? "true" : "false");
                wroteAny = true;
            }

            if (!string.IsNullOrWhiteSpace(coworkerState))
            {
                AppendJsonPair(builder, "coworker_state", coworkerState, ref wroteAny);
            }

            if (!string.IsNullOrWhiteSpace(note))
            {
                AppendJsonPair(builder, "note", note, ref wroteAny);
            }

            builder.Append('}');
            return builder.ToString();
        }

        private static void AppendJsonPair(StringBuilder builder, string key, string value, ref bool wroteAny)
        {
            if (wroteAny)
            {
                builder.Append(',');
            }

            builder.Append('"');
            builder.Append(SvrFireJson.Escape(key ?? string.Empty));
            builder.Append("\":\"");
            builder.Append(SvrFireJson.Escape(value ?? string.Empty));
            builder.Append('"');
            wroteAny = true;
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

        private static SimulationStateEntry CreateStringEntry(string category, string key, string value)
        {
            return new SimulationStateEntry
            {
                Category = category ?? string.Empty,
                Key = key ?? string.Empty,
                ValueJson = "\"" + SvrFireJson.Escape(value ?? string.Empty) + "\""
            };
        }

        private static SimulationStateEntry CreateBoolEntry(string category, string key, bool value)
        {
            return new SimulationStateEntry
            {
                Category = category ?? string.Empty,
                Key = key ?? string.Empty,
                ValueJson = value ? "true" : "false"
            };
        }

        private static SimulationStateEntry CreateNumberEntry(string category, string key, int value)
        {
            return new SimulationStateEntry
            {
                Category = category ?? string.Empty,
                Key = key ?? string.Empty,
                ValueJson = value.ToString(CultureInfo.InvariantCulture)
            };
        }

        private static SimulationStateEntry CreateNullableFloatEntry(string category, string key, float? value)
        {
            return new SimulationStateEntry
            {
                Category = category ?? string.Empty,
                Key = key ?? string.Empty,
                ValueJson = value.HasValue
                    ? value.Value.ToString("0.###", CultureInfo.InvariantCulture)
                    : "null"
            };
        }

        private static SimulationStateEntry CreateObjectEntry(string category, string key, string valueJson)
        {
            return new SimulationStateEntry
            {
                Category = category ?? string.Empty,
                Key = key ?? string.Empty,
                ValueJson = string.IsNullOrWhiteSpace(valueJson) ? "{}" : valueJson
            };
        }

        private static SimulationKpiEntry CreateNumberKpi(string key, string label, int value)
        {
            return new SimulationKpiEntry
            {
                Key = key ?? string.Empty,
                Label = label ?? string.Empty,
                ValueJson = value.ToString(CultureInfo.InvariantCulture)
            };
        }

        private static SimulationKpiEntry CreateStringKpi(string key, string label, string value)
        {
            return new SimulationKpiEntry
            {
                Key = key ?? string.Empty,
                Label = label ?? string.Empty,
                ValueJson = "\"" + SvrFireJson.Escape(value ?? string.Empty) + "\""
            };
        }

        private static SimulationChecklistItem CloneChecklistItem(SimulationChecklistItem item)
        {
            var safeItem = item ?? new SimulationChecklistItem();
            return new SimulationChecklistItem
            {
                Id = safeItem.Id ?? string.Empty,
                Label = safeItem.Label ?? string.Empty,
                Completed = safeItem.Completed,
                Notes = safeItem.Notes ?? string.Empty
            };
        }

        private static SimulationActionRecord CloneAction(SimulationActionRecord action)
        {
            var safeAction = action ?? new SimulationActionRecord();
            return new SimulationActionRecord
            {
                Actor = safeAction.Actor ?? string.Empty,
                Verb = safeAction.Verb ?? string.Empty,
                Details = safeAction.Details ?? string.Empty,
                OccurredAtSeconds = safeAction.OccurredAtSeconds
            };
        }
    }

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
            var lastRole = ExtractLastRole(messagesJson);
            if (string.Equals(lastRole, "tool", StringComparison.Ordinal))
            {
                return BuildResponseCompletion(BuildFollowUpSummary(status));
            }

            var lastMessage = ExtractLastUserContent(messagesJson).ToLowerInvariant();
            if (lastMessage.Contains("summary") || lastMessage.Contains("status"))
            {
                return BuildResponseCompletion(BuildFollowUpSummary(status));
            }

            var functionCalls = new List<string>();
            if (string.Equals(status.Phase, SvrFireScenarioValues.PhaseNormal, StringComparison.Ordinal))
            {
                functionCalls.Add(BuildFunctionCall(
                    "escalate_hazard",
                    "{\"hazard_state\":\"alarm_and_smoke_exit_a\",\"announcement\":\"Fire alarm active. Exit A has smoke. Exit B is clear.\"}"));
                functionCalls.Add(BuildFunctionCall(
                    "prompt_participant",
                    "{\"message\":\"Fire alarm active. Exit B is clear. Evacuate now.\"}"));
                return BuildFunctionCallCompletion(functionCalls);
            }

            if (lastMessage.Contains("acknowledge") || lastMessage.Contains("alarm"))
            {
                functionCalls.Add(BuildFunctionCall(
                    "prompt_participant",
                    "{\"message\":\"Good. Move to the clear exit and leave the area.\"}"));
            }
            else if (lastMessage.Contains("exit a"))
            {
                functionCalls.Add(BuildFunctionCall(
                    "annotate_context",
                    "{\"note\":\"Participant moved toward the hazardous route.\"}"));
                functionCalls.Add(BuildFunctionCall(
                    "request_end_scenario",
                    "{\"reason\":\"Participant chose a hazardous exit route.\"}"));
            }
            else if (lastMessage.Contains("exit b"))
            {
                functionCalls.Add(BuildFunctionCall(
                    "annotate_context",
                    "{\"note\":\"Participant moved toward the clear route and reached safety.\"}"));
                functionCalls.Add(BuildFunctionCall(
                    "request_end_scenario",
                    "{\"reason\":\"Participant reached the safe zone.\"}"));
            }
            else if (lastMessage.Contains("coworker"))
            {
                functionCalls.Add(BuildFunctionCall(
                    "prompt_participant",
                    "{\"message\":\"Assist quickly if it is safe, then continue to the clear exit.\"}"));
            }
            else
            {
                functionCalls.Add(BuildFunctionCall(
                    "prompt_participant",
                    "{\"message\":\"Acknowledge the alarm and move to the clear exit.\"}"));
            }

            return BuildFunctionCallCompletion(functionCalls);
        }

        private static string BuildFollowUpSummary(SvrFireScenarioStatusSnapshot status)
        {
            var safeStatus = status ?? new SvrFireScenarioStatusSnapshot();
            var score = safeStatus.ReadinessScore ?? new SvrFireReadinessScore();
            return "Phase " +
                   (safeStatus.Phase ?? string.Empty) +
                   ". Location " +
                   (safeStatus.ParticipantLocation ?? string.Empty) +
                   ". Readiness " +
                   score.TotalPoints.ToString(CultureInfo.InvariantCulture) +
                   "/" +
                   score.MaxPoints.ToString(CultureInfo.InvariantCulture) +
                   " (" +
                   (score.Band ?? string.Empty) +
                   ").";
        }

        private static string ExtractLastRole(string messagesJson)
        {
            var matches = Regex.Matches(
                messagesJson ?? string.Empty,
                "\"role\":\"((?:\\\\.|[^\"])*)\"");

            if (matches.Count == 0)
            {
                return string.Empty;
            }

            return SvrFireJson.Unescape(matches[matches.Count - 1].Groups[1].Value);
        }

        private static string ExtractLastUserContent(string messagesJson)
        {
            var matches = Regex.Matches(
                messagesJson ?? string.Empty,
                "\"role\":\"user\",\"content\":\"((?:\\\\.|[^\"])*)\"");

            if (matches.Count == 0)
            {
                return string.Empty;
            }

            return SvrFireJson.Unescape(matches[matches.Count - 1].Groups[1].Value);
        }

        private static string BuildResponseCompletion(string response)
        {
            return "{\"success\":true,\"error\":null,\"cloud_handoff\":false,\"response\":\"" +
                   SvrFireJson.Escape(response) +
                   "\",\"function_calls\":[],\"segments\":[],\"confidence\":0.97}";
        }

        private static string BuildFunctionCallCompletion(List<string> calls)
        {
            var builder = new StringBuilder();
            builder.Append("{\"success\":true,\"error\":null,\"cloud_handoff\":false,\"response\":\"\",\"function_calls\":[");

            for (var i = 0; i < calls.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(calls[i]);
            }

            builder.Append("],\"segments\":[],\"confidence\":0.92}");
            return builder.ToString();
        }

        private static string BuildFunctionCall(string name, string argumentsJson)
        {
            return "{\"name\":\"" +
                   SvrFireJson.Escape(name) +
                   "\",\"arguments\":" +
                   (string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson) +
                   "}";
        }
    }

    internal sealed class EscalateHazardTool : ISimulationToolHandler
    {
        private readonly SvrFireScenarioState _state;
        private readonly Func<float> _clock;

        public EscalateHazardTool(SvrFireScenarioState state, Func<float> clock)
        {
            _state = state;
            _clock = clock;
            Definition = new SimulationToolDefinition
            {
                Name = "escalate_hazard",
                Description = "Escalates the fire scenario and updates hazard or route status.",
                ParametersJson =
                    "{\"type\":\"object\",\"properties\":{\"hazard_state\":{\"type\":\"string\"},\"announcement\":{\"type\":\"string\"}},\"required\":[\"hazard_state\"]}"
            };
        }

        public SimulationToolDefinition Definition { get; private set; }

        public SimulationToolResult Execute(SimulationToolCall call)
        {
            var argumentsJson = call == null ? string.Empty : call.ArgumentsJson;
            var hazardState = SvrToolArgumentReader.ReadString(
                argumentsJson,
                "hazard_state",
                SvrFireScenarioValues.HazardAlarmAndSmokeExitA);
            var announcement = SvrToolArgumentReader.ReadString(
                argumentsJson,
                "announcement",
                "Fire alarm active.");
            _state.EscalateHazard(hazardState, announcement, GetElapsedSeconds());
            return new SimulationToolResult
            {
                Name = call == null ? string.Empty : call.Name,
                Content = "Hazard escalated to " + hazardState + ".",
                IsError = false
            };
        }

        private float GetElapsedSeconds()
        {
            return _clock == null ? 0f : _clock();
        }
    }

    internal sealed class PromptParticipantTool : ISimulationToolHandler
    {
        private readonly SvrFireScenarioState _state;
        private readonly Func<float> _clock;

        public PromptParticipantTool(SvrFireScenarioState state, Func<float> clock)
        {
            _state = state;
            _clock = clock;
            Definition = new SimulationToolDefinition
            {
                Name = "prompt_participant",
                Description = "Delivers a short in-scenario prompt to the participant.",
                ParametersJson =
                    "{\"type\":\"object\",\"properties\":{\"message\":{\"type\":\"string\"}},\"required\":[\"message\"]}"
            };
        }

        public SimulationToolDefinition Definition { get; private set; }

        public SimulationToolResult Execute(SimulationToolCall call)
        {
            var message = SvrToolArgumentReader.ReadString(
                call == null ? string.Empty : call.ArgumentsJson,
                "message",
                "Proceed to the clear exit.");
            _state.PromptParticipant(message, GetElapsedSeconds());
            return new SimulationToolResult
            {
                Name = call == null ? string.Empty : call.Name,
                Content = message,
                IsError = false
            };
        }

        private float GetElapsedSeconds()
        {
            return _clock == null ? 0f : _clock();
        }
    }

    internal sealed class ChangeEnvironmentCueTool : ISimulationToolHandler
    {
        private readonly SvrFireScenarioState _state;
        private readonly Func<float> _clock;

        public ChangeEnvironmentCueTool(SvrFireScenarioState state, Func<float> clock)
        {
            _state = state;
            _clock = clock;
            Definition = new SimulationToolDefinition
            {
                Name = "change_environment_cue",
                Description = "Adjusts route availability, coworker state, or visible environmental cues.",
                ParametersJson =
                    "{\"type\":\"object\",\"properties\":{\"route_id\":{\"type\":\"string\"},\"available\":{\"type\":\"boolean\"},\"coworker_state\":{\"type\":\"string\"},\"note\":{\"type\":\"string\"}},\"required\":[]}"
            };
        }

        public SimulationToolDefinition Definition { get; private set; }

        public SimulationToolResult Execute(SimulationToolCall call)
        {
            var argumentsJson = call == null ? string.Empty : call.ArgumentsJson;
            var routeId = SvrToolArgumentReader.ReadString(argumentsJson, "route_id", string.Empty);
            var available = SvrToolArgumentReader.ReadBoolean(argumentsJson, "available");
            var coworkerState = SvrToolArgumentReader.ReadString(argumentsJson, "coworker_state", string.Empty);
            var note = SvrToolArgumentReader.ReadString(argumentsJson, "note", "Environment cue updated.");

            _state.ChangeEnvironmentCue(routeId, available, coworkerState, note, GetElapsedSeconds());
            return new SimulationToolResult
            {
                Name = call == null ? string.Empty : call.Name,
                Content = note,
                IsError = false
            };
        }

        private float GetElapsedSeconds()
        {
            return _clock == null ? 0f : _clock();
        }
    }

    internal sealed class AnnotateContextTool : ISimulationToolHandler
    {
        private readonly SvrFireScenarioState _state;
        private readonly Func<float> _clock;

        public AnnotateContextTool(SvrFireScenarioState state, Func<float> clock)
        {
            _state = state;
            _clock = clock;
            Definition = new SimulationToolDefinition
            {
                Name = "annotate_context",
                Description = "Stores a non-scoring AI note for later review.",
                ParametersJson =
                    "{\"type\":\"object\",\"properties\":{\"note\":{\"type\":\"string\"}},\"required\":[\"note\"]}"
            };
        }

        public SimulationToolDefinition Definition { get; private set; }

        public SimulationToolResult Execute(SimulationToolCall call)
        {
            var note = SvrToolArgumentReader.ReadString(
                call == null ? string.Empty : call.ArgumentsJson,
                "note",
                "AI annotation recorded.");
            _state.AnnotateContext(note, GetElapsedSeconds());
            return new SimulationToolResult
            {
                Name = call == null ? string.Empty : call.Name,
                Content = note,
                IsError = false
            };
        }

        private float GetElapsedSeconds()
        {
            return _clock == null ? 0f : _clock();
        }
    }

    internal sealed class TransitionPhaseTool : ISimulationToolHandler
    {
        private readonly SvrFireScenarioState _state;
        private readonly Func<float> _clock;

        public TransitionPhaseTool(SvrFireScenarioState state, Func<float> clock)
        {
            _state = state;
            _clock = clock;
            Definition = new SimulationToolDefinition
            {
                Name = "transition_phase",
                Description = "Moves the scenario to another explicit phase.",
                ParametersJson =
                    "{\"type\":\"object\",\"properties\":{\"phase\":{\"type\":\"string\"}},\"required\":[\"phase\"]}"
            };
        }

        public SimulationToolDefinition Definition { get; private set; }

        public SimulationToolResult Execute(SimulationToolCall call)
        {
            var phase = SvrToolArgumentReader.ReadString(
                call == null ? string.Empty : call.ArgumentsJson,
                "phase",
                SvrFireScenarioValues.PhaseEvacuation);
            _state.TransitionPhase(phase, GetElapsedSeconds());
            return new SimulationToolResult
            {
                Name = call == null ? string.Empty : call.Name,
                Content = "Scenario phase changed to " + phase + ".",
                IsError = false
            };
        }

        private float GetElapsedSeconds()
        {
            return _clock == null ? 0f : _clock();
        }
    }

    internal sealed class RequestEndScenarioTool : ISimulationToolHandler
    {
        private readonly SvrFireScenarioState _state;
        private readonly Func<float> _clock;

        public RequestEndScenarioTool(SvrFireScenarioState state, Func<float> clock)
        {
            _state = state;
            _clock = clock;
            Definition = new SimulationToolDefinition
            {
                Name = "request_end_scenario",
                Description = "Requests that the deterministic scenario controller conclude the session if terminal conditions are met.",
                ParametersJson =
                    "{\"type\":\"object\",\"properties\":{\"reason\":{\"type\":\"string\"}},\"required\":[\"reason\"]}"
            };
        }

        public SimulationToolDefinition Definition { get; private set; }

        public SimulationToolResult Execute(SimulationToolCall call)
        {
            var reason = SvrToolArgumentReader.ReadString(
                call == null ? string.Empty : call.ArgumentsJson,
                "reason",
                "AI requested scenario completion.");
            _state.RequestEndScenario(reason, GetElapsedSeconds());
            return new SimulationToolResult
            {
                Name = call == null ? string.Empty : call.Name,
                Content = reason,
                IsError = false
            };
        }

        private float GetElapsedSeconds()
        {
            return _clock == null ? 0f : _clock();
        }
    }

    internal static class SvrToolArgumentReader
    {
        public static string ReadString(string json, string key, string defaultValue)
        {
            var match = Regex.Match(
                json ?? string.Empty,
                "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:\\\\.|[^\"])*)\"");

            if (!match.Success)
            {
                return defaultValue;
            }

            return SvrFireJson.Unescape(match.Groups[1].Value);
        }

        public static bool? ReadBoolean(string json, string key)
        {
            var match = Regex.Match(
                json ?? string.Empty,
                "\"" + Regex.Escape(key) + "\"\\s*:\\s*(true|false)");

            if (!match.Success)
            {
                return null;
            }

            return string.Equals(match.Groups[1].Value, "true", StringComparison.Ordinal);
        }
    }

    internal static class SvrFireJson
    {
        public static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length + 16);
            for (var i = 0; i < value.Length; i++)
            {
                var current = value[i];
                switch (current)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (current < 32)
                        {
                            builder.Append("\\u");
                            builder.Append(((int)current).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(current);
                        }

                        break;
                }
            }

            return builder.ToString();
        }

        public static string Unescape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                var current = value[i];
                if (current != '\\' || i + 1 >= value.Length)
                {
                    builder.Append(current);
                    continue;
                }

                i++;
                switch (value[i])
                {
                    case '\\':
                        builder.Append('\\');
                        break;
                    case '"':
                        builder.Append('"');
                        break;
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'u':
                        if (i + 4 < value.Length)
                        {
                            var hex = value.Substring(i + 1, 4);
                            int codePoint;
                            if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out codePoint))
                            {
                                builder.Append((char)codePoint);
                                i += 4;
                            }
                        }

                        break;
                    default:
                        builder.Append(value[i]);
                        break;
                }
            }

            return builder.ToString();
        }
    }
}
