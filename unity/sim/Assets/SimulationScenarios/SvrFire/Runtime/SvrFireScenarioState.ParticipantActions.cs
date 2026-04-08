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
    }
}
