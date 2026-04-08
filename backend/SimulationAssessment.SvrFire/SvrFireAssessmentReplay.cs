using System;
using System.Collections.Generic;
using System.Text.Json;
using GemmaHackathon.SimulationFramework;

namespace GemmaHackathon.SimulationScenarios.SvrFire
{
    public sealed class SvrFireAssessmentReplayResult
    {
        public string SessionId = string.Empty;
        public string SessionState = string.Empty;
        public string Phase = string.Empty;
        public string SourceSessionDirectory = string.Empty;
        public AuditSessionRecord SessionRecord = new AuditSessionRecord();
        public AssessmentArtifacts Assessment = new AssessmentArtifacts();
        public List<AuditEvent> Timeline = new List<AuditEvent>();
    }

    public static class SvrFireAssessmentReplay
    {
        public static AssessmentArtifacts CreateArtifacts(
            AuditSessionRecord sessionRecord,
            IReadOnlyList<AuditEvent> auditEvents)
        {
            return Replay(sessionRecord, auditEvents).Assessment;
        }

        public static SvrFireAssessmentReplayResult Replay(
            AuditSessionRecord sessionRecord,
            IReadOnlyList<AuditEvent> auditEvents)
        {
            var safeSessionRecord = CloneSessionRecord(sessionRecord);
            var snapshot = CreateDefaultSnapshot(safeSessionRecord);

            if (auditEvents != null)
            {
                for (var i = 0; i < auditEvents.Count; i++)
                {
                    ApplyAuditEvent(snapshot, auditEvents[i]);
                }
            }

            return new SvrFireAssessmentReplayResult
            {
                SessionId = snapshot.SessionRecord.SessionId ?? string.Empty,
                SessionState = snapshot.SessionState ?? string.Empty,
                Phase = snapshot.Phase ?? string.Empty,
                SessionRecord = CloneSessionRecord(snapshot.SessionRecord),
                Assessment = SvrFireReadinessScorer.CreateArtifacts(snapshot),
                Timeline = CloneAuditEvents(snapshot.AuditEvents)
            };
        }

        private static SvrFireScenarioSnapshot CreateDefaultSnapshot(AuditSessionRecord sessionRecord)
        {
            var safeSessionRecord = CloneSessionRecord(sessionRecord);
            if (string.IsNullOrWhiteSpace(safeSessionRecord.ScenarioVariant))
            {
                safeSessionRecord.ScenarioVariant = SvrFireScenarioValues.DefaultVariantId;
            }

            if (string.IsNullOrWhiteSpace(safeSessionRecord.RubricVersion))
            {
                safeSessionRecord.RubricVersion = SvrFireScenarioValues.DefaultRubricVersion;
            }

            if (string.IsNullOrWhiteSpace(safeSessionRecord.ScoringVersion))
            {
                safeSessionRecord.ScoringVersion = SvrFireScenarioValues.DefaultScoringVersion;
            }

            var snapshot = new SvrFireScenarioSnapshot
            {
                SessionRecord = safeSessionRecord,
                SessionState = SvrFireScenarioValues.SessionStateReady,
                Phase = SvrFireScenarioValues.PhaseNormal,
                AlarmActive = false,
                ParticipantLocation = SvrFireScenarioValues.LocationWorkstation,
                HazardState = SvrFireScenarioValues.HazardNone,
                CoworkerState = SvrFireScenarioValues.CoworkerNearby,
                VariantId = safeSessionRecord.ScenarioVariant,
                LastScenarioEvent = "Normal workstation activity.",
                ElapsedSeconds = 0f
            };

            snapshot.RouteAvailability["exit_a"] = true;
            snapshot.RouteAvailability["exit_b"] = true;
            return snapshot;
        }

