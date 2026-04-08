using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GemmaHackathon.SimulationFramework;
using GemmaHackathon.SimulationScenarios.SvrFire;

namespace GemmaHackathon.Backend.AssessmentCli
{
    internal sealed class RunLogManifestMetadata
    {
        public string SchemaVersion = string.Empty;
        public string SessionId = string.Empty;
        public string SessionLabel = string.Empty;
        public string AppId = string.Empty;
        public string StartedAtUtc = string.Empty;
        public string SceneName = string.Empty;
        public string Platform = string.Empty;
        public string RequestedRuntimeMode = string.Empty;
        public string SelectedRuntimeMode = string.Empty;
        public string LoggerVerbosity = string.Empty;
    }

    internal sealed class RunLogManifestSummary
    {
        public string SchemaVersion = string.Empty;
        public string SessionId = string.Empty;
        public string EndedAtUtc = string.Empty;
        public string EndReason = string.Empty;
        public string FinalBackendName = string.Empty;
        public string FinalRuntimeMode = string.Empty;
        public int TotalTurns;
        public int SuccessfulTurns;
        public int FailedTurns;
        public string LastError = string.Empty;
    }

    internal sealed class RunLogManifestDocument
    {
        public RunLogManifestMetadata Metadata = new RunLogManifestMetadata();
        public RunLogManifestSummary Summary = new RunLogManifestSummary();
    }

    internal sealed class SimulationRunSessionLog
    {
        public string SessionDirectory = string.Empty;
        public string EventsPath = string.Empty;
        public string ManifestPath = string.Empty;
        public AuditSessionRecord SessionRecord = new AuditSessionRecord();
        public RunLogManifestDocument Manifest = new RunLogManifestDocument();
        public List<AuditEvent> AuditEvents = new List<AuditEvent>();
    }

    internal static class SimulationRunLogReader
    {
        public static SimulationRunSessionLog Load(CliOptions options)
        {
            var sessionDirectory = ResolveSessionDirectory(options);
            var manifestPath = Path.Combine(sessionDirectory, "manifest.json");
            var eventsPath = Path.Combine(sessionDirectory, "events.jsonl");

            if (!File.Exists(manifestPath))
            {
                throw new FileNotFoundException("Missing manifest.json for session `" + sessionDirectory + "`.", manifestPath);
            }

            if (!File.Exists(eventsPath))
            {
                throw new FileNotFoundException("Missing events.jsonl for session `" + sessionDirectory + "`.", eventsPath);
            }

            var manifest = ReadManifest(manifestPath);
            var auditEvents = ReadAuditEvents(eventsPath);
            var sessionRecord = BuildSessionRecord(manifest, auditEvents, Path.GetFileName(sessionDirectory));

            return new SimulationRunSessionLog
            {
                SessionDirectory = sessionDirectory,
                EventsPath = eventsPath,
                ManifestPath = manifestPath,
                SessionRecord = sessionRecord,
                Manifest = manifest,
                AuditEvents = auditEvents
            };
        }

        private static string ResolveSessionDirectory(CliOptions options)
        {
            var logRoot = RepositoryPaths.GetRunLogRoot();
            if (!Directory.Exists(logRoot))
            {
                throw new DirectoryNotFoundException("Run log root not found: " + logRoot);
            }

            if (options == null || options.UseLatest || string.IsNullOrWhiteSpace(options.SessionSelector))
            {
                return FindLatestSessionDirectory(logRoot);
            }

            var selector = options.SessionSelector.Trim();
            if (Directory.Exists(selector))
            {
                return Path.GetFullPath(selector);
            }

            if (File.Exists(selector))
            {
                return Path.GetDirectoryName(Path.GetFullPath(selector)) ?? string.Empty;
            }

            var directChild = Directory
                .EnumerateDirectories(logRoot, selector, SearchOption.AllDirectories)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(directChild))
            {
                return directChild;
            }

            throw new DirectoryNotFoundException(
                "Could not resolve a session directory for selector `" + selector + "` under `" + logRoot + "`.");
        }

