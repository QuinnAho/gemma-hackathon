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
        public List<AuditEvent> Timeline = new List<AuditEvent>();
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
            string outputDirectory)
        {
            var safeSessionLog = sessionLog ?? new SimulationRunSessionLog();
            var safeReplay = replay ?? new SvrFireAssessmentReplayResult();
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

            var sessionPackage = new SvrFireAssessmentSessionPackage
            {
                ExportedAtUtc = DateTime.UtcNow.ToString("o"),
                SourceSessionDirectory = safeSessionLog.SessionDirectory ?? string.Empty,
                SourceManifestPath = safeSessionLog.ManifestPath ?? string.Empty,
                SourceEventsPath = safeSessionLog.EventsPath ?? string.Empty,
                Manifest = safeSessionLog.Manifest ?? new RunLogManifestDocument(),
                SessionRecord = safeReplay.SessionRecord ?? new AuditSessionRecord(),
                Assessment = safeReplay.Assessment ?? new AssessmentArtifacts(),
                Timeline = safeReplay.Timeline ?? new List<AuditEvent>()
            };
            WriteJson(Path.Combine(exportDirectory, "session-package.json"), sessionPackage);

            File.WriteAllText(
                Path.Combine(exportDirectory, "review-summary.txt"),
                BuildReviewSummary(safeSessionLog, safeReplay),
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
            SvrFireAssessmentReplayResult replay)
        {
            var assessment = replay.Assessment == null ? new AssessmentArtifacts() : replay.Assessment;
            var result = assessment.Result ?? new AssessmentResult();
            var report = assessment.Report ?? new AssessmentReport();
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

            return builder.ToString();
        }

        private static string SafeValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(none)" : value.Trim();
        }
    }
}
