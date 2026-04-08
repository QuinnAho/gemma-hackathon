using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace GemmaHackathon.SimulationFramework
{
    public interface ISimulationKpiProvider
    {
        SimulationKpiSnapshot CaptureKpis();
    }

    [Serializable]
    public sealed class SimulationKpiEntry
    {
        public string Key = string.Empty;
        public string Label = string.Empty;
        public string ValueJson = "null";
    }

    [Serializable]
    public sealed class SimulationKpiSnapshot
    {
        public List<SimulationKpiEntry> Entries = new List<SimulationKpiEntry>();

        public bool HasEntries
        {
            get { return Entries != null && Entries.Count > 0; }
        }

        public SimulationKpiSnapshot Clone()
        {
            var clone = new SimulationKpiSnapshot();
            if (Entries == null)
            {
                return clone;
            }

            for (var i = 0; i < Entries.Count; i++)
            {
                var entry = Entries[i] ?? new SimulationKpiEntry();
                clone.Entries.Add(new SimulationKpiEntry
                {
                    Key = entry.Key ?? string.Empty,
                    Label = entry.Label ?? string.Empty,
                    ValueJson = string.IsNullOrWhiteSpace(entry.ValueJson) ? "null" : entry.ValueJson
                });
            }

            return clone;
        }

        public string ToStructuredJson()
        {
            var builder = new StringBuilder(256);
            builder.Append("{\"entries\":[");

            if (Entries != null)
            {
                for (var i = 0; i < Entries.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(',');
                    }

                    var entry = Entries[i] ?? new SimulationKpiEntry();
                    builder.Append('{');
                    JsonText.AppendStringProperty(builder, "key", entry.Key);
                    builder.Append(',');
                    JsonText.AppendStringProperty(builder, "label", entry.Label);
                    builder.Append(",\"value\":");
                    builder.Append(string.IsNullOrWhiteSpace(entry.ValueJson) ? "null" : entry.ValueJson);
                    builder.Append('}');
                }
            }

            builder.Append("]}");
            return builder.ToString();
        }
    }

    public enum SimulationRunLogVerbosity
    {
        Compact,
        Verbose
    }

    public interface ISimulationRunLogger : IDisposable
    {
        string SessionId { get; }
        SimulationRunLogVerbosity Verbosity { get; }
        void StartSession(SimulationRunSessionMetadata metadata);
        void LogEvent(SimulationRunLogEvent logEvent);
        void EndSession(SimulationRunSessionSummary summary);
        void Flush();
    }

    public interface ISimulationRunLoggerDiagnostics
    {
        bool IsSessionStarted { get; }
        int WrittenEventCount { get; }
        int FailureCount { get; }
        string LastError { get; }
        string SessionDirectoryPath { get; }
        string EventsPath { get; }
        string ManifestPath { get; }
    }

    [Serializable]
    public sealed class SimulationRunSessionMetadata
    {
        public string SchemaVersion = SimulationRunLogging.SchemaVersion;
        public string SessionId = string.Empty;
        public string SessionLabel = string.Empty;
        public string AppId = string.Empty;
        public string StartedAtUtc = string.Empty;
        public string SceneName = string.Empty;
        public string Platform = string.Empty;
        public string RequestedRuntimeMode = string.Empty;
        public string SelectedRuntimeMode = string.Empty;
        public string LoggerVerbosity = string.Empty;

        public string ToJson()
        {
            var builder = new StringBuilder(256);
            builder.Append('{');
            JsonText.AppendStringProperty(builder, "schema_version", SchemaVersion);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "session_id", SessionId);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "session_label", SessionLabel);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "app_id", AppId);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "started_at_utc", StartedAtUtc);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "scene_name", SceneName);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "platform", Platform);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "requested_runtime_mode", RequestedRuntimeMode);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "selected_runtime_mode", SelectedRuntimeMode);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "logger_verbosity", LoggerVerbosity);
            builder.Append('}');
            return builder.ToString();
        }
    }

    [Serializable]
    public sealed class SimulationRunSessionSummary
    {
        public string SchemaVersion = SimulationRunLogging.SchemaVersion;
        public string SessionId = string.Empty;
        public string EndedAtUtc = string.Empty;
        public string EndReason = string.Empty;
        public string FinalBackendName = string.Empty;
        public string FinalRuntimeMode = string.Empty;
        public int TotalTurns;
        public int SuccessfulTurns;
        public int FailedTurns;
        public string LastError = string.Empty;

        public string ToJson()
        {
            var builder = new StringBuilder(256);
            builder.Append('{');
            JsonText.AppendStringProperty(builder, "schema_version", SchemaVersion);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "session_id", SessionId);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "ended_at_utc", EndedAtUtc);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "end_reason", EndReason);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "final_backend_name", FinalBackendName);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "final_runtime_mode", FinalRuntimeMode);
            builder.Append(',');
            SimulationRunJson.AppendIntProperty(builder, "total_turns", TotalTurns);
            builder.Append(',');
            SimulationRunJson.AppendIntProperty(builder, "successful_turns", SuccessfulTurns);
            builder.Append(',');
            SimulationRunJson.AppendIntProperty(builder, "failed_turns", FailedTurns);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "last_error", LastError);
            builder.Append('}');
            return builder.ToString();
        }
    }

    [Serializable]
    public sealed class SimulationRunLogEvent
    {
        public string SchemaVersion = SimulationRunLogging.SchemaVersion;
        public string EventId = string.Empty;
        public string SessionId = string.Empty;
        public string TurnId = string.Empty;
        public string CorrelationId = string.Empty;
        public string Family = string.Empty;
        public string Kind = string.Empty;
        public string TimestampUtc = string.Empty;
        public string PayloadJson = "{}";

        public string ToJson()
        {
            var builder = new StringBuilder(512);
            builder.Append('{');
            JsonText.AppendStringProperty(builder, "schema_version", SchemaVersion);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "event_id", EventId);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "session_id", SessionId);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "turn_id", TurnId);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "correlation_id", CorrelationId);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "family", Family);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "kind", Kind);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "timestamp_utc", TimestampUtc);
            builder.Append(",\"payload\":");
            builder.Append(string.IsNullOrWhiteSpace(PayloadJson) ? "{}" : PayloadJson);
            builder.Append('}');
            return builder.ToString();
        }
    }

    [Serializable]
    public sealed class SimulationRunRuntimeRecord
    {
        public string RequestedRuntimeMode = string.Empty;
        public string SelectedRuntimeMode = string.Empty;
        public string ActiveBackendName = string.Empty;
        public string ModelSource = string.Empty;
        public string SafeModelReference = string.Empty;
        public string RuntimeSummary = string.Empty;
        public string BootstrapState = string.Empty;
        public string BootstrapError = string.Empty;
        public string HealthCheckResponse = string.Empty;
        public bool SupportsTextCompletion = true;
        public bool SupportsToolCalling = true;
        public bool SupportsSpeechTranscription;
        public bool UsesLiveModel;
        public bool IsTargetRuntime;
        public bool IsFallback;

        public string ToJson(SimulationRunLogVerbosity verbosity)
        {
            var builder = new StringBuilder(384);
            builder.Append('{');
            JsonText.AppendStringProperty(builder, "requested_runtime_mode", RequestedRuntimeMode);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "selected_runtime_mode", SelectedRuntimeMode);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "active_backend_name", ActiveBackendName);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "model_source", ModelSource);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "safe_model_reference", SafeModelReference);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "bootstrap_state", BootstrapState);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "bootstrap_error", BootstrapError);
            builder.Append(',');
            SimulationRunJson.AppendBoolProperty(builder, "supports_text_completion", SupportsTextCompletion);
            builder.Append(',');
            SimulationRunJson.AppendBoolProperty(builder, "supports_tool_calling", SupportsToolCalling);
            builder.Append(',');
            SimulationRunJson.AppendBoolProperty(builder, "supports_speech_transcription", SupportsSpeechTranscription);
            builder.Append(',');
            SimulationRunJson.AppendBoolProperty(builder, "uses_live_model", UsesLiveModel);
            builder.Append(',');
            SimulationRunJson.AppendBoolProperty(builder, "is_target_runtime", IsTargetRuntime);
            builder.Append(',');
            SimulationRunJson.AppendBoolProperty(builder, "is_fallback", IsFallback);

            if (verbosity == SimulationRunLogVerbosity.Verbose)
            {
                builder.Append(',');
                JsonText.AppendStringProperty(builder, "runtime_summary", RuntimeSummary);
                builder.Append(',');
                JsonText.AppendStringProperty(builder, "health_check_response", HealthCheckResponse);
            }

            builder.Append('}');
            return builder.ToString();
        }
    }

    [Serializable]
    public sealed class SimulationRunTurnRecord
    {
        public string InputRole = string.Empty;
        public string InputContent = string.Empty;
        public bool Success;
        public bool AppliedToHistory;
        public bool CloudHandoffRequested;
        public bool ReachedToolRoundTripLimit;
        public string FinalAssistantResponse = string.Empty;
        public string Error = string.Empty;
        public int CompletionCount;
        public double TotalCompletionTimeMs;
        public int AssistantResponseCount;
        public int ToolResultCount;

        public string ToJson(SimulationRunLogVerbosity verbosity)
        {
            var builder = new StringBuilder(320);
            builder.Append('{');
            JsonText.AppendStringProperty(builder, "input_role", InputRole);
            builder.Append(',');
            SimulationRunJson.AppendBoolProperty(builder, "success", Success);
            builder.Append(',');
            SimulationRunJson.AppendBoolProperty(builder, "applied_to_history", AppliedToHistory);
            builder.Append(',');
            SimulationRunJson.AppendBoolProperty(builder, "cloud_handoff_requested", CloudHandoffRequested);
            builder.Append(',');
            SimulationRunJson.AppendBoolProperty(builder, "reached_tool_round_trip_limit", ReachedToolRoundTripLimit);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "error", Error);
            builder.Append(',');
            SimulationRunJson.AppendIntProperty(builder, "completion_count", CompletionCount);
            builder.Append(',');
            SimulationRunJson.AppendNullableDoubleProperty(builder, "total_completion_time_ms", TotalCompletionTimeMs);
            builder.Append(',');
            SimulationRunJson.AppendIntProperty(builder, "assistant_response_count", AssistantResponseCount);
            builder.Append(',');
            SimulationRunJson.AppendIntProperty(builder, "tool_result_count", ToolResultCount);

            if (verbosity == SimulationRunLogVerbosity.Verbose)
            {
                builder.Append(',');
                JsonText.AppendStringProperty(builder, "input_content", InputContent);
                builder.Append(',');
                JsonText.AppendStringProperty(builder, "final_assistant_response", FinalAssistantResponse);
            }

            builder.Append('}');
            return builder.ToString();
        }
    }

    [Serializable]
    public sealed class SimulationRunTraceRecord
    {
        public string TraceKind = string.Empty;
        public string Content = string.Empty;

        public string ToJson()
        {
            var builder = new StringBuilder(192);
            builder.Append('{');
            JsonText.AppendStringProperty(builder, "trace_kind", TraceKind);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "content", Content);
            builder.Append('}');
            return builder.ToString();
        }
    }

    [Serializable]
    public sealed class SimulationRunCompletionRecord
    {
        public bool Success;
        public bool ParseSucceeded;
        public bool CloudHandoff;
        public string Error = string.Empty;
        public string Response = string.Empty;
        public int FunctionCallCount;
        public double? Confidence;
        public double? TimeToFirstTokenMs;
        public double? TotalTimeMs;
        public double? PrefillTokensPerSecond;
        public double? DecodeTokensPerSecond;
        public double? RamUsageMb;
        public int? PrefillTokens;
        public int? DecodeTokens;
        public int? TotalTokens;
        public string RawJson = string.Empty;

        public string ToJson(SimulationRunLogVerbosity verbosity)
        {
            var builder = new StringBuilder(512);
            builder.Append('{');
            SimulationRunJson.AppendBoolProperty(builder, "success", Success);
            builder.Append(',');
            SimulationRunJson.AppendBoolProperty(builder, "parse_succeeded", ParseSucceeded);
            builder.Append(',');
            SimulationRunJson.AppendBoolProperty(builder, "cloud_handoff", CloudHandoff);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "error", Error);
            builder.Append(',');
            SimulationRunJson.AppendIntProperty(builder, "function_call_count", FunctionCallCount);
            builder.Append(',');
            SimulationRunJson.AppendNullableDoubleProperty(builder, "confidence", Confidence);
            builder.Append(',');
            SimulationRunJson.AppendNullableDoubleProperty(builder, "time_to_first_token_ms", TimeToFirstTokenMs);
            builder.Append(',');
            SimulationRunJson.AppendNullableDoubleProperty(builder, "total_time_ms", TotalTimeMs);
            builder.Append(',');
            SimulationRunJson.AppendNullableDoubleProperty(builder, "prefill_tps", PrefillTokensPerSecond);
            builder.Append(',');
            SimulationRunJson.AppendNullableDoubleProperty(builder, "decode_tps", DecodeTokensPerSecond);
            builder.Append(',');
            SimulationRunJson.AppendNullableDoubleProperty(builder, "ram_usage_mb", RamUsageMb);
            builder.Append(',');
            SimulationRunJson.AppendNullableIntProperty(builder, "prefill_tokens", PrefillTokens);
            builder.Append(',');
            SimulationRunJson.AppendNullableIntProperty(builder, "decode_tokens", DecodeTokens);
            builder.Append(',');
            SimulationRunJson.AppendNullableIntProperty(builder, "total_tokens", TotalTokens);

            if (verbosity == SimulationRunLogVerbosity.Verbose)
            {
                builder.Append(',');
                JsonText.AppendStringProperty(builder, "response", Response);
                builder.Append(',');
                JsonText.AppendStringProperty(builder, "raw_json", RawJson);
            }

            builder.Append('}');
            return builder.ToString();
        }
    }

    [Serializable]
    public sealed class SimulationRunToolRecord
    {
        public string Name = string.Empty;
        public string ArgumentsJson = "{}";
        public string Content = string.Empty;
        public bool IsError;
        public double? DurationMs;

        public string ToJson(SimulationRunLogVerbosity verbosity)
        {
            var builder = new StringBuilder(256);
            builder.Append('{');
            JsonText.AppendStringProperty(builder, "name", Name);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "arguments_json", ArgumentsJson);
            builder.Append(',');
            SimulationRunJson.AppendBoolProperty(builder, "is_error", IsError);
            builder.Append(',');
            SimulationRunJson.AppendNullableDoubleProperty(builder, "duration_ms", DurationMs);

            if (verbosity == SimulationRunLogVerbosity.Verbose)
            {
                builder.Append(',');
                JsonText.AppendStringProperty(builder, "content", Content);
            }

            builder.Append('}');
            return builder.ToString();
        }
    }

    [Serializable]
    public sealed class SimulationRunErrorRecord
    {
        public string Source = string.Empty;
        public string Message = string.Empty;
        public string Details = string.Empty;

        public string ToJson()
        {
            var builder = new StringBuilder(192);
            builder.Append('{');
            JsonText.AppendStringProperty(builder, "source", Source);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "message", Message);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "details", Details);
            builder.Append('}');
            return builder.ToString();
        }
    }

    public sealed class LocalJsonlSimulationRunLoggerOptions
    {
        public string RootPath = string.Empty;
        public string SessionGroupPath = string.Empty;
        public string SessionPrefix = "simulation-run";
        public SimulationRunLogVerbosity Verbosity = SimulationRunLogVerbosity.Compact;
        public bool AutoFlush = true;
    }

    public sealed class LocalJsonlSimulationRunLogger : ISimulationRunLogger, ISimulationRunLoggerDiagnostics
    {
        private readonly object _syncRoot = new object();
        private readonly LocalJsonlSimulationRunLoggerOptions _options;
        private readonly string _sessionDirectory;
        private readonly string _eventsPath;
        private readonly string _manifestPath;
        private readonly StreamWriter _eventsWriter;
        private SimulationRunSessionMetadata _sessionMetadata;
        private SimulationRunSessionSummary _sessionSummary;
        private bool _isDisposed;
        private bool _isSessionStarted;
        private int _writtenEventCount;
        private int _failureCount;
        private string _lastError = string.Empty;

        public LocalJsonlSimulationRunLogger(LocalJsonlSimulationRunLoggerOptions options = null)
        {
            _options = options ?? new LocalJsonlSimulationRunLoggerOptions();
            SessionId = SimulationRunLogging.CreateIdentifier(
                string.IsNullOrWhiteSpace(_options.SessionPrefix) ? "simulation-run" : _options.SessionPrefix);

            var rootPath = string.IsNullOrWhiteSpace(_options.RootPath)
                ? SimulationRunLogging.ResolveRunLogRootPath("simulation-runs")
                : ResolveRootPath(_options.RootPath);
            var sessionGroupPath = NormalizeRelativeGroupPath(_options.SessionGroupPath);
            _sessionDirectory = string.IsNullOrWhiteSpace(sessionGroupPath)
                ? Path.Combine(rootPath, SessionId)
                : Path.Combine(rootPath, sessionGroupPath, SessionId);
            _eventsPath = Path.Combine(_sessionDirectory, "events.jsonl");
            _manifestPath = Path.Combine(_sessionDirectory, "manifest.json");

            Directory.CreateDirectory(_sessionDirectory);

            var eventStream = new FileStream(_eventsPath, FileMode.Append, FileAccess.Write, FileShare.Read);
            _eventsWriter = new StreamWriter(eventStream, Encoding.UTF8);
            _eventsWriter.AutoFlush = _options.AutoFlush;
        }

        public string SessionId { get; private set; }

        public SimulationRunLogVerbosity Verbosity
        {
            get { return _options.Verbosity; }
        }

        public bool IsSessionStarted
        {
            get
            {
                lock (_syncRoot)
                {
                    return _isSessionStarted;
                }
            }
        }

        public int WrittenEventCount
        {
            get
            {
                lock (_syncRoot)
                {
                    return _writtenEventCount;
                }
            }
        }

        public int FailureCount
        {
            get
            {
                lock (_syncRoot)
                {
                    return _failureCount;
                }
            }
        }

        public string LastError
        {
            get
            {
                lock (_syncRoot)
                {
                    return _lastError ?? string.Empty;
                }
            }
        }

        public string SessionDirectoryPath
        {
            get { return _sessionDirectory ?? string.Empty; }
        }

        public string EventsPath
        {
            get { return _eventsPath ?? string.Empty; }
        }

        public string ManifestPath
        {
            get { return _manifestPath ?? string.Empty; }
        }

        public void StartSession(SimulationRunSessionMetadata metadata)
        {
            lock (_syncRoot)
            {
                if (_isDisposed)
                {
                    return;
                }

                _sessionMetadata = metadata ?? new SimulationRunSessionMetadata();
                _sessionMetadata.SchemaVersion = SimulationRunLogging.SchemaVersion;
                _sessionMetadata.SessionId = SessionId;
                if (string.IsNullOrWhiteSpace(_sessionMetadata.StartedAtUtc))
                {
                    _sessionMetadata.StartedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                }

                if (string.IsNullOrWhiteSpace(_sessionMetadata.LoggerVerbosity))
                {
                    _sessionMetadata.LoggerVerbosity = Verbosity.ToString();
                }

                WriteManifestLocked();
                WriteEventLocked(new SimulationRunLogEvent
                {
                    Family = "session",
                    Kind = "started",
                    PayloadJson = _sessionMetadata.ToJson()
                });
                _isSessionStarted = true;
            }
        }

        public void LogEvent(SimulationRunLogEvent logEvent)
        {
            lock (_syncRoot)
            {
                if (_isDisposed)
                {
                    return;
                }

                WriteEventLocked(logEvent);
            }
        }

        public void EndSession(SimulationRunSessionSummary summary)
        {
            lock (_syncRoot)
            {
                if (_isDisposed)
                {
                    return;
                }

                _sessionSummary = summary ?? new SimulationRunSessionSummary();
                _sessionSummary.SchemaVersion = SimulationRunLogging.SchemaVersion;
                _sessionSummary.SessionId = SessionId;
                if (string.IsNullOrWhiteSpace(_sessionSummary.EndedAtUtc))
                {
                    _sessionSummary.EndedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                }

                WriteManifestLocked();
                WriteEventLocked(new SimulationRunLogEvent
                {
                    Family = "session",
                    Kind = "ended",
                    PayloadJson = _sessionSummary.ToJson()
                });
            }
        }

        public void Flush()
        {
            lock (_syncRoot)
            {
                if (_isDisposed)
                {
                    return;
                }

                try
                {
                    _eventsWriter.Flush();
                }
                catch (Exception ex)
                {
                    RecordFailure(ex);
                }
            }
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                if (_isDisposed)
                {
                    return;
                }

                _isDisposed = true;

                try
                {
                    _eventsWriter.Flush();
                }
                catch (Exception ex)
                {
                    RecordFailure(ex);
                }

                try
                {
                    _eventsWriter.Dispose();
                }
                catch (Exception ex)
                {
                    RecordFailure(ex);
                }
            }
        }

        private void WriteEventLocked(SimulationRunLogEvent logEvent)
        {
            if (logEvent == null)
            {
                return;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(logEvent.SchemaVersion))
                {
                    logEvent.SchemaVersion = SimulationRunLogging.SchemaVersion;
                }

                if (string.IsNullOrWhiteSpace(logEvent.SessionId))
                {
                    logEvent.SessionId = SessionId;
                }

                if (string.IsNullOrWhiteSpace(logEvent.EventId))
                {
                    logEvent.EventId = SimulationRunLogging.CreateIdentifier("event");
                }

                if (string.IsNullOrWhiteSpace(logEvent.TimestampUtc))
                {
                    logEvent.TimestampUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                }

                _eventsWriter.WriteLine(logEvent.ToJson());
                _writtenEventCount++;
            }
            catch (Exception ex)
            {
                RecordFailure(ex);
            }
        }

        private void WriteManifestLocked()
        {
            try
            {
                var builder = new StringBuilder(512);
                builder.Append('{');
                builder.Append("\"metadata\":");
                builder.Append(_sessionMetadata == null ? "null" : _sessionMetadata.ToJson());
                builder.Append(",\"summary\":");
                builder.Append(_sessionSummary == null ? "null" : _sessionSummary.ToJson());
                builder.Append('}');
                File.WriteAllText(_manifestPath, builder.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                RecordFailure(ex);
            }
        }

        private void RecordFailure(Exception ex)
        {
            _failureCount++;
            _lastError = ex == null
                ? "Logger operation failed."
                : ex.GetType().Name + ": " + ex.Message;
        }

        private static string ResolveRootPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return SimulationRunLogging.ResolveRunLogRootPath("simulation-runs");
            }

            return Path.IsPathRooted(value)
                ? Path.GetFullPath(value)
                : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), value));
        }

        private static string NormalizeRelativeGroupPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var segments = value.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            var builder = new StringBuilder(128);

            for (var i = 0; i < segments.Length; i++)
            {
                var sanitizedSegment = SanitizePathSegment(segments[i]);
                if (string.IsNullOrWhiteSpace(sanitizedSegment))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(Path.DirectorySeparatorChar);
                }

                builder.Append(sanitizedSegment);
            }

            return builder.ToString();
        }

        private static string SanitizePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var invalidCharacters = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);

            for (var i = 0; i < value.Length; i++)
            {
                var current = value[i];
                if (Array.IndexOf(invalidCharacters, current) >= 0)
                {
                    builder.Append('_');
                    continue;
                }

                builder.Append(char.IsWhiteSpace(current) ? '-' : current);
            }

            return builder.ToString().Trim('.', ' ');
        }
    }

    public static class SimulationRunLogging
    {
        public const string SchemaVersion = "1.0.0";
        public const string DefaultRunLogRootProperty = "paths.simulationRunLogRoot";

        public static string CreateIdentifier(string prefix)
        {
            var safePrefix = string.IsNullOrWhiteSpace(prefix) ? "id" : prefix.Trim();
            return safePrefix + "_" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture) + "_" +
                   Guid.NewGuid().ToString("N");
        }

        public static string ResolveDefaultRootPath(string folderName)
        {
            return ResolveRunLogRootPath(folderName);
        }

        public static string ResolveRunLogRootPath(
            string folderName = "simulation-runs",
            string configPropertyPath = DefaultRunLogRootProperty)
        {
            var repoRoot = ResolveRepoRoot();
            var leafFolder = string.IsNullOrWhiteSpace(folderName) ? "simulation-runs" : folderName.Trim();

            if (!string.IsNullOrWhiteSpace(repoRoot))
            {
                var configuredRoot = ReadConfiguredRunLogRoot(repoRoot, configPropertyPath);
                if (!string.IsNullOrWhiteSpace(configuredRoot))
                {
                    return configuredRoot;
                }

                return Path.Combine(repoRoot, ".local", "logs", leafFolder);
            }

            var persistentRoot = string.IsNullOrWhiteSpace(Application.persistentDataPath)
                ? Application.temporaryCachePath
                : Application.persistentDataPath;
            return Path.Combine(persistentRoot, leafFolder);
        }

        private static string ReadConfiguredRunLogRoot(string repoRoot, string configPropertyPath)
        {
            if (string.IsNullOrWhiteSpace(repoRoot) || string.IsNullOrWhiteSpace(configPropertyPath))
            {
                return string.Empty;
            }

            try
            {
                var configPath = Path.Combine(repoRoot, "config", "local.json");
                if (!File.Exists(configPath))
                {
                    return string.Empty;
                }

                var root = JsonDom.Parse(File.ReadAllText(configPath));
                if (root == null || root.Kind != JsonValueKind.Object)
                {
                    return string.Empty;
                }

                var configuredValue = ReadConfigString(root, configPropertyPath);
                if (string.IsNullOrWhiteSpace(configuredValue))
                {
                    return string.Empty;
                }

                return Path.IsPathRooted(configuredValue)
                    ? Path.GetFullPath(configuredValue)
                    : Path.GetFullPath(Path.Combine(repoRoot, configuredValue));
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ResolveRepoRoot()
        {
            var candidates = new List<string>();
            AddCandidate(candidates, Directory.GetCurrentDirectory());
            AddCandidate(candidates, AppDomain.CurrentDomain.BaseDirectory);

            if (!string.IsNullOrWhiteSpace(Application.dataPath))
            {
                AddCandidate(candidates, Application.dataPath);
                AddCandidate(candidates, Path.GetDirectoryName(Application.dataPath));
            }

            for (var i = 0; i < candidates.Count; i++)
            {
                var current = string.IsNullOrWhiteSpace(candidates[i])
                    ? null
                    : new DirectoryInfo(candidates[i]);

                while (current != null)
                {
                    if (LooksLikeRepoRoot(current.FullName))
                    {
                        return current.FullName;
                    }

                    current = current.Parent;
                }
            }

            return string.Empty;
        }

        private static void AddCandidate(List<string> candidates, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            try
            {
                var fullPath = Path.GetFullPath(value);
                if (!candidates.Contains(fullPath))
                {
                    candidates.Add(fullPath);
                }
            }
            catch
            {
            }
        }

        private static string ReadConfigString(JsonValue root, string propertyPath)
        {
            if (root == null || root.Kind != JsonValueKind.Object || string.IsNullOrWhiteSpace(propertyPath))
            {
                return string.Empty;
            }

            JsonValue current = root;
            var segments = propertyPath.Split('.');
            for (var i = 0; i < segments.Length; i++)
            {
                if (current == null || current.Kind != JsonValueKind.Object)
                {
                    return string.Empty;
                }

                JsonValue next;
                if (!current.TryGetProperty(segments[i], out next))
                {
                    return string.Empty;
                }

                current = next;
            }

            string value;
            return current != null && current.TryGetString(out value)
                ? value
                : string.Empty;
        }

        private static bool LooksLikeRepoRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            return Directory.Exists(Path.Combine(path, "unity", "sim")) &&
                   File.Exists(Path.Combine(path, "config", "local.example.json"));
        }
    }

    internal static class SimulationRunJson
    {
        internal static void AppendBoolProperty(StringBuilder builder, string name, bool value)
        {
            builder.Append('"');
            builder.Append(JsonText.Escape(name));
            builder.Append("\":");
            builder.Append(value ? "true" : "false");
        }

        internal static void AppendIntProperty(StringBuilder builder, string name, int value)
        {
            builder.Append('"');
            builder.Append(JsonText.Escape(name));
            builder.Append("\":");
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        internal static void AppendNullableIntProperty(StringBuilder builder, string name, int? value)
        {
            builder.Append('"');
            builder.Append(JsonText.Escape(name));
            builder.Append("\":");
            builder.Append(value.HasValue
                ? value.Value.ToString(CultureInfo.InvariantCulture)
                : "null");
        }

        internal static void AppendNullableDoubleProperty(StringBuilder builder, string name, double? value)
        {
            builder.Append('"');
            builder.Append(JsonText.Escape(name));
            builder.Append("\":");
            builder.Append(value.HasValue
                ? value.Value.ToString("0.###", CultureInfo.InvariantCulture)
                : "null");
        }
    }
}
