using System;
using System.Collections.Generic;
using System.Globalization;
using GemmaHackathon.SimulationFramework;

namespace GemmaHackathon.SimulationScenarios.SvrFire
{
    public static class SvrFireReadinessScorer
    {
        private const string MetricAlarmRecognition = "alarm_recognition";
        private const string MetricEvacuationStart = "evacuation_start";
        private const string MetricRouteCorrectness = "route_correctness";
        private const string MetricProtocolCompletion = "protocol_completion";
        private const string MetricCriticalErrorPenalty = "critical_error_penalty";

        private const string FactAlarmActive = "alarm.active";
        private const string FactParticipantLocation = "participant.location";
        private const string FactHazardState = "hazard.state";
        private const string FactCoworkerState = "coworker.state";
        private const string FactSelectedRouteId = "participant.selected_route";
        private const string FactAlarmTriggeredAt = "timing.alarm_triggered_at_seconds";
        private const string FactAlarmAcknowledgedAt = "timing.alarm_acknowledged_at_seconds";
        private const string FactEvacuationStartedAt = "timing.evacuation_started_at_seconds";

        private static readonly ScoringPolicy DefaultPolicy = CreateDefaultPolicy();

        internal static AssessmentInput CreateAssessmentInput(SvrFireScenarioSnapshot snapshot)
        {
            var safeSnapshot = snapshot ?? new SvrFireScenarioSnapshot();
            var sessionRecord = safeSnapshot.SessionRecord ?? new AuditSessionRecord();
            var input = new AssessmentInput
            {
                SessionId = sessionRecord.SessionId ?? string.Empty,
                ScenarioId = SvrFireScenarioValues.ScenarioId,
                ScenarioVariantId = string.IsNullOrWhiteSpace(safeSnapshot.VariantId)
                    ? SvrFireScenarioValues.DefaultVariantId
                    : safeSnapshot.VariantId,
                SessionState = safeSnapshot.SessionState ?? string.Empty,
                Phase = safeSnapshot.Phase ?? string.Empty,
                RubricVersion = string.IsNullOrWhiteSpace(sessionRecord.RubricVersion)
                    ? SvrFireScenarioValues.DefaultRubricVersion
                    : sessionRecord.RubricVersion,
                ScoringVersion = string.IsNullOrWhiteSpace(sessionRecord.ScoringVersion)
                    ? SvrFireScenarioValues.DefaultScoringVersion
                    : sessionRecord.ScoringVersion,
                ElapsedSeconds = safeSnapshot.ElapsedSeconds
            };

            input.SetBooleanFact(FactAlarmActive, safeSnapshot.AlarmActive);
            input.SetTextFact(FactParticipantLocation, safeSnapshot.ParticipantLocation);
            input.SetTextFact(FactHazardState, safeSnapshot.HazardState);
            input.SetTextFact(FactCoworkerState, safeSnapshot.CoworkerState);
            input.SetTextFact(FactSelectedRouteId, safeSnapshot.SelectedRouteId);

            if (safeSnapshot.AlarmTriggeredAtSeconds.HasValue)
            {
                input.SetNumericFact(FactAlarmTriggeredAt, safeSnapshot.AlarmTriggeredAtSeconds.Value);
            }

            if (safeSnapshot.AlarmAcknowledgedAtSeconds.HasValue)
            {
                input.SetNumericFact(FactAlarmAcknowledgedAt, safeSnapshot.AlarmAcknowledgedAtSeconds.Value);
            }

            if (safeSnapshot.EvacuationStartedAtSeconds.HasValue)
            {
                input.SetNumericFact(FactEvacuationStartedAt, safeSnapshot.EvacuationStartedAtSeconds.Value);
            }

            if (safeSnapshot.RouteAvailability != null)
            {
                foreach (var pair in safeSnapshot.RouteAvailability)
                {
                    input.SetBooleanFact(BuildRouteAvailabilityKey(pair.Key), pair.Value);
                }
            }

            if (safeSnapshot.AuditEvents != null)
            {
                for (var i = 0; i < safeSnapshot.AuditEvents.Count; i++)
                {
                    var auditEvent = safeSnapshot.AuditEvents[i];
                    if (auditEvent == null || string.IsNullOrWhiteSpace(auditEvent.ActionCode))
                    {
                        continue;
                    }

                    input.ActionCodes.Add(auditEvent.ActionCode);
                }
            }

            if (safeSnapshot.CriticalErrorCodes != null)
            {
                for (var i = 0; i < safeSnapshot.CriticalErrorCodes.Count; i++)
                {
                    var code = safeSnapshot.CriticalErrorCodes[i];
                    if (string.IsNullOrWhiteSpace(code))
                    {
                        continue;
                    }

                    input.CriticalFailureCodes.Add(code);
                }
            }

            return input;
        }