        private static void ApplyAuditEvent(SvrFireScenarioSnapshot snapshot, AuditEvent auditEvent)
        {
            if (snapshot == null || auditEvent == null)
            {
                return;
            }

            var safeAuditEvent = auditEvent.Clone();
            snapshot.AuditEvents.Add(safeAuditEvent);
            snapshot.ElapsedSeconds = Math.Max(snapshot.ElapsedSeconds, safeAuditEvent.ElapsedMilliseconds / 1000f);

            if (!string.IsNullOrWhiteSpace(safeAuditEvent.SessionId))
            {
                snapshot.SessionRecord.SessionId = safeAuditEvent.SessionId;
            }

            if (!string.IsNullOrWhiteSpace(safeAuditEvent.ScenarioVariant))
            {
                snapshot.SessionRecord.ScenarioVariant = safeAuditEvent.ScenarioVariant;
                snapshot.VariantId = safeAuditEvent.ScenarioVariant;
            }

            if (!string.IsNullOrWhiteSpace(safeAuditEvent.RubricVersion))
            {
                snapshot.SessionRecord.RubricVersion = safeAuditEvent.RubricVersion;
            }

            if (!string.IsNullOrWhiteSpace(safeAuditEvent.ScoringVersion))
            {
                snapshot.SessionRecord.ScoringVersion = safeAuditEvent.ScoringVersion;
            }

            switch (safeAuditEvent.ActionCode)
            {
                case "scenario_start":
                    snapshot.SessionState = SvrFireScenarioValues.SessionStateRunning;
                    snapshot.Phase = NormalizePhase(safeAuditEvent.Phase, SvrFireScenarioValues.PhaseNormal);
                    snapshot.LastScenarioEvent = "SVR office fire session started.";
                    return;

                case "alarm_triggered":
                    ApplyAlarmTriggered(snapshot, safeAuditEvent);
                    return;

                case "participant_action":
                    ApplyParticipantAction(snapshot, safeAuditEvent);
                    return;

                case "critical_error":
                    ApplyCriticalError(snapshot, safeAuditEvent);
                    return;

                case "scenario_end":
                    ApplyScenarioEnd(snapshot, safeAuditEvent);
                    return;

                default:
                    snapshot.Phase = NormalizePhase(safeAuditEvent.Phase, snapshot.Phase);
                    return;
            }
        }

        private static void ApplyAlarmTriggered(SvrFireScenarioSnapshot snapshot, AuditEvent auditEvent)
        {
            var hazardState = TryReadString(auditEvent.DataJson, "hazard_state");
            if (string.IsNullOrWhiteSpace(hazardState))
            {
                hazardState = auditEvent.Target;
            }

            var announcement = TryReadString(auditEvent.DataJson, "announcement");
            snapshot.SessionState = SvrFireScenarioValues.SessionStateRunning;
            snapshot.Phase = SvrFireScenarioValues.PhaseAlarm;
            snapshot.AlarmActive = true;
            snapshot.AlarmTriggeredAtSeconds = auditEvent.ElapsedMilliseconds / 1000f;
            snapshot.HazardState = string.IsNullOrWhiteSpace(hazardState)
                ? SvrFireScenarioValues.HazardAlarmAndSmokeExitA
                : hazardState;
            snapshot.LastScenarioEvent = string.IsNullOrWhiteSpace(announcement)
                ? "Fire alarm triggered."
                : announcement;

            if (string.Equals(snapshot.HazardState, SvrFireScenarioValues.HazardAlarmAndSmokeExitA, StringComparison.Ordinal))
            {
                snapshot.RouteAvailability["exit_a"] = false;
                snapshot.RouteAvailability["exit_b"] = true;
            }
            else if (string.Equals(snapshot.HazardState, SvrFireScenarioValues.HazardAlarmAndSmokeExitB, StringComparison.Ordinal))
            {
                snapshot.RouteAvailability["exit_a"] = true;
                snapshot.RouteAvailability["exit_b"] = false;
            }
            else
            {
                snapshot.RouteAvailability["exit_a"] = true;
                snapshot.RouteAvailability["exit_b"] = true;
            }

            if (string.Equals(snapshot.CoworkerState, SvrFireScenarioValues.CoworkerNearby, StringComparison.Ordinal))
            {
                snapshot.CoworkerState = SvrFireScenarioValues.CoworkerNeedsHelp;
            }
        }

