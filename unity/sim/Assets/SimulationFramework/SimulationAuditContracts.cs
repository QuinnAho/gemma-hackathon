using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GemmaHackathon.SimulationFramework
{
    [Serializable]
    public sealed class ParticipantAction
    {
        public string ActionCode = string.Empty;
        public string Source = string.Empty;
        public string Target = string.Empty;
        public Dictionary<string, string> Metadata = new Dictionary<string, string>(StringComparer.Ordinal);

        public ParticipantAction Clone()
        {
            var clone = new ParticipantAction
            {
                ActionCode = ActionCode ?? string.Empty,
                Source = Source ?? string.Empty,
                Target = Target ?? string.Empty
            };

            if (Metadata == null)
            {
                return clone;
            }

            foreach (var pair in Metadata)
            {
                clone.Metadata[pair.Key ?? string.Empty] = pair.Value ?? string.Empty;
            }

            return clone;
        }

        public string ToStructuredJson()
        {
            var builder = new StringBuilder(256);
            builder.Append('{');
            JsonText.AppendStringProperty(builder, "action_code", ActionCode);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "source", Source);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "target", Target);
            builder.Append(",\"metadata\":{");

            var wroteAny = false;
            if (Metadata != null)
            {
                foreach (var pair in Metadata)
                {
                    if (wroteAny)
                    {
                        builder.Append(',');
                    }

                    JsonText.AppendStringProperty(builder, pair.Key ?? string.Empty, pair.Value ?? string.Empty);
                    wroteAny = true;
                }
            }

            builder.Append("}}");
            return builder.ToString();
        }
    }

    [Serializable]
    public sealed class AuditSessionRecord
    {
        public string SessionId = string.Empty;
        public string ParticipantAlias = string.Empty;
        public string ScenarioVariant = string.Empty;
        public string RubricVersion = string.Empty;
        public string ScoringVersion = string.Empty;
        public string RuntimeBackend = string.Empty;
        public string SessionPhase = string.Empty;

        public string ToJson()
        {
            var builder = new StringBuilder(256);
            builder.Append('{');
            JsonText.AppendStringProperty(builder, "session_id", SessionId);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "participant_alias", ParticipantAlias);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "scenario_variant", ScenarioVariant);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "rubric_version", RubricVersion);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "scoring_version", ScoringVersion);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "runtime_backend", RuntimeBackend);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "session_phase", SessionPhase);
            builder.Append('}');
            return builder.ToString();
        }
    }

    [Serializable]
    public sealed class AuditEvent
    {
        public string SessionId = string.Empty;
        public string EventId = string.Empty;
        public string CorrelationId = string.Empty;
        public string ScenarioVariant = string.Empty;
        public string RubricVersion = string.Empty;
        public string ScoringVersion = string.Empty;
        public string TimestampUtc = string.Empty;
        public int ElapsedMilliseconds;
        public string Phase = string.Empty;
        public string Source = string.Empty;
        public string ActionCode = string.Empty;
        public string Actor = string.Empty;
        public string Target = string.Empty;
        public string Outcome = string.Empty;
        public string DataJson = "{}";

        public AuditEvent Clone()
        {
            return new AuditEvent
            {
                SessionId = SessionId ?? string.Empty,
                EventId = EventId ?? string.Empty,
                CorrelationId = CorrelationId ?? string.Empty,
                ScenarioVariant = ScenarioVariant ?? string.Empty,
                RubricVersion = RubricVersion ?? string.Empty,
                ScoringVersion = ScoringVersion ?? string.Empty,
                TimestampUtc = TimestampUtc ?? string.Empty,
                ElapsedMilliseconds = ElapsedMilliseconds,
                Phase = Phase ?? string.Empty,
                Source = Source ?? string.Empty,
                ActionCode = ActionCode ?? string.Empty,
                Actor = Actor ?? string.Empty,
                Target = Target ?? string.Empty,
                Outcome = Outcome ?? string.Empty,
                DataJson = string.IsNullOrWhiteSpace(DataJson) ? "{}" : DataJson
            };
        }

        public string ToJson()
        {
            var builder = new StringBuilder(384);
            builder.Append('{');
            JsonText.AppendStringProperty(builder, "session_id", SessionId);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "event_id", EventId);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "correlation_id", CorrelationId);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "scenario_variant", ScenarioVariant);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "rubric_version", RubricVersion);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "scoring_version", ScoringVersion);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "timestamp_utc", TimestampUtc);
            builder.Append(',');
            SimulationRunJson.AppendIntProperty(builder, "elapsed_ms", ElapsedMilliseconds);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "phase", Phase);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "source", Source);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "action_code", ActionCode);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "actor", Actor);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "target", Target);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "outcome", Outcome);
            builder.Append(",\"data\":");
            builder.Append(string.IsNullOrWhiteSpace(DataJson) ? "{}" : DataJson);
            builder.Append('}');
            return builder.ToString();
        }
    }

    public static class AuditEventJson
    {
        public static string CreateObject(params KeyValuePair<string, string>[] stringProperties)
        {
            var builder = new StringBuilder(128);
            builder.Append('{');

            if (stringProperties != null)
            {
                for (var i = 0; i < stringProperties.Length; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(',');
                    }

                    JsonText.AppendStringProperty(
                        builder,
                        stringProperties[i].Key ?? string.Empty,
                        stringProperties[i].Value ?? string.Empty);
                }
            }

            builder.Append('}');
            return builder.ToString();
        }

        public static string CreateNumberObject(params KeyValuePair<string, double>[] numberProperties)
        {
            var builder = new StringBuilder(128);
            builder.Append('{');

            if (numberProperties != null)
            {
                for (var i = 0; i < numberProperties.Length; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(',');
                    }

                    builder.Append('"');
                    builder.Append(JsonText.Escape(numberProperties[i].Key ?? string.Empty));
                    builder.Append("\":");
                    builder.Append(numberProperties[i].Value.ToString("0.###", CultureInfo.InvariantCulture));
                }
            }

            builder.Append('}');
            return builder.ToString();
        }
    }
}
