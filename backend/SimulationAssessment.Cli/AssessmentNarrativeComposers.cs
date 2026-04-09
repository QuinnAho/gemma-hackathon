using System;
using System.Collections.Generic;
using System.Text.Json;
using GemmaHackathon.SimulationFramework;
using GemmaHackathon.SimulationScenarios.SvrFire;

namespace GemmaHackathon.Backend.AssessmentCli
{
    internal sealed class AssessmentNarrativeComposeRequest
    {
        public AuditSessionRecord SessionRecord = new AuditSessionRecord();
        public AssessmentArtifacts Assessment = new AssessmentArtifacts();
        public List<AuditEvent> Timeline = new List<AuditEvent>();
        public string ExportDirectory = string.Empty;
    }

    internal interface IAssessmentNarrativeComposer
    {
        AssessmentNarrative Compose(AssessmentNarrativeComposeRequest request);
    }

    internal static class AssessmentNarrativeComposerFactory
    {
        public static IAssessmentNarrativeComposer Create(CliOptions options)
        {
            var mode = options == null ? CliOptions.NarrativeModeNone : options.NarrativeMode;
            if (string.Equals(mode, CliOptions.NarrativeModeNone, StringComparison.Ordinal))
            {
                return null;
            }

            if (string.Equals(mode, CliOptions.NarrativeModeTemplate, StringComparison.Ordinal))
            {
                return new TemplateAssessmentNarrativeComposer();
            }

            if (string.Equals(mode, CliOptions.NarrativeModeDesktopGemma, StringComparison.Ordinal))
            {
                return new DesktopGemmaAssessmentNarrativeComposer(new TemplateAssessmentNarrativeComposer());
            }

            throw new InvalidOperationException("Unsupported narrative mode `" + mode + "`.");
        }
    }

    internal sealed class TemplateAssessmentNarrativeComposer : IAssessmentNarrativeComposer
    {
        private const string PromptVersion = "template.narrative.v1";

        public AssessmentNarrative Compose(AssessmentNarrativeComposeRequest request)
        {
            var safeRequest = request ?? new AssessmentNarrativeComposeRequest();
            var assessment = safeRequest.Assessment ?? new AssessmentArtifacts();
            var result = assessment.Result ?? new AssessmentResult();
            var report = assessment.Report ?? new AssessmentReport();

            var recommendations = BuildRecommendations(result);
            return new AssessmentNarrative
            {
                Success = true,
                UsedFallback = false,
                Provider = "template",
                ModelReference = "deterministic-template",
                PromptVersion = PromptVersion,
                GeneratedAtUtc = DateTime.UtcNow.ToString("o"),
                Summary = BuildSummary(result, report),
                ReportAddendum = BuildReportAddendum(result, report),
                Recommendations = recommendations,
                GroundingNote = "Narrative derived only from deterministic assessment outputs and audit evidence.",
                Error = string.Empty,
                RawResponse = string.Empty
            };
        }

        private static string BuildSummary(AssessmentResult result, AssessmentReport report)
        {
            var safeResult = result ?? new AssessmentResult();
            var safeReport = report ?? new AssessmentReport();
            var summary = string.IsNullOrWhiteSpace(safeReport.Summary)
                ? "Assessment results are available."
                : safeReport.Summary.Trim();

            if (safeResult.CriticalFailures.Count > 0)
            {
                return summary + " A terminal critical failure was recorded, so the deterministic result remains a fail regardless of partial progress elsewhere.";
            }

            if (string.Equals(safeResult.Band, "pass", StringComparison.Ordinal))
            {
                return summary + " The run met the current pass threshold with no deterministic critical failures.";
            }

            if (string.Equals(safeResult.Band, "marginal", StringComparison.Ordinal))
            {
                return summary + " The run avoided terminal failure but still missed enough checkpoints to remain below the pass threshold.";
            }

            return summary + " The run did not meet the current readiness threshold.";
        }