        private static string FindLatestSessionDirectory(string logRoot)
        {
            var manifestPaths = Directory
                .EnumerateFiles(logRoot, "manifest.json", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .ToList();

            if (manifestPaths.Count == 0)
            {
                throw new FileNotFoundException("No session manifests were found under `" + logRoot + "`.");
            }

            return manifestPaths[0].DirectoryName ?? string.Empty;
        }

        private static RunLogManifestDocument ReadManifest(string manifestPath)
        {
            using (var document = JsonDocument.Parse(File.ReadAllText(manifestPath)))
            {
                var manifest = new RunLogManifestDocument();
                JsonElement metadataElement;
                if (document.RootElement.TryGetProperty("metadata", out metadataElement))
                {
                    manifest.Metadata.SchemaVersion = ReadString(metadataElement, "schema_version");
                    manifest.Metadata.SessionId = ReadString(metadataElement, "session_id");
                    manifest.Metadata.SessionLabel = ReadString(metadataElement, "session_label");
                    manifest.Metadata.AppId = ReadString(metadataElement, "app_id");
                    manifest.Metadata.StartedAtUtc = ReadString(metadataElement, "started_at_utc");
                    manifest.Metadata.SceneName = ReadString(metadataElement, "scene_name");
                    manifest.Metadata.Platform = ReadString(metadataElement, "platform");
                    manifest.Metadata.RequestedRuntimeMode = ReadString(metadataElement, "requested_runtime_mode");
                    manifest.Metadata.SelectedRuntimeMode = ReadString(metadataElement, "selected_runtime_mode");
                    manifest.Metadata.LoggerVerbosity = ReadString(metadataElement, "logger_verbosity");
                }

                JsonElement summaryElement;
                if (document.RootElement.TryGetProperty("summary", out summaryElement))
                {
                    manifest.Summary.SchemaVersion = ReadString(summaryElement, "schema_version");
                    manifest.Summary.SessionId = ReadString(summaryElement, "session_id");
                    manifest.Summary.EndedAtUtc = ReadString(summaryElement, "ended_at_utc");
                    manifest.Summary.EndReason = ReadString(summaryElement, "end_reason");
                    manifest.Summary.FinalBackendName = ReadString(summaryElement, "final_backend_name");
                    manifest.Summary.FinalRuntimeMode = ReadString(summaryElement, "final_runtime_mode");
                    manifest.Summary.TotalTurns = ReadInt(summaryElement, "total_turns");
                    manifest.Summary.SuccessfulTurns = ReadInt(summaryElement, "successful_turns");
                    manifest.Summary.FailedTurns = ReadInt(summaryElement, "failed_turns");
                    manifest.Summary.LastError = ReadString(summaryElement, "last_error");
                }

                return manifest;
            }
        }

        private static List<AuditEvent> ReadAuditEvents(string eventsPath)
        {
            var result = new List<AuditEvent>();
            foreach (var line in File.ReadLines(eventsPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using (var document = JsonDocument.Parse(line))
                {
                    var root = document.RootElement;
                    if (!string.Equals(ReadString(root, "family"), "audit", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    JsonElement payload;
                    if (!root.TryGetProperty("payload", out payload) || payload.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    result.Add(new AuditEvent
                    {
                        SessionId = ReadString(payload, "session_id"),
                        EventId = ReadString(payload, "event_id"),
                        CorrelationId = ReadString(payload, "correlation_id"),
                        ScenarioVariant = ReadString(payload, "scenario_variant"),
                        RubricVersion = ReadString(payload, "rubric_version"),
                        ScoringVersion = ReadString(payload, "scoring_version"),
                        TimestampUtc = ReadString(payload, "timestamp_utc"),
                        ElapsedMilliseconds = ReadInt(payload, "elapsed_ms"),
                        Phase = ReadString(payload, "phase"),
                        Source = ReadString(payload, "source"),
                        ActionCode = ReadString(payload, "action_code"),
                        Actor = ReadString(payload, "actor"),
                        Target = ReadString(payload, "target"),
                        Outcome = ReadString(payload, "outcome"),
                        DataJson = ReadRawObject(payload, "data")
                    });
                }
            }

            return result;
        }

        private static AuditSessionRecord BuildSessionRecord(
            RunLogManifestDocument manifest,
            List<AuditEvent> auditEvents,
            string fallbackSessionId)
        {
            var sessionRecord = new AuditSessionRecord();
            sessionRecord.SessionId = !string.IsNullOrWhiteSpace(manifest.Metadata.SessionId)
                ? manifest.Metadata.SessionId
                : fallbackSessionId;
            sessionRecord.ParticipantAlias = SvrFireScenarioValues.DefaultParticipantAlias;
            sessionRecord.RuntimeBackend = manifest.Summary.FinalBackendName ?? string.Empty;

            if (auditEvents != null && auditEvents.Count > 0)
            {
                sessionRecord.ScenarioVariant = FirstNonEmpty(
                    auditEvents,
                    static item => item.ScenarioVariant,
                    SvrFireScenarioValues.DefaultVariantId);
                sessionRecord.RubricVersion = FirstNonEmpty(
                    auditEvents,
                    static item => item.RubricVersion,
                    SvrFireScenarioValues.DefaultRubricVersion);
                sessionRecord.ScoringVersion = FirstNonEmpty(
                    auditEvents,
                    static item => item.ScoringVersion,
                    SvrFireScenarioValues.DefaultScoringVersion);
                sessionRecord.SessionPhase = auditEvents[auditEvents.Count - 1].Phase ?? string.Empty;
            }
            else
            {
                sessionRecord.ScenarioVariant = SvrFireScenarioValues.DefaultVariantId;
                sessionRecord.RubricVersion = SvrFireScenarioValues.DefaultRubricVersion;
                sessionRecord.ScoringVersion = SvrFireScenarioValues.DefaultScoringVersion;
                sessionRecord.SessionPhase = string.Empty;
            }

            return sessionRecord;
        }

        private static string FirstNonEmpty(
            List<AuditEvent> auditEvents,
            Func<AuditEvent, string> selector,
            string fallback)
        {
            for (var i = 0; i < auditEvents.Count; i++)
            {
                var candidate = selector(auditEvents[i] ?? new AuditEvent());
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    return candidate;
                }
            }

            return fallback ?? string.Empty;
        }

        private static string ReadString(JsonElement element, string propertyName)
        {
            JsonElement value;
            if (!element.TryGetProperty(propertyName, out value) || value.ValueKind != JsonValueKind.String)
            {
                return string.Empty;
            }

            return value.GetString() ?? string.Empty;
        }

        private static int ReadInt(JsonElement element, string propertyName)
        {
            JsonElement value;
            if (!element.TryGetProperty(propertyName, out value))
            {
                return 0;
            }

            return value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;
        }

        private static string ReadRawObject(JsonElement element, string propertyName)
        {
            JsonElement value;
            if (!element.TryGetProperty(propertyName, out value))
            {
                return "{}";
            }

            return value.GetRawText();
        }
    }
}
