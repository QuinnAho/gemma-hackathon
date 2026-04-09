using System;
using System.Collections.Generic;
using GemmaHackathon.SimulationFramework;
using GemmaHackathon.SimulationScenarios.SvrFire;

namespace SimulationAssessment.SvrFire.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            var failures = new List<string>();

            RunTest("replay_defaults_variant_and_versions", TestReplayDefaultsVariantAndVersions, failures);
            RunTest("replay_marks_wrong_exit_as_terminal_failure", TestReplayMarksWrongExitAsTerminalFailure, failures);
            RunTest("scoring_uses_alarm_relative_timing", TestScoringUsesAlarmRelativeTiming, failures);
            RunTest("scoring_emits_expected_deterministic_deficits", TestScoringEmitsExpectedDeterministicDeficits, failures);

            if (failures.Count > 0)
            {
                Console.Error.WriteLine("SimulationAssessment.SvrFire.Tests failed:");
                for (var i = 0; i < failures.Count; i++)
                {
                    Console.Error.WriteLine("- " + failures[i]);
                }

                return 1;
            }

            Console.WriteLine("SimulationAssessment.SvrFire.Tests passed.");
            return 0;
        }

        private static void RunTest(string name, Action test, List<string> failures)
        {
            try
            {
                test();
                Console.WriteLine("PASS " + name);
            }
            catch (Exception ex)
            {
                failures.Add(name + ": " + ex.Message);
                Console.Error.WriteLine("FAIL " + name + ": " + ex.Message);
            }
        }

        private static void TestReplayDefaultsVariantAndVersions()
        {
            var replay = SvrFireAssessmentReplay.Replay(new AuditSessionRecord
            {
                SessionId = "session-defaults"
            }, new List<AuditEvent>());

            AssertEqual(
                SvrFireScenarioValues.DefaultVariantId,
                replay.SessionRecord.ScenarioVariant,
                "Replay should default the scenario variant.");
            AssertEqual(
                SvrFireScenarioValues.DefaultRubricVersion,
                replay.SessionRecord.RubricVersion,
                "Replay should default the rubric version.");
            AssertEqual(
                SvrFireScenarioValues.DefaultScoringVersion,
                replay.SessionRecord.ScoringVersion,
                "Replay should default the scoring version.");
            AssertEqual(
                SvrFireScenarioValues.DefaultVariantId,
                replay.Assessment.Input.ScenarioVariantId,
                "Assessment input should carry the default scenario variant.");
            AssertEqual(
                SvrFireScenarioValues.DefaultRubricVersion,
                replay.Assessment.Input.RubricVersion,
                "Assessment input should carry the default rubric version.");
            AssertEqual(
                SvrFireScenarioValues.DefaultScoringVersion,
                replay.Assessment.Input.ScoringVersion,
                "Assessment input should carry the default scoring version.");
        }

        private static void TestReplayMarksWrongExitAsTerminalFailure()
        {
            var replay = SvrFireAssessmentReplay.Replay(
                new AuditSessionRecord
                {
                    SessionId = "session-wrong-exit"
                },
                new List<AuditEvent>
                {
                    CreateAuditEvent(100, "normal", "scenario_start", "system", "scenario", "started", "{\"scenario_id\":\"svr-office-fire-evacuation\"}"),
                    CreateAuditEvent(
                        5000,
                        "alarm",
                        "alarm_triggered",
                        "system",
                        SvrFireScenarioValues.HazardAlarmAndSmokeExitA,
                        "triggered",
                        "{\"hazard_state\":\"alarm_and_smoke_exit_a\"}"),
                    CreateAuditEvent(
                        9000,
                        "alarm",
                        "participant_action",
                        "participant",
                        string.Empty,
                        "acknowledged",
                        "{\"action_code\":\"acknowledge_alarm\"}"),
                    CreateAuditEvent(
                        14000,
                        "complete",
                        "participant_action",
                        "participant",
                        "exit_a",
                        "entered_hazard",
                        "{\"action_code\":\"move_exit_a\"}"),
                    CreateAuditEvent(
                        14000,
                        "complete",
                        "critical_error",
                        "system",
                        SvrFireScenarioValues.CriticalWrongExit,
                        "recorded",
                        "{\"code\":\"wrong_exit_into_fire\",\"details\":\"Participant selected a hazardous exit route.\"}"),
                    CreateAuditEvent(
                        14000,
                        "complete",
                        "scenario_end",
                        "system",
                        "scenario",
                        "completed_hazard",
                        "{\"selected_route\":\"exit_a\",\"final_location\":\"hazard_zone\",\"reason\":\"Participant entered a hazardous route.\"}")
                });

            AssertEqual("fail", replay.Assessment.Result.Band, "Wrong-exit failure should be terminal.");
            AssertEqual(0, replay.Assessment.Result.TotalPoints, "Wrong-exit failure should zero the score.");
            AssertTrue(
                Contains(replay.Assessment.Result.CriticalFailures, SvrFireScenarioValues.CriticalWrongExit),
                "Critical failure list should include the wrong-exit code.");
        }

        private static void TestScoringUsesAlarmRelativeTiming()
        {
            var artifacts = SvrFireReadinessScorer.CreateArtifacts(CreateStrongPassInput(), null);

            AssertEqual(100, artifacts.Result.TotalPoints, "A fast safe response should earn full score.");
            AssertEqual(25, ReadMetricScore(artifacts.Result, "alarm_recognition"), "Alarm recognition should be scored relative to the alarm trigger.");
            AssertEqual(25, ReadMetricScore(artifacts.Result, "evacuation_start"), "Evacuation start should be scored relative to the alarm trigger.");
            AssertEqual("pass", artifacts.Result.Band, "The deterministic result should be a pass.");
        }

        private static void TestScoringEmitsExpectedDeterministicDeficits()
        {
            var input = new AssessmentInput
            {
                SessionId = "session-deficits",
                ScenarioId = SvrFireScenarioValues.ScenarioId,
                ScenarioVariantId = SvrFireScenarioValues.DefaultVariantId,
                SessionState = SvrFireScenarioValues.SessionStateComplete,
                Phase = SvrFireScenarioValues.PhaseComplete,
                RubricVersion = SvrFireScenarioValues.DefaultRubricVersion,
                ScoringVersion = SvrFireScenarioValues.DefaultScoringVersion,
                ElapsedSeconds = 30d
            };
            input.SetBooleanFact("alarm.active", true);
            input.SetTextFact("participant.location", SvrFireScenarioValues.LocationSafe);
            input.SetTextFact("hazard.state", SvrFireScenarioValues.HazardAlarmAndSmokeExitA);
            input.SetTextFact("coworker.state", SvrFireScenarioValues.CoworkerNeedsHelp);
            input.SetTextFact("participant.selected_route", "exit_b");
            input.SetNumericFact("timing.alarm_triggered_at_seconds", 5d);
            input.SetNumericFact("timing.evacuation_started_at_seconds", 24d);
            input.SetBooleanFact("route.exit_a.available", false);
            input.SetBooleanFact("route.exit_b.available", true);

            var artifacts = SvrFireReadinessScorer.CreateArtifacts(input, null);

            AssertTrue(
                ContainsDeficit(artifacts.Result.Deficits, SvrFireDeficitCatalog.AlarmAcknowledgementMissingId),
                "Missing alarm acknowledgement should produce a deterministic deficit.");
            AssertTrue(
                ContainsDeficit(artifacts.Result.Deficits, SvrFireDeficitCatalog.EvacuationDelayedId),
                "Delayed evacuation should produce a deterministic deficit.");
            AssertTrue(
                !ContainsDeficit(artifacts.Result.Deficits, SvrFireDeficitCatalog.SafeRouteMissingId),
                "A validated safe route should not produce a safe-route deficit.");
        }

        private static AssessmentInput CreateStrongPassInput()
        {
            var input = new AssessmentInput
            {
                SessionId = "session-pass",
                ScenarioId = SvrFireScenarioValues.ScenarioId,
                ScenarioVariantId = SvrFireScenarioValues.DefaultVariantId,
                SessionState = SvrFireScenarioValues.SessionStateComplete,
                Phase = SvrFireScenarioValues.PhaseComplete,
                RubricVersion = SvrFireScenarioValues.DefaultRubricVersion,
                ScoringVersion = SvrFireScenarioValues.DefaultScoringVersion,
                ElapsedSeconds = 18d
            };
            input.SetBooleanFact("alarm.active", true);
            input.SetTextFact("participant.location", SvrFireScenarioValues.LocationSafe);
            input.SetTextFact("hazard.state", SvrFireScenarioValues.HazardAlarmAndSmokeExitA);
            input.SetTextFact("coworker.state", SvrFireScenarioValues.CoworkerAssisted);
            input.SetTextFact("participant.selected_route", "exit_b");
            input.SetNumericFact("timing.alarm_triggered_at_seconds", 5d);
            input.SetNumericFact("timing.alarm_acknowledged_at_seconds", 7d);
            input.SetNumericFact("timing.evacuation_started_at_seconds", 12d);
            input.SetBooleanFact("route.exit_a.available", false);
            input.SetBooleanFact("route.exit_b.available", true);
            return input;
        }

        private static AuditEvent CreateAuditEvent(
            int elapsedMilliseconds,
            string phase,
            string actionCode,
            string actor,
            string target,
            string outcome,
            string dataJson)
        {
            return new AuditEvent
            {
                SessionId = "session-wrong-exit",
                EventId = Guid.NewGuid().ToString("N"),
                ScenarioVariant = SvrFireScenarioValues.DefaultVariantId,
                RubricVersion = SvrFireScenarioValues.DefaultRubricVersion,
                ScoringVersion = SvrFireScenarioValues.DefaultScoringVersion,
                ElapsedMilliseconds = elapsedMilliseconds,
                Phase = phase ?? string.Empty,
                Source = actor ?? string.Empty,
                ActionCode = actionCode ?? string.Empty,
                Actor = actor ?? string.Empty,
                Target = target ?? string.Empty,
                Outcome = outcome ?? string.Empty,
                DataJson = dataJson ?? "{}"
            };
        }

        private static int ReadMetricScore(AssessmentResult result, string metricId)
        {
            var safeResult = result ?? new AssessmentResult();
            int value;
            if (!safeResult.MetricScores.TryGetValue(metricId, out value))
            {
                throw new InvalidOperationException("Metric `" + metricId + "` was not present.");
            }

            return value;
        }

        private static bool Contains(IReadOnlyList<string> values, string expected)
        {
            if (values == null)
            {
                return false;
            }

            for (var i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], expected, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsDeficit(IReadOnlyList<DeficitRecord> deficits, string expectedId)
        {
            if (deficits == null)
            {
                return false;
            }

            for (var i = 0; i < deficits.Count; i++)
            {
                if (deficits[i] != null &&
                    string.Equals(deficits[i].Id, expectedId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(message + " Expected `" + expected + "` but found `" + actual + "`.");
            }
        }
    }
}