        private static void ApplyParticipantAction(SvrFireScenarioSnapshot snapshot, AuditEvent auditEvent)
        {
            var actionCode = TryReadString(auditEvent.DataJson, "action_code");
            if (string.IsNullOrWhiteSpace(actionCode))
            {
                actionCode = auditEvent.Target;
            }

            var actionTarget = TryReadString(auditEvent.DataJson, "target");
            snapshot.LastParticipantAction = DescribeParticipantAction(actionCode);

            switch (actionCode)
            {
                case SvrFireScenarioValues.ActionAcknowledgeAlarm:
                    if (string.Equals(auditEvent.Outcome, "acknowledged", StringComparison.Ordinal))
                    {
                        snapshot.AlarmAcknowledgedAtSeconds = auditEvent.ElapsedMilliseconds / 1000f;
                    }

                    return;

                case SvrFireScenarioValues.ActionMoveExitA:
                case SvrFireScenarioValues.ActionMoveExitB:
                    if (!snapshot.EvacuationStartedAtSeconds.HasValue)
                    {
                        snapshot.EvacuationStartedAtSeconds = auditEvent.ElapsedMilliseconds / 1000f;
                    }

                    snapshot.Phase = string.Equals(auditEvent.Outcome, "recorded", StringComparison.Ordinal)
                        ? SvrFireScenarioValues.PhaseEvacuation
                        : SvrFireScenarioValues.PhaseComplete;
                    snapshot.SelectedRouteId = string.Equals(actionCode, SvrFireScenarioValues.ActionMoveExitA, StringComparison.Ordinal)
                        ? "exit_a"
                        : "exit_b";

                    if (!string.IsNullOrWhiteSpace(actionTarget))
                    {
                        snapshot.SelectedRouteId = actionTarget;
                    }

                    if (string.Equals(auditEvent.Outcome, "reached_safety", StringComparison.Ordinal))
                    {
                        snapshot.ParticipantLocation = SvrFireScenarioValues.LocationSafe;
                        return;
                    }

                    if (string.Equals(auditEvent.Outcome, "entered_hazard", StringComparison.Ordinal))
                    {
                        snapshot.ParticipantLocation = SvrFireScenarioValues.LocationHazard;
                        return;
                    }

                    snapshot.ParticipantLocation = snapshot.SelectedRouteId;
                    return;

                case SvrFireScenarioValues.ActionHelpCoworker:
                    if (string.Equals(auditEvent.Outcome, "coworker_assisted", StringComparison.Ordinal))
                    {
                        snapshot.CoworkerState = SvrFireScenarioValues.CoworkerAssisted;
                    }

                    return;

                case SvrFireScenarioValues.ActionAbandonScenario:
                    snapshot.Phase = SvrFireScenarioValues.PhaseComplete;
                    return;

                default:
                    return;
            }
        }

        private static void ApplyCriticalError(SvrFireScenarioSnapshot snapshot, AuditEvent auditEvent)
        {
            var code = TryReadString(auditEvent.DataJson, "code");
            if (string.IsNullOrWhiteSpace(code))
            {
                code = auditEvent.Target;
            }

            if (!string.IsNullOrWhiteSpace(code) && !ContainsCriticalError(snapshot, code))
            {
                snapshot.CriticalErrorCodes.Add(code);
            }

            var details = TryReadString(auditEvent.DataJson, "details");
            if (!string.IsNullOrWhiteSpace(details))
            {
                snapshot.LastScenarioEvent = details;
            }
        }

        private static void ApplyScenarioEnd(SvrFireScenarioSnapshot snapshot, AuditEvent auditEvent)
        {
            snapshot.SessionState = SvrFireScenarioValues.SessionStateComplete;
            snapshot.Phase = SvrFireScenarioValues.PhaseComplete;

            var finalLocation = TryReadString(auditEvent.DataJson, "final_location");
            if (!string.IsNullOrWhiteSpace(finalLocation))
            {
                snapshot.ParticipantLocation = finalLocation;
            }

            var selectedRoute = TryReadString(auditEvent.DataJson, "selected_route");
            if (!string.IsNullOrWhiteSpace(selectedRoute))
            {
                snapshot.SelectedRouteId = selectedRoute;
            }

            var reason = TryReadString(auditEvent.DataJson, "reason");
            var details = TryReadString(auditEvent.DataJson, "details");
            snapshot.LastScenarioEvent = !string.IsNullOrWhiteSpace(reason)
                ? reason
                : (!string.IsNullOrWhiteSpace(details) ? details : "SVR office fire session completed.");
        }

        private static string TryReadString(string json, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(propertyName))
            {
                return string.Empty;
            }

            try
            {
                using (var document = JsonDocument.Parse(json))
                {
                    JsonElement value;
                    if (!document.RootElement.TryGetProperty(propertyName, out value) ||
                        value.ValueKind != JsonValueKind.String)
                    {
                        return string.Empty;
                    }

                    return value.GetString() ?? string.Empty;
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool ContainsCriticalError(SvrFireScenarioSnapshot snapshot, string code)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(code))
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

        private static List<AuditEvent> CloneAuditEvents(List<AuditEvent> auditEvents)
        {
            var result = new List<AuditEvent>(auditEvents == null ? 0 : auditEvents.Count);
            if (auditEvents == null)
            {
                return result;
            }

            for (var i = 0; i < auditEvents.Count; i++)
            {
                result.Add((auditEvents[i] ?? new AuditEvent()).Clone());
            }

            return result;
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

        private static string NormalizePhase(string phase, string fallback)
        {
            return string.IsNullOrWhiteSpace(phase) ? (fallback ?? string.Empty) : phase.Trim();
        }

        private static string DescribeParticipantAction(string actionCode)
        {
            switch (actionCode)
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
                    return string.IsNullOrWhiteSpace(actionCode)
                        ? string.Empty
                        : "Participant performed action `" + actionCode.Trim() + "`.";
            }
        }
    }
}