        private static string BuildReportAddendum(AssessmentResult result, AssessmentReport report)
        {
            var safeResult = result ?? new AssessmentResult();
            var safeReport = report ?? new AssessmentReport();
            if (safeResult.CriticalFailures.Count > 0)
            {
                return "This narrative layer does not reinterpret the event record. The deterministic report shows a critical failure, and follow-up coaching should focus on the specific decision or delay that triggered that terminal condition.";
            }

            if (safeResult.Deficits.Count == 0)
            {
                return "The deterministic record shows no deficits. Follow-up can focus on reinforcing the correct sequence and preserving response speed under pressure.";
            }

            return "The deterministic report shows that the outcome was driven by a small number of concrete gaps rather than ambiguous model judgment. Coaching should stay anchored to those recorded deficits and their timing in the audit trail.";
        }

        private static List<string> BuildRecommendations(AssessmentResult result)
        {
            var safeResult = result ?? new AssessmentResult();
            var recommendations = new List<string>();

            for (var i = 0; i < safeResult.Deficits.Count; i++)
            {
                var deficit = safeResult.Deficits[i] ?? new DeficitRecord();
                var recommendation = SvrFireDeficitCatalog.BuildRecommendation(deficit);
                if (!string.IsNullOrWhiteSpace(recommendation) && !Contains(recommendations, recommendation))
                {
                    recommendations.Add(recommendation);
                }
            }

            if (recommendations.Count == 0)
            {
                recommendations.Add("Repeat the drill and preserve the same deterministic response sequence.");
            }

            return recommendations;
        }

