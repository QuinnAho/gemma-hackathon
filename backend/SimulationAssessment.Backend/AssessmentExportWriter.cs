using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using GemmaHackathon.SimulationFramework;
using GemmaHackathon.SimulationScenarios.SvrFire;

namespace GemmaHackathon.Backend.AssessmentCli
{
    internal sealed class SvrFireAssessmentSessionPackage
    {
        public string SchemaVersion = "svr.assessment.export.v1";
        public string ExportedAtUtc = string.Empty;
        public string SourceSessionDirectory = string.Empty;
        public string SourceManifestPath = string.Empty;
        public string SourceEventsPath = string.Empty;
        public RunLogManifestDocument Manifest = new RunLogManifestDocument();
        public AuditSessionRecord SessionRecord = new AuditSessionRecord();
        public AssessmentArtifacts Assessment = new AssessmentArtifacts();
        public AssessmentNarrative Narrative = new AssessmentNarrative();
        public List<AuditEvent> Timeline = new List<AuditEvent>();
    }

    internal sealed class AssessmentReportWithNarrativePackage
    {
        public AssessmentReport DeterministicReport = new AssessmentReport();
        public AssessmentReport AugmentedReport = new AssessmentReport();
        public AssessmentNarrative Narrative = new AssessmentNarrative();
    }

    internal sealed class AssessmentExportArtifact
    {
        public string Id = string.Empty;
        public string Stage = string.Empty;
        public string Format = string.Empty;
        public string RelativePath = string.Empty;
        public bool Deterministic;
        public string Description = string.Empty;
    }

    internal sealed class SvrFireAssessmentExportManifest
    {
        public string SchemaVersion = "svr.assessment.export-manifest.v1";
        public string ExportedAtUtc = string.Empty;
        public string ExportDirectory = string.Empty;
        public string SourceSessionDirectory = string.Empty;
        public string SourceManifestPath = string.Empty;
        public string SourceEventsPath = string.Empty;
        public string CanonicalEvidenceProducer = "unity";
        public string DeterministicTruthBoundary =
            "Deterministic truth comes from canonical Unity evidence replayed through shared assessment logic. Optional AI narrative is append-only and cannot change score, band, deficits, or critical failures.";
        public string Pipeline =
            "canonical evidence -> replay/normalize -> deterministic assessment -> deterministic report -> optional AI narrative -> versioned export";
        public string ExportHost = "backend";
        public string SessionId = string.Empty;
        public string ScenarioId = string.Empty;
        public string ScenarioVariantId = string.Empty;
        public string RubricVersion = string.Empty;
        public string ScoringVersion = string.Empty;
        public string RuntimeBackend = string.Empty;
        public bool HasNarrative;
        public string NarrativeProvider = string.Empty;
        public bool NarrativeUsedFallback;
        public List<AssessmentExportArtifact> Artifacts = new List<AssessmentExportArtifact>();
    }