        internal static AssessmentResult Calculate(SvrFireScenarioSnapshot snapshot)
        {
            return Calculate(CreateAssessmentInput(snapshot), DefaultPolicy);
        }

        internal static AssessmentArtifacts CreateArtifacts(SvrFireScenarioSnapshot snapshot)
        {
            return CreateArtifacts(CreateAssessmentInput(snapshot), DefaultPolicy);
        }

        public static AssessmentArtifacts CreateArtifacts(AssessmentInput input, ScoringPolicy policy)
        {
            var safeInput = input ?? new AssessmentInput();
            var safePolicy = policy ?? CreateDefaultPolicy();
            var result = Calculate(safeInput, safePolicy);
            return new AssessmentArtifacts
            {
                Input = safeInput,
                Result = result,
                Report = BuildReport(safeInput, result)
            };
        }

        public static AssessmentResult Calculate(AssessmentInput input, ScoringPolicy policy)
        {
            var safeInput = input ?? new AssessmentInput();
            var safePolicy = policy ?? CreateDefaultPolicy();
            var result = new AssessmentResult
            {
                PolicyId = safePolicy.PolicyId ?? string.Empty,
                SessionId = safeInput.SessionId ?? string.Empty,
                ScenarioId = safeInput.ScenarioId ?? string.Empty,
                ScenarioVariantId = safeInput.ScenarioVariantId ?? string.Empty,
                RubricVersion = safeInput.RubricVersion ?? string.Empty,
                ScoringVersion = safeInput.ScoringVersion ?? string.Empty,
                MaxPoints = safePolicy.MaxPoints
            };

            for (var i = 0; i < safePolicy.CriticalFailureCodes.Count; i++)
            {
                var code = safePolicy.CriticalFailureCodes[i];
                if (!safeInput.HasCriticalFailure(code))
                {
                    continue;
                }

                result.CriticalFailures.Add(code);
                AddCriticalFailureDeficit(result, code);
            }

            if (result.CriticalFailures.Count > 0)
            {
                result.TotalPoints = 0;
                result.Band = "fail";
                return result;
            }

            AddMetric(result, MetricAlarmRecognition, ScoreTimingMetric(safeInput, safePolicy, MetricAlarmRecognition));
            AddMetric(result, MetricEvacuationStart, ScoreTimingMetric(safeInput, safePolicy, MetricEvacuationStart));
            AddMetric(result, MetricRouteCorrectness, ScoreRouteCorrectness(safeInput, safePolicy));
            AddMetric(result, MetricProtocolCompletion, ScoreProtocolCompletion(safeInput, safePolicy));
            AddMetric(result, MetricCriticalErrorPenalty, ScoreCriticalErrorPenalty(safeInput, safePolicy));

            var total = 0;
            foreach (var pair in result.MetricScores)
            {
                total += pair.Value;
            }

            if (total < 0)
            {
                total = 0;
            }
            else if (total > result.MaxPoints)
            {
                total = result.MaxPoints;
            }

            result.TotalPoints = total;
            result.Band = ResolveBand(total, safePolicy);
            AddDerivedDeficits(result, safeInput, safePolicy);
            return result;
        }