        private static bool Contains(List<string> values, string value)
        {
            for (var i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], value, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal sealed class DesktopGemmaAssessmentNarrativeComposer : IAssessmentNarrativeComposer
    {
        private const string PromptVersion = "desktop-gemma.narrative.v1";
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            IncludeFields = true,
            WriteIndented = true
        };

        private readonly IAssessmentNarrativeComposer _fallbackComposer;
        private readonly IDesktopGemmaNarrativeClientFactory _clientFactory;

        public DesktopGemmaAssessmentNarrativeComposer(
            IAssessmentNarrativeComposer fallbackComposer,
            IDesktopGemmaNarrativeClientFactory clientFactory = null)
        {
            _fallbackComposer = fallbackComposer;
            _clientFactory = clientFactory ?? new BackendDesktopGemmaBridgeClientFactory();
        }

        public AssessmentNarrative Compose(AssessmentNarrativeComposeRequest request)
        {
            var safeRequest = request ?? new AssessmentNarrativeComposeRequest();
            var baselineNarrative = _fallbackComposer == null
                ? new AssessmentNarrative()
                : _fallbackComposer.Compose(safeRequest);

            try
            {
                using (var client = _clientFactory.Create())
                {
                    var response = client.Complete(
                        ConversationMessageJson.Serialize(BuildPromptMessages(safeRequest)),
                        BuildOptionsJson());

                    if (!response.Success)
                    {
                        return ComposeFallback(
                            safeRequest,
                            "Desktop Gemma narrative request failed: " + SafeValue(response.Error),
                            response.RawJson);
                    }

                    AssessmentNarrative parsedNarrative;
                    if (!TryParseNarrativeResponse(response.Response, response, baselineNarrative, out parsedNarrative))
                    {
                        return ComposeFallback(
                            safeRequest,
                            "Desktop Gemma narrative response could not be parsed into the expected grounded JSON contract.",
                            response.Response);
                    }

                    parsedNarrative.PromptVersion = PromptVersion;
                    parsedNarrative.GeneratedAtUtc = DateTime.UtcNow.ToString("o");
                    return parsedNarrative;
                }
            }
            catch (Exception ex)
            {
                return ComposeFallback(
                    safeRequest,
                    "Desktop Gemma narrative generation failed: " + ex.Message,
                    string.Empty);
            }
        }

        private AssessmentNarrative ComposeFallback(
            AssessmentNarrativeComposeRequest request,
            string error,
            string rawResponse)
        {
            var fallback = _fallbackComposer == null
                ? new AssessmentNarrative
                {
                    Success = false,
                    Provider = "desktop_gemma_bridge",
                    PromptVersion = PromptVersion,
                    GeneratedAtUtc = DateTime.UtcNow.ToString("o"),
                    Error = error ?? string.Empty,
                    RawResponse = rawResponse ?? string.Empty
                }
                : _fallbackComposer.Compose(request);

            fallback.UsedFallback = true;
            fallback.Error = string.IsNullOrWhiteSpace(error)
                ? (fallback.Error ?? string.Empty)
                : error;
            if (string.IsNullOrWhiteSpace(fallback.RawResponse))
            {
                fallback.RawResponse = rawResponse ?? string.Empty;
            }

            return fallback;
        }

        private static List<ConversationMessage> BuildPromptMessages(AssessmentNarrativeComposeRequest request)
        {
            var messages = new List<ConversationMessage>(2);
            messages.Add(new ConversationMessage
            {
                Role = "system",
                Content = BuildSystemPrompt()
            });
            messages.Add(new ConversationMessage
            {
                Role = "user",
                Content = BuildGroundingPacketJson(request)
            });
            return messages;
        }

        private static string BuildSystemPrompt()
        {
            return string.Join(
                "\n",
                "You are writing a post-run narrative appendix for a simulation after-action report.",
                "Use only the grounded deterministic assessment package provided by the user.",
                "Do not change or reinterpret score, band, critical failures, metric scores, checklist state, or timeline facts.",
                "If information is missing from the package, say it was not recorded.",
                "Return a single JSON object with exactly these fields:",
                "{\"summary\":\"...\",\"report_addendum\":\"...\",\"recommendations\":[\"...\"],\"grounding_note\":\"...\"}",
                "summary must be 2-4 sentences.",
                "report_addendum must be a concise paragraph suitable to append to the report.",
                "recommendations must contain 2-4 short grounded coaching actions.",
                "grounding_note must explicitly state that the narrative is derived from deterministic outputs.",
                "Do not use markdown.");
        }

        private static string BuildGroundingPacketJson(AssessmentNarrativeComposeRequest request)
        {
            var safeRequest = request ?? new AssessmentNarrativeComposeRequest();
            var assessment = safeRequest.Assessment ?? new AssessmentArtifacts();
            var result = assessment.Result ?? new AssessmentResult();
            var report = assessment.Report ?? new AssessmentReport();
            var timeline = new List<object>();
            var safeTimeline = safeRequest.Timeline ?? new List<AuditEvent>();
            for (var i = 0; i < safeTimeline.Count; i++)
            {
                var item = safeTimeline[i] ?? new AuditEvent();
                timeline.Add(new
                {
                    elapsed_ms = item.ElapsedMilliseconds,
                    phase = item.Phase ?? string.Empty,
                    action_code = item.ActionCode ?? string.Empty,
                    actor = item.Actor ?? string.Empty,
                    target = item.Target ?? string.Empty,
                    outcome = item.Outcome ?? string.Empty,
                    data = item.DataJson ?? "{}"
                });
            }

            var promptPacket = new
            {
                schema_version = "svr.narrative.prompt.v1",
                session_record = safeRequest.SessionRecord ?? new AuditSessionRecord(),
                deterministic_assessment = assessment,
                score = new
                {
                    total_points = result.TotalPoints,
                    max_points = result.MaxPoints,
                    band = result.Band ?? string.Empty
                },
                deterministic_report = report,
                timeline = timeline
            };

            return JsonSerializer.Serialize(promptPacket, JsonOptions);
        }

        private static string BuildOptionsJson()
        {
            return JsonSerializer.Serialize(new
            {
                temperature = 0.1,
                max_new_tokens = 384
            });
        }

        private static bool TryParseNarrativeResponse(
            string responseText,
            DesktopGemmaBridgeResponse response,
            AssessmentNarrative baselineNarrative,
            out AssessmentNarrative narrative)
        {
            narrative = CreateBaselineNarrative(response, responseText, baselineNarrative);
            var payload = TryExtractJsonObject(responseText);
            if (!string.IsNullOrWhiteSpace(payload))
            {
                try
                {
                    using (var document = JsonDocument.Parse(payload))
                    {
                        var root = document.RootElement;
                        var summary = ReadString(root, "summary");
                        if (string.IsNullOrWhiteSpace(summary))
                        {
                            return false;
                        }

                        narrative.Summary = summary;

                        var reportAddendum = ReadString(root, "report_addendum");
                        if (!string.IsNullOrWhiteSpace(reportAddendum))
                        {
                            narrative.ReportAddendum = reportAddendum;
                        }

                        var groundingNote = ReadString(root, "grounding_note");
                        if (!string.IsNullOrWhiteSpace(groundingNote))
                        {
                            narrative.GroundingNote = groundingNote;
                        }

                        JsonElement recommendationsElement;
                        if (root.TryGetProperty("recommendations", out recommendationsElement) &&
                            recommendationsElement.ValueKind == JsonValueKind.Array)
                        {
                            narrative.Recommendations.Clear();
                            foreach (var item in recommendationsElement.EnumerateArray())
                            {
                                if (item.ValueKind == JsonValueKind.String)
                                {
                                    var recommendation = item.GetString() ?? string.Empty;
                                    if (!string.IsNullOrWhiteSpace(recommendation))
                                    {
                                        narrative.Recommendations.Add(recommendation.Trim());
                                    }
                                }
                            }
                        }

                        return true;
                    }
                }
                catch
                {
                    return false;
                }
            }

            return TryParseLabeledNarrative(responseText, narrative);
        }

        private static AssessmentNarrative CreateBaselineNarrative(
            DesktopGemmaBridgeResponse response,
            string responseText,
            AssessmentNarrative baselineNarrative)
        {
            var safeBaseline = baselineNarrative ?? new AssessmentNarrative();
            var narrative = new AssessmentNarrative
            {
                Success = true,
                UsedFallback = false,
                Provider = string.IsNullOrWhiteSpace(response.Backend)
                    ? "desktop_gemma_bridge"
                    : response.Backend,
                ModelReference = response.ModelIdentifier ?? string.Empty,
                PromptVersion = safeBaseline.PromptVersion ?? string.Empty,
                GeneratedAtUtc = safeBaseline.GeneratedAtUtc ?? string.Empty,
                Summary = safeBaseline.Summary ?? string.Empty,
                ReportAddendum = safeBaseline.ReportAddendum ?? string.Empty,
                GroundingNote = safeBaseline.GroundingNote ?? string.Empty,
                Error = string.Empty,
                RawResponse = responseText ?? string.Empty
            };

            for (var i = 0; i < safeBaseline.Recommendations.Count; i++)
            {
                var recommendation = safeBaseline.Recommendations[i] ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(recommendation))
                {
                    narrative.Recommendations.Add(recommendation.Trim());
                }
            }

            return narrative;
        }