    internal static class AssessmentExportWriter
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            IncludeFields = true,
            WriteIndented = true
        };

        public static string Write(
            SimulationRunSessionLog sessionLog,
            AssessmentReplayRecord replay,
            AssessmentNarrative narrative,
            string outputDirectory)
        {
            var safeSessionLog = sessionLog ?? new SimulationRunSessionLog();
            var safeReplay = replay ?? new AssessmentReplayRecord();
            var safeNarrative = narrative ?? new AssessmentNarrative();
            var exportedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            var sessionId = string.IsNullOrWhiteSpace(safeReplay.SessionId)
                ? safeSessionLog.SessionRecord.SessionId
                : safeReplay.SessionId;

            var exportDirectory = ResolveExportDirectory(sessionId, outputDirectory);
            Directory.CreateDirectory(exportDirectory);
            var artifacts = new List<AssessmentExportArtifact>();
            var deterministicReport = safeReplay.Assessment == null
                ? new AssessmentReport()
                : (safeReplay.Assessment.Report ?? new AssessmentReport());

            WriteJsonArtifact(
                exportDirectory,
                "assessment.json",
                safeReplay.Assessment,
                artifacts,
                "assessment",
                "deterministic_assessment",
                true,
                "Normalized assessment input, deterministic result, and deterministic report payload.");
            WriteJsonArtifact(
                exportDirectory,
                "timeline.json",
                safeReplay.Timeline,
                artifacts,
                "timeline",
                "replay_normalize",
                true,
                "Canonical audit timeline replayed for deterministic assessment.");
            WriteJsonArtifact(
                exportDirectory,
                "report.json",
                deterministicReport,
                artifacts,
                "deterministic_report",
                "deterministic_report",
                true,
                "Deterministic after-action report derived from replayed evidence.");
            if (HasNarrativePayload(safeNarrative))
            {
                WriteJsonArtifact(
                    exportDirectory,
                    "narrative.json",
                    safeNarrative,
                    artifacts,
                    "narrative",
                    "optional_narrative",
                    false,
                    "Optional grounded narrative addendum derived from deterministic outputs.");
                WriteJsonArtifact(
                    exportDirectory,
                    "report-with-narrative.json",
                    new AssessmentReportWithNarrativePackage
                    {
                        DeterministicReport = CloneReport(deterministicReport),
                        AugmentedReport = BuildAugmentedReport(deterministicReport, safeNarrative),
                        Narrative = safeNarrative
                    },
                    artifacts,
                    "report_with_narrative",
                    "optional_narrative",
                    false,
                    "Deterministic report packaged with an optional grounded narrative addendum.");
            }

            var sessionPackage = new SvrFireAssessmentSessionPackage
            {
                ExportedAtUtc = exportedAtUtc,
                SourceSessionDirectory = safeSessionLog.SessionDirectory ?? string.Empty,
                SourceManifestPath = safeSessionLog.ManifestPath ?? string.Empty,
                SourceEventsPath = safeSessionLog.EventsPath ?? string.Empty,
                Manifest = safeSessionLog.Manifest ?? new RunLogManifestDocument(),
                SessionRecord = safeReplay.SessionRecord ?? new AuditSessionRecord(),
                Assessment = safeReplay.Assessment ?? new AssessmentArtifacts(),
                Narrative = safeNarrative,
                Timeline = safeReplay.Timeline ?? new List<AuditEvent>()
            };
            WriteJsonArtifact(
                exportDirectory,
                "session-package.json",
                sessionPackage,
                artifacts,
                "session_package",
                "versioned_export",
                false,
                "Versioned export package combining source references, deterministic outputs, and optional narrative.");

            WriteTextArtifact(
                exportDirectory,
                "review-summary.txt",
                BuildReviewSummary(safeSessionLog, safeReplay, safeNarrative),
                artifacts,
                "review_summary",
                "versioned_export",
                "text",
                false,
                "Human-readable export summary for quick regression review.");

            WriteTextArtifact(
                exportDirectory,
                "after-action-report.md",
                BuildAfterActionReport(safeSessionLog, safeReplay, safeNarrative, exportedAtUtc),
                artifacts,
                "after_action_report",
                "versioned_export",
                "markdown",
                false,
                "Grounded after-action markdown report with deterministic findings and optional narrative addendum.");

            artifacts.Add(new AssessmentExportArtifact
            {
                Id = "export_manifest",
                Stage = "versioned_export",
                Format = "json",
                RelativePath = "export-manifest.json",
                Deterministic = false,
                Description = "Versioned manifest describing the backend export boundary, pipeline, and emitted artifacts."
            });
            WriteJson(
                Path.Combine(exportDirectory, "export-manifest.json"),
                BuildExportManifest(
                    exportDirectory,
                    exportedAtUtc,
                    safeSessionLog,
                    safeReplay,
                    safeNarrative,
                    artifacts));

            return exportDirectory;
        }

        private static string ResolveExportDirectory(string sessionId, string outputDirectory)
        {
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                return Path.GetFullPath(outputDirectory.Trim());
            }

            return Path.Combine(
                RepositoryPaths.GetDefaultExportRoot(),
                string.IsNullOrWhiteSpace(sessionId) ? "session-unknown" : sessionId.Trim());
        }

        private static void WriteJson<T>(string path, T value)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8);
        }

        private static void WriteJsonArtifact<T>(
            string exportDirectory,
            string fileName,
            T value,
            List<AssessmentExportArtifact> artifacts,
            string id,
            string stage,
            bool deterministic,
            string description)
        {
            WriteJson(Path.Combine(exportDirectory, fileName), value);
            artifacts.Add(CreateArtifact(id, stage, "json", fileName, deterministic, description));
        }

        private static void WriteTextArtifact(
            string exportDirectory,
            string fileName,
            string content,
            List<AssessmentExportArtifact> artifacts,
            string id,
            string stage,
            string format,
            bool deterministic,
            string description)
        {
            File.WriteAllText(Path.Combine(exportDirectory, fileName), content ?? string.Empty, Encoding.UTF8);
            artifacts.Add(CreateArtifact(id, stage, format, fileName, deterministic, description));
        }

        private static AssessmentExportArtifact CreateArtifact(
            string id,
            string stage,
            string format,
            string relativePath,
            bool deterministic,
            string description)
        {
            return new AssessmentExportArtifact
            {
                Id = id ?? string.Empty,
                Stage = stage ?? string.Empty,
                Format = format ?? string.Empty,
                RelativePath = relativePath ?? string.Empty,
                Deterministic = deterministic,
                Description = description ?? string.Empty
            };
        }

        private static string BuildReviewSummary(
            SimulationRunSessionLog sessionLog,
            AssessmentReplayRecord replay,
            AssessmentNarrative narrative)
        {
            var safeReplay = replay ?? new AssessmentReplayRecord();
            var assessment = safeReplay.Assessment ?? new AssessmentArtifacts();
            var result = assessment.Result ?? new AssessmentResult();
            var report = assessment.Report ?? new AssessmentReport();
            var safeNarrative = narrative ?? new AssessmentNarrative();
            var builder = new StringBuilder(512);

            builder.AppendLine("SVR Fire Assessment Export");
            builder.AppendLine("Session: " + SafeValue(safeReplay.SessionId));
            builder.AppendLine("Source: " + SafeValue((sessionLog ?? new SimulationRunSessionLog()).SessionDirectory));
            builder.AppendLine(
                "Score: " +
                result.TotalPoints +
                "/" +
                result.MaxPoints +
                " (" +
                SafeValue(result.Band) +
                ")");
            builder.AppendLine("Session State: " + SafeValue(safeReplay.SessionState));
            builder.AppendLine("Phase: " + SafeValue(safeReplay.Phase));
            builder.AppendLine("Timeline Events: " + (safeReplay.Timeline == null ? 0 : safeReplay.Timeline.Count));
            builder.AppendLine("Summary: " + SafeValue(report.Summary));

            if (result.CriticalFailures != null && result.CriticalFailures.Count > 0)
            {
                builder.AppendLine("Critical Failures:");
                for (var i = 0; i < result.CriticalFailures.Count; i++)
                {
                    builder.AppendLine("- " + SafeValue(result.CriticalFailures[i]));
                }
            }
            else
            {
                builder.AppendLine("Critical Failures: none");
            }

            if (result.Deficits != null && result.Deficits.Count > 0)
            {
                builder.AppendLine("Deficits:");
                for (var i = 0; i < result.Deficits.Count; i++)
                {
                    var deficit = result.Deficits[i] ?? new DeficitRecord();
                    builder.AppendLine("- [" + SafeValue(deficit.Severity) + "] " + SafeValue(deficit.Summary));
                }
            }
            else
            {
                builder.AppendLine("Deficits: none");
            }

            if (HasNarrativePayload(safeNarrative))
            {
                builder.AppendLine("Narrative Provider: " + SafeValue(safeNarrative.Provider));
                builder.AppendLine("Narrative Fallback: " + (safeNarrative.UsedFallback ? "yes" : "no"));
                builder.AppendLine("Narrative Summary: " + SafeValue(safeNarrative.Summary));
                if (safeNarrative.Recommendations != null && safeNarrative.Recommendations.Count > 0)
                {
                    builder.AppendLine("Narrative Recommendations:");
                    for (var i = 0; i < safeNarrative.Recommendations.Count; i++)
                    {
                        builder.AppendLine("- " + SafeValue(safeNarrative.Recommendations[i]));
                    }
                }

                if (!string.IsNullOrWhiteSpace(safeNarrative.Error))
                {
                    builder.AppendLine("Narrative Error: " + SafeValue(safeNarrative.Error));
                }
            }

            return builder.ToString();
        }

        private static string BuildAfterActionReport(
            SimulationRunSessionLog sessionLog,
            AssessmentReplayRecord replay,
            AssessmentNarrative narrative,
            string exportedAtUtc)
        {
            var safeSessionLog = sessionLog ?? new SimulationRunSessionLog();
            var safeReplay = replay ?? new AssessmentReplayRecord();
            var safeNarrative = narrative ?? new AssessmentNarrative();
            var assessment = safeReplay.Assessment ?? new AssessmentArtifacts();
            var input = assessment.Input ?? new AssessmentInput();
            var result = assessment.Result ?? new AssessmentResult();
            var report = assessment.Report ?? new AssessmentReport();
            var checklist = SvrFireReadinessScorer.BuildChecklist(input);
            var builder = new StringBuilder(2048);

            builder.AppendLine("# SVR Fire After-Action Report");
            builder.AppendLine();
            builder.AppendLine("## Session");
            builder.AppendLine("- Session: " + SafeValue(safeReplay.SessionId));
            builder.AppendLine("- Exported At (UTC): " + SafeValue(exportedAtUtc));
            builder.AppendLine("- Source: " + SafeValue(safeSessionLog.SessionDirectory));
            builder.AppendLine("- Scenario Variant: " + SafeValue(input.ScenarioVariantId));
            builder.AppendLine("- Rubric Version: " + SafeValue(input.RubricVersion));
            builder.AppendLine("- Scoring Version: " + SafeValue(input.ScoringVersion));
            builder.AppendLine("- Runtime Backend: " + SafeValue((safeSessionLog.SessionRecord ?? new AuditSessionRecord()).RuntimeBackend));
            builder.AppendLine();

            builder.AppendLine("## Outcome");
            builder.AppendLine(
                "- Score: " +
                result.TotalPoints.ToString(CultureInfo.InvariantCulture) +
                "/" +
                result.MaxPoints.ToString(CultureInfo.InvariantCulture) +
                " (" +
                SafeValue(result.Band) +
                ")");
            builder.AppendLine("- Session State: " + SafeValue(safeReplay.SessionState));
            builder.AppendLine("- Phase: " + SafeValue(safeReplay.Phase));
            builder.AppendLine("- Final Location: " + SafeValue(input.GetTextFact("participant.location")));
            builder.AppendLine("- Deterministic Summary: " + SafeValue(report.Summary));
            builder.AppendLine();

            builder.AppendLine("## Deterministic Findings");
            if (result.CriticalFailures != null && result.CriticalFailures.Count > 0)
            {
                builder.AppendLine("- Critical Failures:");
                for (var i = 0; i < result.CriticalFailures.Count; i++)
                {
                    builder.AppendLine("  - " + SafeValue(result.CriticalFailures[i]));
                }
            }
            else
            {
                builder.AppendLine("- Critical Failures: none");
            }

            if (result.Deficits != null && result.Deficits.Count > 0)
            {
                builder.AppendLine("- Deficits:");
                for (var i = 0; i < result.Deficits.Count; i++)
                {
                    var deficit = result.Deficits[i] ?? new DeficitRecord();
                    builder.AppendLine(
                        "  - [" +
                        SafeValue(deficit.Severity) +
                        "] " +
                        SafeValue(deficit.Summary) +
                        " " +
                        SafeValue(deficit.Details));
                }
            }
            else
            {
                builder.AppendLine("- Deficits: none");
            }

            builder.AppendLine();
            builder.AppendLine("## Checklist");
            if (checklist != null && checklist.Count > 0)
            {
                for (var i = 0; i < checklist.Count; i++)
                {
                    var item = checklist[i] ?? new SimulationChecklistItem();
                    builder.AppendLine(
                        "- " +
                        (item.Completed ? "[x] " : "[ ] ") +
                        SafeValue(item.Label) +
                        " " +
                        SafeValue(item.Notes));
                }
            }
            else
            {
                builder.AppendLine("- No checklist items were generated.");
            }

            builder.AppendLine();
            builder.AppendLine("## Metric Breakdown");
            if (result.MetricScores != null && result.MetricScores.Count > 0)
            {
                foreach (var pair in result.MetricScores)
                {
                    builder.AppendLine(
                        "- " +
                        SafeValue(pair.Key) +
                        ": " +
                        pair.Value.ToString(CultureInfo.InvariantCulture));
                }
            }
            else
            {
                builder.AppendLine("- No metric scores were recorded.");
            }

            builder.AppendLine();
            builder.AppendLine("## Timeline");
            if (safeReplay.Timeline != null && safeReplay.Timeline.Count > 0)
            {
                for (var i = 0; i < safeReplay.Timeline.Count; i++)
                {
                    var auditEvent = safeReplay.Timeline[i] ?? new AuditEvent();
                    builder.AppendLine(
                        "- " +
                        FormatElapsedSeconds(auditEvent.ElapsedMilliseconds) +
                        " " +
                        SafeValue(auditEvent.ActionCode) +
                        " by " +
                        SafeValue(auditEvent.Actor) +
                        " -> " +
                        SafeValue(auditEvent.Target) +
                        " outcome=" +
                        SafeValue(auditEvent.Outcome) +
                        " phase=" +
                        SafeValue(auditEvent.Phase));
                }
            }
            else
            {
                builder.AppendLine("- No audit timeline was exported.");
            }

            builder.AppendLine();
            builder.AppendLine("## Narrative Addendum");
            if (HasNarrativePayload(safeNarrative))
            {
                builder.AppendLine("- Provider: " + SafeValue(safeNarrative.Provider) + (safeNarrative.UsedFallback ? " (fallback)" : string.Empty));
                builder.AppendLine("- Summary: " + SafeValue(safeNarrative.Summary));
                builder.AppendLine("- Addendum: " + SafeValue(safeNarrative.ReportAddendum));
                if (safeNarrative.Recommendations != null && safeNarrative.Recommendations.Count > 0)
                {
                    builder.AppendLine("- Recommendations:");
                    for (var i = 0; i < safeNarrative.Recommendations.Count; i++)
                    {
                        builder.AppendLine("  - " + SafeValue(safeNarrative.Recommendations[i]));
                    }
                }
                else
                {
                    builder.AppendLine("- Recommendations: none");
                }

                builder.AppendLine("- Grounding: " + SafeValue(safeNarrative.GroundingNote));
                if (!string.IsNullOrWhiteSpace(safeNarrative.Error))
                {
                    builder.AppendLine("- Narrative Error: " + SafeValue(safeNarrative.Error));
                }
            }
            else
            {
                builder.AppendLine("- No optional narrative addendum was exported.");
            }

            return builder.ToString();
        }

        private static string FormatElapsedSeconds(int elapsedMilliseconds)
        {
            return (elapsedMilliseconds / 1000d).ToString("0.0", CultureInfo.InvariantCulture) + "s";
        }

        private static bool HasNarrativePayload(AssessmentNarrative narrative)
        {
            var safeNarrative = narrative ?? new AssessmentNarrative();
            return safeNarrative.Success ||
                   !string.IsNullOrWhiteSpace(safeNarrative.Provider) ||
                   !string.IsNullOrWhiteSpace(safeNarrative.Error);
        }

        private static AssessmentReport BuildAugmentedReport(
            AssessmentReport deterministicReport,
            AssessmentNarrative narrative)
        {
            var report = CloneReport(deterministicReport);
            var safeNarrative = narrative ?? new AssessmentNarrative();
            if (!HasNarrativePayload(safeNarrative))
            {
                return report;
            }

            report.Sections.Add(BuildNarrativeSection(safeNarrative));
            return report;
        }

        private static AssessmentReportSection BuildNarrativeSection(AssessmentNarrative narrative)
        {
            var safeNarrative = narrative ?? new AssessmentNarrative();
            var section = new AssessmentReportSection
            {
                Id = "narrative",
                Title = "Narrative Addendum"
            };

            if (!string.IsNullOrWhiteSpace(safeNarrative.Summary))
            {
                section.Entries.Add("Summary: " + safeNarrative.Summary.Trim());
            }

            if (!string.IsNullOrWhiteSpace(safeNarrative.ReportAddendum))
            {
                section.Entries.Add("Addendum: " + safeNarrative.ReportAddendum.Trim());
            }

            if (safeNarrative.Recommendations != null)
            {
                for (var i = 0; i < safeNarrative.Recommendations.Count; i++)
                {
                    var recommendation = safeNarrative.Recommendations[i] ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(recommendation))
                    {
                        section.Entries.Add("Recommendation: " + recommendation.Trim());
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(safeNarrative.GroundingNote))
            {
                section.Entries.Add("Grounding: " + safeNarrative.GroundingNote.Trim());
            }

            section.Entries.Add(
                "Provider: " +
                SafeValue(safeNarrative.Provider) +
                (safeNarrative.UsedFallback ? " (fallback)" : string.Empty) +
                ".");

            if (!string.IsNullOrWhiteSpace(safeNarrative.Error))
            {
                section.Entries.Add("Narrative error: " + safeNarrative.Error.Trim());
            }

            return section;
        }

        private static SvrFireAssessmentExportManifest BuildExportManifest(
            string exportDirectory,
            string exportedAtUtc,
            SimulationRunSessionLog sessionLog,
            AssessmentReplayRecord replay,
            AssessmentNarrative narrative,
            List<AssessmentExportArtifact> artifacts)
        {
            var safeSessionLog = sessionLog ?? new SimulationRunSessionLog();
            var safeReplay = replay ?? new AssessmentReplayRecord();
            var safeNarrative = narrative ?? new AssessmentNarrative();
            var assessment = safeReplay.Assessment ?? new AssessmentArtifacts();
            var input = assessment.Input ?? new AssessmentInput();
            var manifest = new SvrFireAssessmentExportManifest
            {
                ExportedAtUtc = exportedAtUtc ?? string.Empty,
                ExportDirectory = exportDirectory ?? string.Empty,
                SourceSessionDirectory = safeSessionLog.SessionDirectory ?? string.Empty,
                SourceManifestPath = safeSessionLog.ManifestPath ?? string.Empty,
                SourceEventsPath = safeSessionLog.EventsPath ?? string.Empty,
                SessionId = safeReplay.SessionId ?? string.Empty,
                ScenarioId = input.ScenarioId ?? string.Empty,
                ScenarioVariantId = input.ScenarioVariantId ?? string.Empty,
                RubricVersion = input.RubricVersion ?? string.Empty,
                ScoringVersion = input.ScoringVersion ?? string.Empty,
                RuntimeBackend = (safeSessionLog.SessionRecord ?? new AuditSessionRecord()).RuntimeBackend ?? string.Empty,
                HasNarrative = HasNarrativePayload(safeNarrative),
                NarrativeProvider = safeNarrative.Provider ?? string.Empty,
                NarrativeUsedFallback = safeNarrative.UsedFallback
            };

            if (artifacts != null)
            {
                for (var i = 0; i < artifacts.Count; i++)
                {
                    var artifact = artifacts[i] ?? new AssessmentExportArtifact();
                    manifest.Artifacts.Add(new AssessmentExportArtifact
                    {
                        Id = artifact.Id ?? string.Empty,
                        Stage = artifact.Stage ?? string.Empty,
                        Format = artifact.Format ?? string.Empty,
                        RelativePath = artifact.RelativePath ?? string.Empty,
                        Deterministic = artifact.Deterministic,
                        Description = artifact.Description ?? string.Empty
                    });
                }
            }

            return manifest;
        }

        private static AssessmentReport CloneReport(AssessmentReport value)
        {
            var source = value ?? new AssessmentReport();
            var clone = new AssessmentReport
            {
                SessionId = source.SessionId ?? string.Empty,
                ScenarioId = source.ScenarioId ?? string.Empty,
                ScenarioVariantId = source.ScenarioVariantId ?? string.Empty,
                PolicyId = source.PolicyId ?? string.Empty,
                RubricVersion = source.RubricVersion ?? string.Empty,
                ScoringVersion = source.ScoringVersion ?? string.Empty,
                GeneratedAtUtc = source.GeneratedAtUtc ?? string.Empty,
                TotalPoints = source.TotalPoints,
                MaxPoints = source.MaxPoints,
                Band = source.Band ?? string.Empty,
                Summary = source.Summary ?? string.Empty
            };

            if (source.Sections != null)
            {
                for (var i = 0; i < source.Sections.Count; i++)
                {
                    clone.Sections.Add(CloneSection(source.Sections[i]));
                }
            }

            return clone;
        }

        private static AssessmentReportSection CloneSection(AssessmentReportSection value)
        {
            var source = value ?? new AssessmentReportSection();
            var clone = new AssessmentReportSection
            {
                Id = source.Id ?? string.Empty,
                Title = source.Title ?? string.Empty
            };

            if (source.Entries != null)
            {
                for (var i = 0; i < source.Entries.Count; i++)
                {
                    clone.Entries.Add(source.Entries[i] ?? string.Empty);
                }
            }

            return clone;
        }

        private static string SafeValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(none)" : value.Trim();
        }
    }
}
