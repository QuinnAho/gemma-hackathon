using System;
using System.Collections.Generic;
using System.Globalization;
using GemmaHackathon.SimulationFramework;

namespace GemmaHackathon.SimulationScenarios.SvrFire
{
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
                    ? BuildTimingNote(safeSnapshot, safeSnapshot.AlarmAcknowledgedAtSeconds.Value, "Acknowledged")
                    : "Alarm not yet acknowledged."));
            checklist.Add(CreateChecklistItem(
                "start_evacuation",
                "Begin evacuation promptly",
                safeSnapshot.EvacuationStartedAtSeconds.HasValue,
                safeSnapshot.EvacuationStartedAtSeconds.HasValue
                    ? BuildTimingNote(safeSnapshot, safeSnapshot.EvacuationStartedAtSeconds.Value, "Started moving")
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

            var seconds = ResolveAlarmRelativeSeconds(snapshot, snapshot.AlarmAcknowledgedAtSeconds.Value);
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

            var seconds = ResolveAlarmRelativeSeconds(snapshot, snapshot.EvacuationStartedAtSeconds.Value);
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

        private static float ResolveAlarmRelativeSeconds(SvrFireScenarioSnapshot snapshot, float absoluteSeconds)
        {
            if (snapshot != null &&
                snapshot.AlarmTriggeredAtSeconds.HasValue &&
                absoluteSeconds >= snapshot.AlarmTriggeredAtSeconds.Value)
            {
                return absoluteSeconds - snapshot.AlarmTriggeredAtSeconds.Value;
            }

            return absoluteSeconds;
        }

        private static string BuildTimingNote(
            SvrFireScenarioSnapshot snapshot,
            float absoluteSeconds,
            string actionLabel)
        {
            var relativeSeconds = ResolveAlarmRelativeSeconds(snapshot, absoluteSeconds);
            if (snapshot != null &&
                snapshot.AlarmTriggeredAtSeconds.HasValue &&
                absoluteSeconds >= snapshot.AlarmTriggeredAtSeconds.Value)
            {
                return actionLabel +
                    " " +
                    relativeSeconds.ToString("0.0", CultureInfo.InvariantCulture) +
                    "s after alarm.";
            }

            return actionLabel +
                " at " +
                absoluteSeconds.ToString("0.0", CultureInfo.InvariantCulture) +
                "s.";
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
}