        private static bool TryParseLabeledNarrative(string responseText, AssessmentNarrative narrative)
        {
            var safeText = responseText == null ? string.Empty : responseText.Trim();
            if (string.IsNullOrWhiteSpace(safeText))
            {
                return false;
            }

            var extractedSummary = ExtractLabeledValue(safeText, "summary");
            var extractedAddendum = ExtractLabeledValue(safeText, "report_addendum");
            var extractedGrounding = ExtractLabeledValue(safeText, "grounding_note");
            var extractedRecommendations = ExtractRecommendations(safeText);

            if (string.IsNullOrWhiteSpace(extractedSummary))
            {
                extractedSummary = safeText;
            }

            if (string.IsNullOrWhiteSpace(extractedSummary))
            {
                return false;
            }

            narrative.Summary = extractedSummary;
            if (!string.IsNullOrWhiteSpace(extractedAddendum))
            {
                narrative.ReportAddendum = extractedAddendum;
            }

            if (!string.IsNullOrWhiteSpace(extractedGrounding))
            {
                narrative.GroundingNote = extractedGrounding;
            }

            if (extractedRecommendations.Count > 0)
            {
                narrative.Recommendations.Clear();
                for (var i = 0; i < extractedRecommendations.Count; i++)
                {
                    narrative.Recommendations.Add(extractedRecommendations[i]);
                }
            }

            return true;
        }