        internal static List<SimulationChecklistItem> BuildChecklist(SvrFireScenarioSnapshot snapshot)
        {
            return BuildChecklist(CreateAssessmentInput(snapshot));
        }

        public static List<SimulationChecklistItem> BuildChecklist(AssessmentInput input)
        {
            return BuildChecklistInternal(input);
        }

        internal static AssessmentReport BuildReport(SvrFireScenarioSnapshot snapshot)
        {
            var input = CreateAssessmentInput(snapshot);
            var result = Calculate(input, DefaultPolicy);
            return BuildReport(input, result);
        }

        public static AssessmentReport BuildReport(AssessmentInput input, AssessmentResult result)
        {
            var safeInput = input ?? new AssessmentInput();
            var safeResult = result ?? new AssessmentResult();
            var checklist = BuildChecklistInternal(safeInput);
            var report = new AssessmentReport
            {
                SessionId = safeInput.SessionId ?? string.Empty,
                ScenarioId = safeInput.ScenarioId ?? string.Empty,
                ScenarioVariantId = safeInput.ScenarioVariantId ?? string.Empty,
                PolicyId = safeResult.PolicyId ?? string.Empty,
                RubricVersion = safeInput.RubricVersion ?? string.Empty,
                ScoringVersion = safeInput.ScoringVersion ?? string.Empty,
                GeneratedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                TotalPoints = safeResult.TotalPoints,
                MaxPoints = safeResult.MaxPoints,
                Band = safeResult.Band ?? string.Empty,
                Summary = BuildSummary(safeInput, safeResult)
            };

            report.Sections.Add(BuildOverviewSection(safeInput, safeResult));
            report.Sections.Add(BuildChecklistSection(checklist));
            report.Sections.Add(BuildMetricsSection(safeResult));

            if (safeResult.CriticalFailures.Count > 0)
            {
                report.Sections.Add(BuildCriticalFailuresSection(safeResult));
            }

            report.Sections.Add(BuildDeficitsSection(safeResult));
            return report;
        }

        private static List<SimulationChecklistItem> BuildChecklistInternal(AssessmentInput input)
        {
            var safeInput = input ?? new AssessmentInput();
            var checklist = new List<SimulationChecklistItem>(4);
            var alarmAcknowledgedAt = safeInput.GetNumericFact(FactAlarmAcknowledgedAt);
            var evacuationStartedAt = safeInput.GetNumericFact(FactEvacuationStartedAt);
            var participantLocation = safeInput.GetTextFact(FactParticipantLocation);
            var selectedRouteId = safeInput.GetTextFact(FactSelectedRouteId);
            var safeRouteChosen = IsSafeRouteChosen(safeInput);

            checklist.Add(CreateChecklistItem(
                "ack_alarm",
                "Recognize and acknowledge the alarm",
                alarmAcknowledgedAt.HasValue,
                alarmAcknowledgedAt.HasValue
                    ? BuildTimingNote(safeInput, alarmAcknowledgedAt.Value, "Acknowledged")
                    : "Alarm not yet acknowledged."));
            checklist.Add(CreateChecklistItem(
                "start_evacuation",
                "Begin evacuation promptly",
                evacuationStartedAt.HasValue,
                evacuationStartedAt.HasValue
                    ? BuildTimingNote(safeInput, evacuationStartedAt.Value, "Started moving")
                    : "Evacuation has not started."));
            checklist.Add(CreateChecklistItem(
                "choose_safe_route",
                "Choose an available exit route",
                safeRouteChosen,
                safeRouteChosen
                    ? "Selected " + selectedRouteId + "."
                    : "No safe route choice recorded."));
            checklist.Add(CreateChecklistItem(
                "reach_safety",
                "Reach the safe zone",
                string.Equals(participantLocation, SvrFireScenarioValues.LocationSafe, StringComparison.Ordinal),
                string.Equals(participantLocation, SvrFireScenarioValues.LocationSafe, StringComparison.Ordinal)
                    ? "Participant reached safety."
                    : "Participant is not yet in the safe zone."));
            return checklist;
        }

