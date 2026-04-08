using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GemmaHackathon.SimulationFramework;

namespace GemmaHackathon.SimulationScenarios.SvrFire
{
    internal sealed partial class SvrFireScenarioState
    {
        private static ParticipantAction NormalizeParticipantAction(ParticipantAction action)
        {
            var safeAction = action == null ? new ParticipantAction() : action.Clone();
            safeAction.ActionCode = safeAction.ActionCode ?? string.Empty;
            safeAction.Source = string.IsNullOrWhiteSpace(safeAction.Source) ? "ui" : safeAction.Source.Trim();
            safeAction.Target = safeAction.Target ?? string.Empty;
            if (safeAction.Metadata == null)
            {
                safeAction.Metadata = new Dictionary<string, string>(System.StringComparer.Ordinal);
            }

            return safeAction;
        }

        private static AuditSessionRecord CloneSessionRecord(AuditSessionRecord value)
        {
            var source = value ?? new AuditSessionRecord();
            return new AuditSessionRecord
            {
                SessionId = source.SessionId ?? string.Empty,
                ParticipantAlias = source.ParticipantAlias ?? string.Empty,
                ScenarioVariant = source.ScenarioVariant ?? string.Empty,
                RubricVersion = source.RubricVersion ?? string.Empty,
                ScoringVersion = source.ScoringVersion ?? string.Empty,
                RuntimeBackend = source.RuntimeBackend ?? string.Empty,
                SessionPhase = source.SessionPhase ?? string.Empty
            };
        }

        private static string BuildRouteAvailabilityJson(Dictionary<string, bool> routeAvailability)
        {
            var builder = new StringBuilder(64);
            builder.Append('{');

            if (routeAvailability != null)
            {
                var wroteAny = false;
                foreach (var pair in routeAvailability)
                {
                    if (wroteAny)
                    {
                        builder.Append(',');
                    }

                    builder.Append('"');
                    builder.Append(SvrFireJson.Escape(pair.Key ?? string.Empty));
                    builder.Append("\":");
                    builder.Append(pair.Value ? "true" : "false");
                    wroteAny = true;
                }
            }

            builder.Append('}');
            return builder.ToString();
        }

        private static string BuildEnvironmentCueData(
            string routeId,
            bool? available,
            string coworkerState,
            string note)
        {
            var builder = new StringBuilder(160);
            builder.Append('{');
            var wroteAny = false;

            if (!string.IsNullOrWhiteSpace(routeId))
            {
                AppendJsonPair(builder, "route_id", routeId, ref wroteAny);
            }

            if (available.HasValue)
            {
                if (wroteAny)
                {
                    builder.Append(',');
                }

                builder.Append("\"available\":");
                builder.Append(available.Value ? "true" : "false");
                wroteAny = true;
            }

            if (!string.IsNullOrWhiteSpace(coworkerState))
            {
                AppendJsonPair(builder, "coworker_state", coworkerState, ref wroteAny);
            }

            if (!string.IsNullOrWhiteSpace(note))
            {
                AppendJsonPair(builder, "note", note, ref wroteAny);
            }

            builder.Append('}');
            return builder.ToString();
        }

        private static void AppendJsonPair(StringBuilder builder, string key, string value, ref bool wroteAny)
        {
            if (wroteAny)
            {
                builder.Append(',');
            }

            builder.Append('"');
            builder.Append(SvrFireJson.Escape(key ?? string.Empty));
            builder.Append("\":\"");
            builder.Append(SvrFireJson.Escape(value ?? string.Empty));
            builder.Append('"');
            wroteAny = true;
        }

        private static SimulationStateEntry CreateStringEntry(string category, string key, string value)
        {
            return new SimulationStateEntry
            {
                Category = category ?? string.Empty,
                Key = key ?? string.Empty,
                ValueJson = "\"" + SvrFireJson.Escape(value ?? string.Empty) + "\""
            };
        }

        private static SimulationStateEntry CreateBoolEntry(string category, string key, bool value)
        {
            return new SimulationStateEntry
            {
                Category = category ?? string.Empty,
                Key = key ?? string.Empty,
                ValueJson = value ? "true" : "false"
            };
        }

        private static SimulationStateEntry CreateNumberEntry(string category, string key, int value)
        {
            return new SimulationStateEntry
            {
                Category = category ?? string.Empty,
                Key = key ?? string.Empty,
                ValueJson = value.ToString(CultureInfo.InvariantCulture)
            };
        }

        private static SimulationStateEntry CreateNullableFloatEntry(string category, string key, float? value)
        {
            return new SimulationStateEntry
            {
                Category = category ?? string.Empty,
                Key = key ?? string.Empty,
                ValueJson = value.HasValue
                    ? value.Value.ToString("0.###", CultureInfo.InvariantCulture)
                    : "null"
            };
        }

        private static SimulationStateEntry CreateObjectEntry(string category, string key, string valueJson)
        {
            return new SimulationStateEntry
            {
                Category = category ?? string.Empty,
                Key = key ?? string.Empty,
                ValueJson = string.IsNullOrWhiteSpace(valueJson) ? "{}" : valueJson
            };
        }

        private static SimulationKpiEntry CreateNumberKpi(string key, string label, int value)
        {
            return new SimulationKpiEntry
            {
                Key = key ?? string.Empty,
                Label = label ?? string.Empty,
                ValueJson = value.ToString(CultureInfo.InvariantCulture)
            };
        }

        private static SimulationKpiEntry CreateStringKpi(string key, string label, string value)
        {
            return new SimulationKpiEntry
            {
                Key = key ?? string.Empty,
                Label = label ?? string.Empty,
                ValueJson = "\"" + SvrFireJson.Escape(value ?? string.Empty) + "\""
            };
        }

        private static SimulationChecklistItem CloneChecklistItem(SimulationChecklistItem item)
        {
            var safeItem = item ?? new SimulationChecklistItem();
            return new SimulationChecklistItem
            {
                Id = safeItem.Id ?? string.Empty,
                Label = safeItem.Label ?? string.Empty,
                Completed = safeItem.Completed,
                Notes = safeItem.Notes ?? string.Empty
            };
        }

        private static SimulationActionRecord CloneAction(SimulationActionRecord action)
        {
            var safeAction = action ?? new SimulationActionRecord();
            return new SimulationActionRecord
            {
                Actor = safeAction.Actor ?? string.Empty,
                Verb = safeAction.Verb ?? string.Empty,
                Details = safeAction.Details ?? string.Empty,
                OccurredAtSeconds = safeAction.OccurredAtSeconds
            };
        }
    }
}