        private static string TryExtractJsonObject(string value)
        {
            var safeValue = value == null ? string.Empty : value.Trim();
            if (string.IsNullOrWhiteSpace(safeValue))
            {
                return string.Empty;
            }

            if (TryParseJsonObject(safeValue))
            {
                return safeValue;
            }

            if (safeValue.StartsWith("```", StringComparison.Ordinal))
            {
                var firstBrace = safeValue.IndexOf('{');
                var lastBrace = safeValue.LastIndexOf('}');
                if (firstBrace >= 0 && lastBrace > firstBrace)
                {
                    var fencedObject = safeValue.Substring(firstBrace, lastBrace - firstBrace + 1);
                    if (TryParseJsonObject(fencedObject))
                    {
                        return fencedObject;
                    }
                }
            }

            var start = safeValue.IndexOf('{');
            var end = safeValue.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                var embeddedObject = safeValue.Substring(start, end - start + 1);
                if (TryParseJsonObject(embeddedObject))
                {
                    return embeddedObject;
                }
            }

            return string.Empty;
        }

        private static bool TryParseJsonObject(string value)
        {
            try
            {
                using (var document = JsonDocument.Parse(value))
                {
                    return document.RootElement.ValueKind == JsonValueKind.Object;
                }
            }
            catch
            {
                return false;
            }
        }

        private static string ReadString(JsonElement root, string propertyName)
        {
            JsonElement value;
            if (!root.TryGetProperty(propertyName, out value) || value.ValueKind != JsonValueKind.String)
            {
                return string.Empty;
            }

            return value.GetString() ?? string.Empty;
        }

        private static string ExtractLabeledValue(string content, string label)
        {
            var safeLabel = string.IsNullOrWhiteSpace(label) ? string.Empty : label.Trim();
            if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(safeLabel))
            {
                return string.Empty;
            }

            var lowerContent = content.ToLowerInvariant();
            var token = safeLabel.ToLowerInvariant() + ":";
            var startIndex = lowerContent.IndexOf(token, StringComparison.Ordinal);
            if (startIndex < 0)
            {
                return string.Empty;
            }

            startIndex += token.Length;
            var endIndex = content.Length;
            var nextLabels = new[]
            {
                "\nsummary:",
                "\nreport_addendum:",
                "\nrecommendations:",
                "\ngrounding_note:"
            };

            for (var i = 0; i < nextLabels.Length; i++)
            {
                var candidateIndex = lowerContent.IndexOf(nextLabels[i], startIndex, StringComparison.Ordinal);
                if (candidateIndex >= 0 && candidateIndex < endIndex)
                {
                    endIndex = candidateIndex;
                }
            }

            return content.Substring(startIndex, endIndex - startIndex).Trim();
        }

        private static List<string> ExtractRecommendations(string content)
        {
            var recommendations = new List<string>();
            var labeledBlock = ExtractLabeledValue(content, "recommendations");
            if (string.IsNullOrWhiteSpace(labeledBlock))
            {
                return recommendations;
            }

            if (labeledBlock.StartsWith("[", StringComparison.Ordinal) &&
                labeledBlock.EndsWith("]", StringComparison.Ordinal) &&
                TryParseJsonArrayRecommendations(labeledBlock, recommendations))
            {
                return recommendations;
            }

            var lines = labeledBlock.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("-", StringComparison.Ordinal))
                {
                    line = line.Substring(1).Trim();
                }

                if (!string.IsNullOrWhiteSpace(line))
                {
                    recommendations.Add(line);
                }
            }

            if (recommendations.Count == 0 && !string.IsNullOrWhiteSpace(labeledBlock))
            {
                recommendations.Add(labeledBlock.Trim());
            }

            return recommendations;
        }

        private static bool TryParseJsonArrayRecommendations(string payload, List<string> output)
        {
            try
            {
                using (var document = JsonDocument.Parse(payload))
                {
                    if (document.RootElement.ValueKind != JsonValueKind.Array)
                    {
                        return false;
                    }

                    foreach (var item in document.RootElement.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            var value = item.GetString() ?? string.Empty;
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                output.Add(value.Trim());
                            }
                        }
                    }

                    return output.Count > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private static string SafeValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(none)" : value.Trim();
        }
    }
}