        private static ScoringPolicy CreateDefaultPolicy()
        {
            var policy = new ScoringPolicy
            {
                PolicyId = SvrFireScenarioValues.DefaultScoringVersion,
                ScenarioId = SvrFireScenarioValues.ScenarioId,
                RubricVersion = SvrFireScenarioValues.DefaultRubricVersion,
                ScoringVersion = SvrFireScenarioValues.DefaultScoringVersion,
                MaxPoints = 100,
                PassThreshold = 70,
                MarginalThreshold = 50,
                ProtocolCompletionPoints = 25,
                CriticalErrorPenaltyPerItem = 10
            };

            policy.CriticalFailureCodes.Add(SvrFireScenarioValues.CriticalIgnoredAlarm);
            policy.CriticalFailureCodes.Add(SvrFireScenarioValues.CriticalWrongExit);
            policy.FixedMetricPoints[MetricRouteCorrectness] = 25;
            policy.TimingMetrics.Add(CreateTimingMetricPolicy(
                MetricAlarmRecognition,
                FactAlarmAcknowledgedAt,
                25,
                new AssessmentTimingBand { MaxSeconds = 5d, Points = 25 },
                new AssessmentTimingBand { MaxSeconds = 10d, Points = 18 },
                new AssessmentTimingBand { MaxSeconds = 20d, Points = 10 }));
            policy.TimingMetrics.Add(CreateTimingMetricPolicy(
                MetricEvacuationStart,
                FactEvacuationStartedAt,
                25,
                new AssessmentTimingBand { MaxSeconds = 10d, Points = 25 },
                new AssessmentTimingBand { MaxSeconds = 20d, Points = 18 },
                new AssessmentTimingBand { MaxSeconds = 30d, Points = 10 }));
            return policy;
        }

        private static AssessmentTimingMetricPolicy CreateTimingMetricPolicy(
            string metricId,
            string measurementKey,
            int maxPoints,
            params AssessmentTimingBand[] bands)
        {
            var policy = new AssessmentTimingMetricPolicy
            {
                MetricId = metricId ?? string.Empty,
                MeasurementKey = measurementKey ?? string.Empty,
                MaxPoints = maxPoints
            };

            if (bands != null)
            {
                for (var i = 0; i < bands.Length; i++)
                {
                    if (bands[i] == null)
                    {
                        continue;
                    }

                    policy.Bands.Add(bands[i]);
                }
            }

            return policy;
        }

        private static string BuildRouteAvailabilityKey(string routeId)
        {
            return "route." + (routeId ?? string.Empty) + ".available";
        }

        private static int ScoreTimingMetric(AssessmentInput input, ScoringPolicy policy, string metricId)
        {
            var metricPolicy = FindTimingMetricPolicy(policy, metricId);
            if (metricPolicy == null)
            {
                return 0;
            }

            var absoluteSeconds = input.GetNumericFact(metricPolicy.MeasurementKey);
            if (!absoluteSeconds.HasValue)
            {
                return 0;
            }

            var relativeSeconds = ResolveAlarmRelativeSeconds(input, absoluteSeconds.Value);
            for (var i = 0; i < metricPolicy.Bands.Count; i++)
            {
                if (relativeSeconds <= metricPolicy.Bands[i].MaxSeconds)
                {
                    return metricPolicy.Bands[i].Points;
                }
            }

            return 0;
        }

        private static AssessmentTimingMetricPolicy FindTimingMetricPolicy(ScoringPolicy policy, string metricId)
        {
            if (policy == null || string.IsNullOrWhiteSpace(metricId))
            {
                return null;
            }

            for (var i = 0; i < policy.TimingMetrics.Count; i++)
            {
                var metricPolicy = policy.TimingMetrics[i];
                if (metricPolicy != null &&
                    string.Equals(metricPolicy.MetricId, metricId, StringComparison.Ordinal))
                {
                    return metricPolicy;
                }
            }

            return null;
        }

