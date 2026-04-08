using System;
using System.Collections.Generic;

namespace GemmaHackathon.SimulationFramework
{
    [Serializable]
    public sealed class AssessmentInput
    {
        public string SessionId = string.Empty;
        public string ScenarioId = string.Empty;
        public string ScenarioVariantId = string.Empty;
        public string SessionState = string.Empty;
        public string Phase = string.Empty;
        public string RubricVersion = string.Empty;
        public string ScoringVersion = string.Empty;
        public double ElapsedSeconds;
        public Dictionary<string, string> TextFacts = new Dictionary<string, string>(StringComparer.Ordinal);
        public Dictionary<string, bool> BooleanFacts = new Dictionary<string, bool>(StringComparer.Ordinal);
        public Dictionary<string, double> NumericFacts = new Dictionary<string, double>(StringComparer.Ordinal);
        public List<string> ActionCodes = new List<string>();
        public List<string> CriticalFailureCodes = new List<string>();

        public void SetTextFact(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            TextFacts[key] = value ?? string.Empty;
        }

        public void SetBooleanFact(string key, bool value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            BooleanFacts[key] = value;
        }

        public void SetNumericFact(string key, double value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            NumericFacts[key] = value;
        }

        public string GetTextFact(string key, string defaultValue = "")
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return defaultValue ?? string.Empty;
            }

            string value;
            return TextFacts.TryGetValue(key, out value) ? (value ?? string.Empty) : (defaultValue ?? string.Empty);
        }

        public bool GetBooleanFact(string key, bool defaultValue = false)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return defaultValue;
            }

            bool value;
            return BooleanFacts.TryGetValue(key, out value) ? value : defaultValue;
        }

        public double? GetNumericFact(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            double value;
            return NumericFacts.TryGetValue(key, out value) ? value : (double?)null;
        }

        public bool HasCriticalFailure(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return false;
            }

            for (var i = 0; i < CriticalFailureCodes.Count; i++)
            {
                if (string.Equals(CriticalFailureCodes[i], code, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }

    [Serializable]
    public sealed class AssessmentTimingBand
    {
        public double MaxSeconds = double.MaxValue;
        public int Points;
    }

    [Serializable]
    public sealed class AssessmentTimingMetricPolicy
    {
        public string MetricId = string.Empty;
        public string MeasurementKey = string.Empty;
        public int MaxPoints;
        public List<AssessmentTimingBand> Bands = new List<AssessmentTimingBand>();
    }

    [Serializable]
    public sealed class ScoringPolicy
    {
        public string PolicyId = string.Empty;
        public string ScenarioId = string.Empty;
        public string RubricVersion = string.Empty;
        public string ScoringVersion = string.Empty;
        public int MaxPoints = 100;
        public int PassThreshold = 70;
        public int MarginalThreshold = 50;
        public int ProtocolCompletionPoints = 25;
        public int CriticalErrorPenaltyPerItem = 10;
        public List<string> CriticalFailureCodes = new List<string>();
        public Dictionary<string, int> FixedMetricPoints = new Dictionary<string, int>(StringComparer.Ordinal);
        public List<AssessmentTimingMetricPolicy> TimingMetrics = new List<AssessmentTimingMetricPolicy>();
    }

    [Serializable]
    public sealed class DeficitRecord
    {
        public string Id = string.Empty;
        public string MetricId = string.Empty;
        public string Severity = string.Empty;
        public string Summary = string.Empty;
        public string Details = string.Empty;
    }

    [Serializable]
    public sealed class AssessmentResult
    {
        public string PolicyId = string.Empty;
        public string SessionId = string.Empty;
        public string ScenarioId = string.Empty;
        public string ScenarioVariantId = string.Empty;
        public string RubricVersion = string.Empty;
        public string ScoringVersion = string.Empty;
        public int TotalPoints;
        public int MaxPoints;
        public string Band = string.Empty;
        public Dictionary<string, int> MetricScores = new Dictionary<string, int>(StringComparer.Ordinal);
        public List<string> CriticalFailures = new List<string>();
        public List<DeficitRecord> Deficits = new List<DeficitRecord>();
    }

    [Serializable]
    public sealed class AssessmentArtifacts
    {
        public AssessmentInput Input = new AssessmentInput();
        public AssessmentResult Result = new AssessmentResult();
        public AssessmentReport Report = new AssessmentReport();
    }

    [Serializable]
    public sealed class AssessmentNarrative
    {
        public bool Success;
        public bool UsedFallback;
        public string Provider = string.Empty;
        public string ModelReference = string.Empty;
        public string PromptVersion = string.Empty;
        public string GeneratedAtUtc = string.Empty;
        public string Summary = string.Empty;
        public string ReportAddendum = string.Empty;
        public List<string> Recommendations = new List<string>();
        public string GroundingNote = string.Empty;
        public string Error = string.Empty;
        public string RawResponse = string.Empty;
    }

    [Serializable]
    public sealed class AssessmentReport
    {
        public string SessionId = string.Empty;
        public string ScenarioId = string.Empty;
        public string ScenarioVariantId = string.Empty;
        public string PolicyId = string.Empty;
        public string RubricVersion = string.Empty;
        public string ScoringVersion = string.Empty;
        public string GeneratedAtUtc = string.Empty;
        public int TotalPoints;
        public int MaxPoints;
        public string Band = string.Empty;
        public string Summary = string.Empty;
        public List<AssessmentReportSection> Sections = new List<AssessmentReportSection>();
    }

    [Serializable]
    public sealed class AssessmentReportSection
    {
        public string Id = string.Empty;
        public string Title = string.Empty;
        public List<string> Entries = new List<string>();
    }
}
