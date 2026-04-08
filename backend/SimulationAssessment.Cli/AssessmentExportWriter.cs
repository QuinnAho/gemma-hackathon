using System;
using System.Collections.Generic;
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

    internal static class AssessmentExportWriter
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            IncludeFields = true,
            WriteIndented = true
        };

        public static string Write(
            SimulationRunSessionLog sessionLog,
            SvrFireAssessmentReplayResult replay,
            AssessmentNarrative narrative,
            string outputDirectory)
        {
            var safeSessionLog = sessionLog ?? new SimulationRunSessionLog();
            var safeReplay = replay ?? new SvrFireAssessmentReplayResult();
            var safeNarrative = narrative ?? new AssessmentNarrative();
            var sessionId = string.IsNullOrWhiteSpace(safeReplay.SessionId)
                ? safeSessionLog.SessionRecord.SessionId
                : safeReplay.SessionId;

            var exportDirectory = ResolveExportDirectory(sessionId, outputDirectory);
            Directory.CreateDirectory(exportDirectory);

            WriteJson(Path.Combine(exportDirectory, "assessment.json"), safeReplay.Assessment);
            WriteJson(Path.Combine(exportDirectory, "timeline.json"), safeReplay.Timeline);
            WriteJson(
                Path.Combine(exportDirectory, "report.json"),
                safeReplay.Assessment == null
                    ? new AssessmentReport()
                    : (safeReplay.Assessment.Report ?? new AssessmentReport()));
            if (HasNarrativePayload(safeNarrative))
            {
                WriteJson(Path.Combine(exportDirectory, "narrative.json"), safeNarrative);
                WriteJson(
                    Path.Combine(exportDirectory, "report-with-narrative.json"),
                    new AssessmentReportWithNarrativePackage
                    {
                        DeterministicReport = CloneReport(
                            safeReplay.Assessment == null
                                ? new AssessmentReport()
                                : (safeReplay.Assessment.Report ?? new AssessmentReport())),
                        AugmentedReport = BuildAugmentedReport(
                            safeReplay.Assessment == null
                                ? new AssessmentReport()
                                : (safeReplay.Assessment.Report ?? new AssessmentReport()),
                            safeNarrative),
                        Narrative = safeNarrative
                    });
            }

            var sessionPackage = new SvrFireAssessmentSessionPackage
            {
                ExportedAtUtc = DateTime.UtcNow.ToString("o"),
                SourceSessionDirectory = safeSessionLog.SessionDirectory ?? string.Empty,
                SourceManifestPath = safeSessionLog.ManifestPath ?? string.Empty,
                SourceEventsPath = safeSessionLog.EventsPath ?? string.Empty,
                Manifest = safeSessionLog.Manifest ?? new RunLogManifestDocument(),
                SessionRecord = safeReplay.SessionRecord ?? new AuditSessionRecord(),
                Assessment = safeReplay.Assessment ?? new AssessmentArtifacts(),
                Narrative = safeNarrative,
                Timeline = safeReplay.Timeline ?? new List<AuditEvent>()
            };
            WriteJson(Path.Combine(exportDirectory, "session-package.json"), sessionPackage);

            File.WriteAllText(
                Path.Combine(exportDirectory, "review-summary.txt"),
                BuildReviewSummary(safeSessionLog, safeReplay, safeNarrative),
                Encoding.UTF8);

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

        private static string BuildReviewSummary(
            SimulationRunSessionLog sessionLog,
            SvrFireAssessmentReplayResult replay,
            AssessmentNarrative narrative)
        {
            var assessment = replay.Assessment == null ? new AssessmentArtifacts() : replay.Assessment;
            var result = assessment.Result ?? new AssessmentResult();
            var report = assessment.Report ?? new AssessmentReport();
            var safeNarrative = narrative ?? new AssessmentNarrative();
            var builder = new StringBuilder(512);

            builder.AppendLine("SVR Fire Assessment Export");
            builder.AppendLine("Session: " + SafeValue(replay.SessionId));
            builder.AppendLine("Source: " + SafeValue(sessionLog.SessionDirectory));
            builder.AppendLine(
                "Score: " +
                result.TotalPoints +
                "/" +
                result.MaxPoints +
                " (" +
                SafeValue(result.Band) +
                ")");
            builder.AppendLine("Session State: " + SafeValue(replay.SessionState));
            builder.AppendLine("Phase: " + SafeValue(replay.Phase));
            builder.AppendLine("Timeline Events: " + (replay.Timeline == null ? 0 : replay.Timeline.Count));
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