        private static int ScoreRouteCorrectness(AssessmentInput input, ScoringPolicy policy)
        {
            if (!IsSafeRouteChosen(input))
            {
                return 0;
            }

            int points;
            return policy != null && policy.FixedMetricPoints.TryGetValue(MetricRouteCorrectness, out points)
                ? points
                : 0;
        }

        private static int ScoreProtocolCompletion(AssessmentInput input, ScoringPolicy policy)
        {
            var checklist = BuildChecklist(input);
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

            return (int)Math.Round(
                (completedSteps / (double)checklist.Count) * (policy == null ? 0d : policy.ProtocolCompletionPoints),
                MidpointRounding.AwayFromZero);
        }

        private static int ScoreCriticalErrorPenalty(AssessmentInput input, ScoringPolicy policy)
        {
            if (input == null || input.CriticalFailureCodes.Count == 0 || policy == null)
            {
                return 0;
            }

            var penaltyCount = 0;
            for (var i = 0; i < input.CriticalFailureCodes.Count; i++)
            {
                if (!IsCriticalFailureTerminal(policy, input.CriticalFailureCodes[i]))
                {
                    penaltyCount++;
                }
            }

            return penaltyCount == 0 ? 0 : -policy.CriticalErrorPenaltyPerItem * penaltyCount;
        }

