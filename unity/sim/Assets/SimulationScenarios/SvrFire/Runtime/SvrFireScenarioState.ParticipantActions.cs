using System;
using System.Collections.Generic;
using GemmaHackathon.SimulationFramework;

namespace GemmaHackathon.SimulationScenarios.SvrFire
{
    internal sealed partial class SvrFireScenarioState
    {
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

        private void ApplyParticipantActionLocked(ParticipantAction action, float elapsedSeconds)
        {
            var safeAction = action ?? new ParticipantAction();
            var description = DescribeParticipantAction(safeAction);
            _lastParticipantAction = description;
            AddActionLocked("participant", safeAction.ActionCode, description, elapsedSeconds);

            var outcome = "recorded";
            var target = safeAction.Target ?? string.Empty;
            var shouldCompleteSession = false;
            var completionReason = string.Empty;
            var completionOutcome = string.Empty;
            var completionDataJson = string.Empty;

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
                        outcome = "reached_safety";
                        shouldCompleteSession = true;
                        completionReason = "Participant reached the designated safe exit.";
                        completionOutcome = "completed_safe";
                    }
                    else
                    {
                        _participantLocation = SvrFireScenarioValues.LocationHazard;
                        _phase = SvrFireScenarioValues.PhaseComplete;
                        outcome = "entered_hazard";
                        EmitCriticalErrorLocked(
                            SvrFireScenarioValues.CriticalWrongExit,
                            "Participant selected a hazardous exit route.",
                            elapsedSeconds);
                        shouldCompleteSession = true;
                        completionReason = "Participant entered a hazardous route.";
                        completionOutcome = "completed_hazard";
                    }

                    completionDataJson = AuditEventJson.CreateObject(
                        new KeyValuePair<string, string>("reason", completionReason),
                        new KeyValuePair<string, string>("selected_route", _selectedRouteId),
                        new KeyValuePair<string, string>("final_location", _participantLocation));

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
                    outcome = "abandoned";
                    target = "scenario";
                    shouldCompleteSession = true;
                    completionReason = "Participant abandoned the scenario.";
                    completionOutcome = "completed_abandoned";
                    completionDataJson = AuditEventJson.CreateObject(
                        new KeyValuePair<string, string>("reason", completionReason),
                        new KeyValuePair<string, string>("final_location", _participantLocation));
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

            if (shouldCompleteSession)
            {
                CompleteSessionLocked(
                    completionReason,
                    "scenario",
                    completionOutcome,
                    elapsedSeconds,
                    completionDataJson);
            }
        }

        private void EnsureTimeBasedCriticalFailuresLocked(float elapsedSeconds)
        {
            float secondsSinceAlarm;
            if (_alarmActive &&
                !_alarmAcknowledgedAtSeconds.HasValue &&
                TryGetSecondsSinceAlarmLocked(elapsedSeconds, out secondsSinceAlarm) &&
                secondsSinceAlarm >= 60f &&
                !_criticalErrorCodes.Contains(SvrFireScenarioValues.CriticalIgnoredAlarm))
            {
                EmitCriticalErrorLocked(
                    SvrFireScenarioValues.CriticalIgnoredAlarm,
                    "Participant failed to acknowledge the alarm within 60 seconds of alarm activation.",
                    elapsedSeconds);
            }
        }

        private bool TryGetSecondsSinceAlarmLocked(float elapsedSeconds, out float secondsSinceAlarm)
        {
            if (!_alarmTriggeredAtSeconds.HasValue)
            {
                secondsSinceAlarm = 0f;
                return false;
            }

            secondsSinceAlarm = elapsedSeconds - _alarmTriggeredAtSeconds.Value;
            if (secondsSinceAlarm < 0f)
            {
                secondsSinceAlarm = 0f;
            }

            return true;
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
    }
}
