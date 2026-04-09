using System;
using System.Collections.Generic;
using GemmaHackathon.SimulationFramework;

namespace GemmaHackathon.SimulationScenarios.SvrFire
{
    internal sealed class SvrFireDeficitTemplate
    {
        public string Id = string.Empty;
        public string MetricId = string.Empty;
        public string Severity = string.Empty;
        public string Summary = string.Empty;
        public string DefaultDetails = string.Empty;
        public string Recommendation = string.Empty;
    }

    public static class SvrFireDeficitCatalog
    {
        public const string AlarmAcknowledgementMissingId = "alarm_ack_missing";
        public const string AlarmAcknowledgementDelayedId = "alarm_ack_delayed";
        public const string EvacuationMissingId = "evacuation_missing";
        public const string EvacuationDelayedId = "evacuation_delayed";
        public const string SafeRouteMissingId = "safe_route_missing";
        public const string SafetyNotReachedId = "safety_not_reached";

        private static readonly Dictionary<string, SvrFireDeficitTemplate> Templates = CreateTemplates();

        public static DeficitRecord CreateRecord(string id, string detailsOverride = null)
        {
            var safeId = string.IsNullOrWhiteSpace(id) ? string.Empty : id.Trim();

            SvrFireDeficitTemplate template;
            if (!TryGetTemplate(safeId, out template))
            {
                return new DeficitRecord
                {
                    Id = safeId,
                    MetricId = string.Empty,
                    Severity = "info",
                    Summary = string.IsNullOrWhiteSpace(safeId)
                        ? "A deterministic assessment deficit was recorded."
                        : "Deterministic deficit `" + safeId + "` was recorded.",
                    Details = string.IsNullOrWhiteSpace(detailsOverride) ? safeId : detailsOverride.Trim()
                };
            }

            return new DeficitRecord
            {
                Id = template.Id,
                MetricId = template.MetricId,
                Severity = template.Severity,
                Summary = template.Summary,
                Details = string.IsNullOrWhiteSpace(detailsOverride)
                    ? template.DefaultDetails
                    : detailsOverride.Trim()
            };
        }

        public static string BuildRecommendation(DeficitRecord deficit)
        {
            var safeDeficit = deficit ?? new DeficitRecord();

            SvrFireDeficitTemplate template;
            if (TryGetTemplate(safeDeficit.Id, out template) &&
                !string.IsNullOrWhiteSpace(template.Recommendation))
            {
                return template.Recommendation;
            }

            return string.IsNullOrWhiteSpace(safeDeficit.Summary)
                ? string.Empty
                : "Address the recorded deficit: " + safeDeficit.Summary.Trim();
        }

        private static bool TryGetTemplate(string id, out SvrFireDeficitTemplate template)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                template = null;
                return false;
            }

            return Templates.TryGetValue(id.Trim(), out template);
        }

        private static Dictionary<string, SvrFireDeficitTemplate> CreateTemplates()
        {
            return new Dictionary<string, SvrFireDeficitTemplate>(StringComparer.Ordinal)
            {
                {
                    AlarmAcknowledgementMissingId,
                    new SvrFireDeficitTemplate
                    {
                        Id = AlarmAcknowledgementMissingId,
                        MetricId = "alarm_recognition",
                        Severity = "high",
                        Summary = "Alarm acknowledgement was not recorded.",
                        DefaultDetails = "The participant never acknowledged the alarm during the assessment.",
                        Recommendation = "Acknowledge the alarm immediately so the evacuation protocol starts from a recorded alarm response."
                    }
                },
                {
                    AlarmAcknowledgementDelayedId,
                    new SvrFireDeficitTemplate
                    {
                        Id = AlarmAcknowledgementDelayedId,
                        MetricId = "alarm_recognition",
                        Severity = "medium",
                        Summary = "Alarm acknowledgement was delayed.",
                        DefaultDetails = "The participant acknowledged the alarm after the fastest scoring band.",
                        Recommendation = "Acknowledge the alarm immediately so the evacuation protocol starts from a recorded alarm response."
                    }
                },
                {
                    EvacuationMissingId,
                    new SvrFireDeficitTemplate
                    {
                        Id = EvacuationMissingId,
                        MetricId = "evacuation_start",
                        Severity = "high",
                        Summary = "Evacuation never started.",
                        DefaultDetails = "The participant did not begin moving toward an exit.",
                        Recommendation = "Begin moving toward a validated exit sooner once the alarm state is active."
                    }
                },
                {
                    EvacuationDelayedId,
                    new SvrFireDeficitTemplate
                    {
                        Id = EvacuationDelayedId,
                        MetricId = "evacuation_start",
                        Severity = "medium",
                        Summary = "Evacuation start was delayed.",
                        DefaultDetails = "The participant started evacuation after the fastest scoring band.",
                        Recommendation = "Begin moving toward a validated exit sooner once the alarm state is active."
                    }
                },
                {
                    SafeRouteMissingId,
                    new SvrFireDeficitTemplate
                    {
                        Id = SafeRouteMissingId,
                        MetricId = "route_correctness",
                        Severity = "high",
                        Summary = "A safe exit route was not confirmed.",
                        DefaultDetails = "The participant did not end the drill on a validated safe route.",
                        Recommendation = "Confirm which exits remain safe before committing to a route, especially after smoke or blockage cues appear."
                    }
                },
                {
                    SafetyNotReachedId,
                    new SvrFireDeficitTemplate
                    {
                        Id = SafetyNotReachedId,
                        MetricId = "protocol_completion",
                        Severity = "high",
                        Summary = "The participant did not reach the safe zone.",
                        DefaultDetails = "The drill ended before the participant reached the safe zone.",
                        Recommendation = "Continue the evacuation until the safe zone is actually reached and recorded by the scenario state."
                    }
                },
                {
                    SvrFireScenarioValues.CriticalIgnoredAlarm,
                    new SvrFireDeficitTemplate
                    {
                        Id = SvrFireScenarioValues.CriticalIgnoredAlarm,
                        MetricId = "alarm_recognition",
                        Severity = "critical",
                        Summary = "Alarm acknowledgement exceeded the failure window.",
                        DefaultDetails = "The participant failed to acknowledge the alarm within 60 seconds.",
                        Recommendation = "Acknowledge the alarm immediately so the evacuation protocol starts from a recorded alarm response."
                    }
                },
                {
                    SvrFireScenarioValues.CriticalWrongExit,
                    new SvrFireDeficitTemplate
                    {
                        Id = SvrFireScenarioValues.CriticalWrongExit,
                        MetricId = "route_correctness",
                        Severity = "critical",
                        Summary = "The participant selected a hazardous exit route.",
                        DefaultDetails = "The evacuation path ended in a hazardous zone.",
                        Recommendation = "Confirm which exits remain safe before committing to a route, especially after smoke or blockage cues appear."
                    }
                }
            };
        }
    }
}
