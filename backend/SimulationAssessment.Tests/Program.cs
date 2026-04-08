using System;
using System.Collections.Generic;
using GemmaHackathon.Backend.AssessmentCli;
using GemmaHackathon.SimulationFramework;
using GemmaHackathon.SimulationScenarios.SvrFire;

namespace SimulationAssessment.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            var failures = new List<string>();

            RunTest("accepts_labeled_summary_response", TestAcceptsLabeledSummaryResponse, failures);
            RunTest("accepts_json_narrative_response", TestAcceptsJsonNarrativeResponse, failures);
            RunTest("falls_back_when_bridge_response_fails", TestFallsBackWhenBridgeResponseFails, failures);

            if (failures.Count > 0)
            {
                Console.Error.WriteLine("SimulationAssessment.Tests failed:");
                for (var i = 0; i < failures.Count; i++)
                {
                    Console.Error.WriteLine("- " + failures[i]);
                }

                return 1;
            }

            Console.WriteLine("SimulationAssessment.Tests passed.");
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

        private static void TestAcceptsLabeledSummaryResponse()
        {
            var request = BuildRequest();
            var baseline = new TemplateAssessmentNarrativeComposer().Compose(request);
            var response = new DesktopGemmaBridgeResponse
            {
                Success = true,
                Backend = "desktop_gemma_bridge",
                ModelIdentifier = "stub-model",
                Response = "summary: Grounded AI summary.",
                RawJson = "{\"success\":true}"
            };

            var composer = new DesktopGemmaAssessmentNarrativeComposer(
                new TemplateAssessmentNarrativeComposer(),
                new StubClientFactory(response));

            var narrative = composer.Compose(request);

            AssertTrue(narrative.Success, "Narrative should succeed.");
            AssertTrue(!narrative.UsedFallback, "Labeled response should not require fallback.");
            AssertEqual("desktop_gemma_bridge", narrative.Provider, "Provider should come from the bridge response.");
            AssertEqual("stub-model", narrative.ModelReference, "Model reference should come from the bridge response.");
            AssertEqual("Grounded AI summary.", narrative.Summary, "Summary should be parsed from labeled response.");
            AssertEqual(baseline.ReportAddendum, narrative.ReportAddendum, "Missing addendum should fall back to deterministic baseline.");
            AssertEqual(baseline.GroundingNote, narrative.GroundingNote, "Missing grounding note should fall back to deterministic baseline.");
            AssertSequenceEqual(baseline.Recommendations, narrative.Recommendations, "Missing recommendations should fall back to deterministic baseline.");
        }

        private static void TestAcceptsJsonNarrativeResponse()
        {
            var request = BuildRequest();
            var response = new DesktopGemmaBridgeResponse
            {
                Success = true,
                Backend = "desktop_gemma_bridge",
                ModelIdentifier = "stub-model",
                Response = "{\"summary\":\"JSON summary.\",\"report_addendum\":\"JSON addendum.\",\"recommendations\":[\"First action.\",\"Second action.\"],\"grounding_note\":\"Narrative derived from deterministic outputs.\"}",
                RawJson = "{\"success\":true}"
            };

            var composer = new DesktopGemmaAssessmentNarrativeComposer(
                new TemplateAssessmentNarrativeComposer(),
                new StubClientFactory(response));

            var narrative = composer.Compose(request);

            AssertTrue(narrative.Success, "Narrative should succeed.");
            AssertTrue(!narrative.UsedFallback, "Structured JSON response should not require fallback.");
            AssertEqual("JSON summary.", narrative.Summary, "JSON summary should be preserved.");
            AssertEqual("JSON addendum.", narrative.ReportAddendum, "JSON addendum should be preserved.");
            AssertEqual("Narrative derived from deterministic outputs.", narrative.GroundingNote, "JSON grounding note should be preserved.");
            AssertEqual(2, narrative.Recommendations.Count, "JSON recommendations should replace baseline defaults.");
            AssertEqual("First action.", narrative.Recommendations[0], "First JSON recommendation should be preserved.");
            AssertEqual("Second action.", narrative.Recommendations[1], "Second JSON recommendation should be preserved.");
        }

        private static void TestFallsBackWhenBridgeResponseFails()
        {
            var request = BuildRequest();
            var baseline = new TemplateAssessmentNarrativeComposer().Compose(request);
            var response = new DesktopGemmaBridgeResponse
            {
                Success = false,
                Error = "synthetic bridge failure",
                Backend = "desktop_gemma_bridge",
                RawJson = "{\"success\":false}"
            };

            var composer = new DesktopGemmaAssessmentNarrativeComposer(
                new TemplateAssessmentNarrativeComposer(),
                new StubClientFactory(response));

            var narrative = composer.Compose(request);

            AssertTrue(narrative.Success, "Fallback narrative should still succeed.");
            AssertTrue(narrative.UsedFallback, "Failure response should trigger fallback.");
            AssertEqual("template", narrative.Provider, "Fallback should use deterministic template provider.");
            AssertEqual(baseline.Summary, narrative.Summary, "Fallback should preserve deterministic summary.");
            AssertContains(narrative.Error, "synthetic bridge failure", "Fallback error should include bridge failure details.");
        }

        private static AssessmentNarrativeComposeRequest BuildRequest()
        {
            var result = new AssessmentResult
            {
                PolicyId = "svr-fire-policy",
                SessionId = "test-session",
                ScenarioId = "svr_fire",
                ScenarioVariantId = "default",
                RubricVersion = "svr-fire.v1",
                ScoringVersion = "svr-score.v1",
                TotalPoints = 0,
                MaxPoints = 100,
                Band = "fail"
            };
            result.CriticalFailures.Add(SvrFireScenarioValues.CriticalWrongExit);
            result.Deficits.Add(new DeficitRecord
            {
                Id = SvrFireScenarioValues.CriticalWrongExit,
                MetricId = "safe_route",
                Severity = "critical",
                Summary = "The participant selected a hazardous exit route."
            });

            return new AssessmentNarrativeComposeRequest
            {
                SessionRecord = new AuditSessionRecord
                {
                    SessionId = "test-session",
                    ParticipantAlias = "tester",
                    ScenarioVariant = "svr-fire.default",
                    RubricVersion = "svr-fire.v1",
                    ScoringVersion = "svr-score.v1",
                    RuntimeBackend = "desktop_gemma_bridge",
                    SessionPhase = "complete"
                },
                Assessment = new AssessmentArtifacts
                {
                    Input = new AssessmentInput
                    {
                        SessionId = "test-session",
                        ScenarioId = "svr_fire",
                        ScenarioVariantId = "default",
                        SessionState = "complete",
                        Phase = "complete",
                        RubricVersion = "svr-fire.v1",
                        ScoringVersion = "svr-score.v1"
                    },
                    Result = result,
                    Report = new AssessmentReport
                    {
                        SessionId = "test-session",
                        ScenarioId = "svr_fire",
                        ScenarioVariantId = "default",
                        PolicyId = "svr-fire-policy",
                        RubricVersion = "svr-fire.v1",
                        ScoringVersion = "svr-score.v1",
                        TotalPoints = 0,
                        MaxPoints = 100,
                        Band = "fail",
                        Summary = "Assessment completed with a score of 0/100 (fail). Final participant location: hazard_zone."
                    }
                },
                Timeline = new List<AuditEvent>
                {
                    new AuditEvent
                    {
                        SessionId = "test-session",
                        EventId = "evt-1",
                        ElapsedMilliseconds = 1500,
                        Phase = "evacuation",
                        ActionCode = "participant.move_exit_a",
                        Actor = "participant",
                        Target = "exit_a",
                        Outcome = "hazard",
                        DataJson = "{}"
                    }
                }
            };
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

        private static void AssertContains(string actual, string expectedFragment, string message)
        {
            var safeActual = actual ?? string.Empty;
            var safeExpected = expectedFragment ?? string.Empty;
            if (safeActual.IndexOf(safeExpected, StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException(message + " Expected fragment `" + safeExpected + "` in `" + safeActual + "`.");
            }
        }

        private static void AssertSequenceEqual(List<string> expected, List<string> actual, string message)
        {
            var safeExpected = expected ?? new List<string>();
            var safeActual = actual ?? new List<string>();
            if (safeExpected.Count != safeActual.Count)
            {
                throw new InvalidOperationException(message + " Expected count `" + safeExpected.Count + "` but found `" + safeActual.Count + "`.");
            }

            for (var i = 0; i < safeExpected.Count; i++)
            {
                if (!string.Equals(safeExpected[i], safeActual[i], StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(message + " Mismatch at index " + i + ".");
                }
            }
        }
    }

    internal sealed class StubClientFactory : IDesktopGemmaNarrativeClientFactory
    {
        private readonly DesktopGemmaBridgeResponse _response;

        public StubClientFactory(DesktopGemmaBridgeResponse response)
        {
            _response = response ?? new DesktopGemmaBridgeResponse();
        }

        public IDesktopGemmaNarrativeClient Create()
        {
            return new StubClient(_response);
        }
    }

    internal sealed class StubClient : IDesktopGemmaNarrativeClient
    {
        private readonly DesktopGemmaBridgeResponse _response;

        public StubClient(DesktopGemmaBridgeResponse response)
        {
            _response = response ?? new DesktopGemmaBridgeResponse();
        }

        public DesktopGemmaBridgeResponse Complete(string messagesJson, string optionsJson = null, string toolsJson = "[]")
        {
            return _response;
        }

        public void Dispose()
        {
        }
    }
}
