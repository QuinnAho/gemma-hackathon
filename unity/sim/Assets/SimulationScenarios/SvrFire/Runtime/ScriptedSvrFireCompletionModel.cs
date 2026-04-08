using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GemmaHackathon.SimulationFramework;

namespace GemmaHackathon.SimulationScenarios.SvrFire
{
    internal sealed class ScriptedSvrFireCompletionModel : ISimulationCompletionModel
    {
        private readonly SvrFireScenarioStateProvider _stateProvider;

        public ScriptedSvrFireCompletionModel(SvrFireScenarioStateProvider stateProvider)
        {
            _stateProvider = stateProvider;
        }

        public void Reset()
        {
        }

        public string CompleteJson(
            string messagesJson,
            string optionsJson = null,
            string toolsJson = null,
            Action<string, uint> tokenCallback = null,
            byte[] pcm16Mono = null,
            int responseBufferBytes = 0)
        {
            var status = _stateProvider == null
                ? new SvrFireScenarioStatusSnapshot()
                : _stateProvider.CaptureStatus();
            var lastRole = ExtractLastRole(messagesJson);
            if (string.Equals(lastRole, "tool", StringComparison.Ordinal))
            {
                return BuildResponseCompletion(BuildFollowUpSummary(status));
            }

            var lastMessage = ExtractLastUserContent(messagesJson).ToLowerInvariant();
            if (lastMessage.Contains("summary") || lastMessage.Contains("status"))
            {
                return BuildResponseCompletion(BuildFollowUpSummary(status));
            }

            var functionCalls = new List<string>();
            if (string.Equals(status.Phase, SvrFireScenarioValues.PhaseNormal, StringComparison.Ordinal))
            {
                functionCalls.Add(BuildFunctionCall(
                    "escalate_hazard",
                    "{\"hazard_state\":\"alarm_and_smoke_exit_a\",\"announcement\":\"Fire alarm active. Exit A has smoke. Exit B is clear.\"}"));
                functionCalls.Add(BuildFunctionCall(
                    "prompt_participant",
                    "{\"message\":\"Fire alarm active. Exit B is clear. Evacuate now.\"}"));
                return BuildFunctionCallCompletion(functionCalls);
            }

            if (lastMessage.Contains("acknowledge") || lastMessage.Contains("alarm"))
            {
                functionCalls.Add(BuildFunctionCall(
                    "prompt_participant",
                    "{\"message\":\"Good. Move to the clear exit and leave the area.\"}"));
            }
            else if (lastMessage.Contains("exit a"))
            {
                functionCalls.Add(BuildFunctionCall(
                    "annotate_context",
                    "{\"note\":\"Participant moved toward the hazardous route.\"}"));
                functionCalls.Add(BuildFunctionCall(
                    "request_end_scenario",
                    "{\"reason\":\"Participant chose a hazardous exit route.\"}"));
            }
            else if (lastMessage.Contains("exit b"))
            {
                functionCalls.Add(BuildFunctionCall(
                    "annotate_context",
                    "{\"note\":\"Participant moved toward the clear route and reached safety.\"}"));
                functionCalls.Add(BuildFunctionCall(
                    "request_end_scenario",
                    "{\"reason\":\"Participant reached the safe zone.\"}"));
            }
            else if (lastMessage.Contains("coworker"))
            {
                functionCalls.Add(BuildFunctionCall(
                    "prompt_participant",
                    "{\"message\":\"Assist quickly if it is safe, then continue to the clear exit.\"}"));
            }
            else
            {
                functionCalls.Add(BuildFunctionCall(
                    "prompt_participant",
                    "{\"message\":\"Acknowledge the alarm and move to the clear exit.\"}"));
            }

            return BuildFunctionCallCompletion(functionCalls);
        }

        private static string BuildFollowUpSummary(SvrFireScenarioStatusSnapshot status)
        {
            var safeStatus = status ?? new SvrFireScenarioStatusSnapshot();
            var score = safeStatus.ReadinessScore ?? new SvrFireReadinessScore();
            return "Phase " +
                   (safeStatus.Phase ?? string.Empty) +
                   ". Location " +
                   (safeStatus.ParticipantLocation ?? string.Empty) +
                   ". Readiness " +
                   score.TotalPoints.ToString(CultureInfo.InvariantCulture) +
                   "/" +
                   score.MaxPoints.ToString(CultureInfo.InvariantCulture) +
                   " (" +
                   (score.Band ?? string.Empty) +
                   ").";
        }

        private static string ExtractLastRole(string messagesJson)
        {
            var matches = Regex.Matches(
                messagesJson ?? string.Empty,
                "\"role\":\"((?:\\\\.|[^\"])*)\"");

            if (matches.Count == 0)
            {
                return string.Empty;
            }

            return SvrFireJson.Unescape(matches[matches.Count - 1].Groups[1].Value);
        }

        private static string ExtractLastUserContent(string messagesJson)
        {
            var matches = Regex.Matches(
                messagesJson ?? string.Empty,
                "\"role\":\"user\",\"content\":\"((?:\\\\.|[^\"])*)\"");

            if (matches.Count == 0)
            {
                return string.Empty;
            }

            return SvrFireJson.Unescape(matches[matches.Count - 1].Groups[1].Value);
        }

        private static string BuildResponseCompletion(string response)
        {
            return "{\"success\":true,\"error\":null,\"cloud_handoff\":false,\"response\":\"" +
                   SvrFireJson.Escape(response) +
                   "\",\"function_calls\":[],\"segments\":[],\"confidence\":0.97}";
        }

        private static string BuildFunctionCallCompletion(List<string> calls)
        {
            var builder = new StringBuilder();
            builder.Append("{\"success\":true,\"error\":null,\"cloud_handoff\":false,\"response\":\"\",\"function_calls\":[");

            for (var i = 0; i < calls.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(calls[i]);
            }

            builder.Append("],\"segments\":[],\"confidence\":0.92}");
            return builder.ToString();
        }

        private static string BuildFunctionCall(string name, string argumentsJson)
        {
            return "{\"name\":\"" +
                   SvrFireJson.Escape(name) +
                   "\",\"arguments\":" +
                   (string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson) +
                   "}";
        }
    }
}