        private static bool IsCriticalFailureTerminal(ScoringPolicy policy, string code)
        {
            if (policy == null || string.IsNullOrWhiteSpace(code))
            {
                return false;
            }

            for (var i = 0; i < policy.CriticalFailureCodes.Count; i++)
            {
                if (string.Equals(policy.CriticalFailureCodes[i], code, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSafeRouteChosen(AssessmentInput input)
        {
            if (input == null)
            {
                return false;
            }

            var selectedRouteId = input.GetTextFact(FactSelectedRouteId);
            if (string.IsNullOrWhiteSpace(selectedRouteId))
            {
                return false;
            }

            return input.GetBooleanFact(BuildRouteAvailabilityKey(selectedRouteId)) &&
                   string.Equals(
                       input.GetTextFact(FactParticipantLocation),
                       SvrFireScenarioValues.LocationSafe,
                       StringComparison.Ordinal);
        }

        private static double ResolveAlarmRelativeSeconds(AssessmentInput input, double absoluteSeconds)
        {
            var alarmTriggeredAt = input == null ? null : input.GetNumericFact(FactAlarmTriggeredAt);
            if (alarmTriggeredAt.HasValue && absoluteSeconds >= alarmTriggeredAt.Value)
            {
                return absoluteSeconds - alarmTriggeredAt.Value;
            }

            return absoluteSeconds;
        }

        private static string BuildTimingNote(AssessmentInput input, double absoluteSeconds, string actionLabel)
        {
            var relativeSeconds = ResolveAlarmRelativeSeconds(input, absoluteSeconds);
            var alarmTriggeredAt = input == null ? null : input.GetNumericFact(FactAlarmTriggeredAt);
            if (alarmTriggeredAt.HasValue && absoluteSeconds >= alarmTriggeredAt.Value)
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

        private static string ResolveBand(int totalPoints, ScoringPolicy policy)
        {
            if (policy == null)
            {
                return "fail";
            }

            if (totalPoints >= policy.PassThreshold)
            {
                return "pass";
            }

            return totalPoints >= policy.MarginalThreshold ? "marginal" : "fail";
        }

        private static void AddMetric(AssessmentResult result, string metricId, int value)
        {
            if (result == null || string.IsNullOrWhiteSpace(metricId))
            {
                return;
            }

            result.MetricScores[metricId] = value;
        }

        private static void AddDerivedDeficits(AssessmentResult result, AssessmentInput input, ScoringPolicy policy)
        {
            if (result == null || input == null)
            {
                return;
            }

            var alarmAcknowledgedAt = input.GetNumericFact(FactAlarmAcknowledgedAt);
            if (!alarmAcknowledgedAt.HasValue)
            {
                AddDeficit(
                    result,
                    SvrFireDeficitCatalog.CreateRecord(SvrFireDeficitCatalog.AlarmAcknowledgementMissingId));
            }
            else if (ScoreTimingMetric(input, policy, MetricAlarmRecognition) < 25)
            {
                AddDeficit(
                    result,
                    SvrFireDeficitCatalog.CreateRecord(
                        SvrFireDeficitCatalog.AlarmAcknowledgementDelayedId,
                        BuildTimingNote(input, alarmAcknowledgedAt.Value, "Acknowledged")));
            }

            var evacuationStartedAt = input.GetNumericFact(FactEvacuationStartedAt);
            if (!evacuationStartedAt.HasValue)
            {
                AddDeficit(
                    result,
                    SvrFireDeficitCatalog.CreateRecord(SvrFireDeficitCatalog.EvacuationMissingId));
            }
            else if (ScoreTimingMetric(input, policy, MetricEvacuationStart) < 25)
            {
                AddDeficit(
                    result,
                    SvrFireDeficitCatalog.CreateRecord(
                        SvrFireDeficitCatalog.EvacuationDelayedId,
                        BuildTimingNote(input, evacuationStartedAt.Value, "Started moving")));
            }

            if (!IsSafeRouteChosen(input))
            {
                AddDeficit(
                    result,
                    SvrFireDeficitCatalog.CreateRecord(SvrFireDeficitCatalog.SafeRouteMissingId));
            }

            if (!string.Equals(
                input.GetTextFact(FactParticipantLocation),
                SvrFireScenarioValues.LocationSafe,
                StringComparison.Ordinal))
            {
                AddDeficit(
                    result,
                    SvrFireDeficitCatalog.CreateRecord(
                        SvrFireDeficitCatalog.SafetyNotReachedId,
                        "The final recorded participant location was `" +
                        (input.GetTextFact(FactParticipantLocation) ?? string.Empty) +
                        "`."));
            }
        }

        private static void AddCriticalFailureDeficit(AssessmentResult result, string code)
        {
            if (string.Equals(code, SvrFireScenarioValues.CriticalIgnoredAlarm, StringComparison.Ordinal))
            {
                AddDeficit(result, SvrFireDeficitCatalog.CreateRecord(code));
                return;
            }

            if (string.Equals(code, SvrFireScenarioValues.CriticalWrongExit, StringComparison.Ordinal))
            {
                AddDeficit(result, SvrFireDeficitCatalog.CreateRecord(code));
                return;
            }

            AddDeficit(
                result,
                code,
                MetricCriticalErrorPenalty,
                "critical",
                "A critical assessment failure was recorded.",
                code ?? string.Empty);
        }

        private static void AddDeficit(AssessmentResult result, DeficitRecord deficit)
        {
            if (result == null || deficit == null || string.IsNullOrWhiteSpace(deficit.Id))
            {
                return;
            }

            for (var i = 0; i < result.Deficits.Count; i++)
            {
                if (result.Deficits[i] != null &&
                    string.Equals(result.Deficits[i].Id, deficit.Id, StringComparison.Ordinal))
                {
                    return;
                }
            }

            result.Deficits.Add(new DeficitRecord
            {
                Id = deficit.Id ?? string.Empty,
                MetricId = deficit.MetricId ?? string.Empty,
                Severity = deficit.Severity ?? string.Empty,
                Summary = deficit.Summary ?? string.Empty,
                Details = deficit.Details ?? string.Empty
            });
        }

        private static void AddDeficit(
            AssessmentResult result,
            string id,
            string metricId,
            string severity,
            string summary,
            string details)
        {
            AddDeficit(
                result,
                new DeficitRecord
                {
                    Id = id ?? string.Empty,
                    MetricId = metricId ?? string.Empty,
                    Severity = severity ?? string.Empty,
                    Summary = summary ?? string.Empty,
                    Details = details ?? string.Empty
                });
        }

        private static AssessmentReportSection BuildOverviewSection(AssessmentInput input, AssessmentResult result)
        {
            var section = new AssessmentReportSection
            {
                Id = "overview",
                Title = "Outcome"
            };
            section.Entries.Add(
                "Score " +
                result.TotalPoints.ToString(CultureInfo.InvariantCulture) +
                "/" +
                result.MaxPoints.ToString(CultureInfo.InvariantCulture) +
                " (" +
                (result.Band ?? string.Empty) +
                ").");
            section.Entries.Add("Phase: " + SafeValue(input.Phase) + ".");
            section.Entries.Add("Participant location: " + SafeValue(input.GetTextFact(FactParticipantLocation)) + ".");
            section.Entries.Add("Hazard state: " + SafeValue(input.GetTextFact(FactHazardState)) + ".");
            return section;
        }

        private static AssessmentReportSection BuildChecklistSection(IReadOnlyList<SimulationChecklistItem> checklist)
        {
            var section = new AssessmentReportSection
            {
                Id = "checklist",
                Title = "Checklist"
            };

            if (checklist == null || checklist.Count == 0)
            {
                section.Entries.Add("No checklist items were generated.");
                return section;
            }

            for (var i = 0; i < checklist.Count; i++)
            {
                var item = checklist[i] ?? new SimulationChecklistItem();
                section.Entries.Add(
                    (item.Completed ? "[x] " : "[ ] ") +
                    (item.Label ?? string.Empty) +
                    " " +
                    SafeValue(item.Notes));
            }

            return section;
        }

        private static AssessmentReportSection BuildMetricsSection(AssessmentResult result)
        {
            var section = new AssessmentReportSection
            {
                Id = "metrics",
                Title = "Metric Breakdown"
            };

            if (result == null || result.MetricScores.Count == 0)
            {
                section.Entries.Add("No metric scores were recorded.");
                return section;
            }

            foreach (var pair in result.MetricScores)
            {
                section.Entries.Add(pair.Key + ": " + pair.Value.ToString(CultureInfo.InvariantCulture));
            }

            return section;
        }

        private static AssessmentReportSection BuildCriticalFailuresSection(AssessmentResult result)
        {
            var section = new AssessmentReportSection
            {
                Id = "critical_failures",
                Title = "Critical Failures"
            };

            for (var i = 0; i < result.CriticalFailures.Count; i++)
            {
                section.Entries.Add(result.CriticalFailures[i] ?? string.Empty);
            }

            return section;
        }

        private static AssessmentReportSection BuildDeficitsSection(AssessmentResult result)
        {
            var section = new AssessmentReportSection
            {
                Id = "deficits",
                Title = "Deficits"
            };

            if (result == null || result.Deficits.Count == 0)
            {
                section.Entries.Add("No deterministic deficits were recorded.");
                return section;
            }

            for (var i = 0; i < result.Deficits.Count; i++)
            {
                var deficit = result.Deficits[i] ?? new DeficitRecord();
                section.Entries.Add(
                    "[" +
                    SafeValue(deficit.Severity) +
                    "] " +
                    SafeValue(deficit.Summary) +
                    " " +
                    SafeValue(deficit.Details));
            }

            return section;
        }

        private static string BuildSummary(AssessmentInput input, AssessmentResult result)
        {
            var location = input.GetTextFact(FactParticipantLocation);
            return "Assessment completed with a score of " +
                   result.TotalPoints.ToString(CultureInfo.InvariantCulture) +
                   "/" +
                   result.MaxPoints.ToString(CultureInfo.InvariantCulture) +
                   " (" +
                   (result.Band ?? string.Empty) +
                   "). Final participant location: " +
                   SafeValue(location) +
                   ".";
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

        private static string SafeValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(none)" : value.Trim();
        }
    }
}
