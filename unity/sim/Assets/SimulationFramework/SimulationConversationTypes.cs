using System;
using System.Collections.Generic;
using System.Globalization;

namespace GemmaHackathon.SimulationFramework
{
    public enum SimulationConversationTraceKind
    {
        TurnInput,
        StateSnapshot,
        RequestMessagesJson,
        CompletionJson,
        FunctionCall,
        ToolResult,
        AssistantResponse,
        Error
    }

    [Serializable]
    public sealed class SimulationConversationTraceEntry
    {
        public SimulationConversationTraceKind Kind;
        public string Content = string.Empty;
        public string TimestampUtc = string.Empty;

        public static SimulationConversationTraceEntry Create(SimulationConversationTraceKind kind, string content)
        {
            return new SimulationConversationTraceEntry
            {
                Kind = kind,
                Content = content ?? string.Empty,
                TimestampUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            };
        }
    }

    public sealed class SimulationConversationManagerOptions
    {
        public const string DefaultCompletionOptionsJson =
            "{\"temperature\":0.0,\"auto_handoff\":false,\"enable_thinking_if_supported\":false}";

        public string SystemPrompt =
            "You are a simulation-aware assistant. Use the current simulation state JSON as the source of truth. " +
            "When tools are available, call them only when necessary and use exact JSON arguments that match the provided schemas.";

        public string CompletionOptionsJson = DefaultCompletionOptionsJson;
        public string FollowUpCompletionOptionsJson = DefaultCompletionOptionsJson;
        public string EventMessagePrefix = "Simulation event:\n";
        public int ResponseBufferBytes = CactusNative.LargeJsonBufferSize;
        public int MaxToolRoundTrips = 2;
        public bool ResetModelBeforeEachCompletion = true;
        public Action<SimulationConversationTraceEntry> TraceSink;
        public ISimulationToolExecutor ToolExecutor;
        public ISimulationRunLogger RunLogger;
    }

    [Serializable]
    public sealed class CactusCompletionResponse
    {
        public bool ParseSucceeded;
        public bool Success;
        public bool CloudHandoff;
        public string Error = string.Empty;
        public string Response = string.Empty;
        public string Thinking = string.Empty;
        public string RawJson = string.Empty;
        public List<SimulationToolCall> FunctionCalls = new List<SimulationToolCall>();
        public double? Confidence;
        public double? TimeToFirstTokenMs;
        public double? TotalTimeMs;
        public double? PrefillTokensPerSecond;
        public double? DecodeTokensPerSecond;
        public double? RamUsageMb;
        public int? PrefillTokens;
        public int? DecodeTokens;
        public int? TotalTokens;

        public static CactusCompletionResponse Parse(string rawJson)
        {
            var result = new CactusCompletionResponse();
            result.RawJson = rawJson ?? string.Empty;

            if (string.IsNullOrWhiteSpace(rawJson))
            {
                result.ParseSucceeded = false;
                result.Success = false;
                result.Error = "Completion JSON was empty.";
                return result;
            }

            try
            {
                var root = JsonDom.Parse(rawJson);
                if (root.Kind != JsonValueKind.Object)
                {
                    throw new FormatException("Completion payload root must be a JSON object.");
                }

                result.ParseSucceeded = true;
                result.Success = ReadBoolean(root, "success", false);
                result.CloudHandoff = ReadBoolean(root, "cloud_handoff", false);
                result.Error = ReadNullableString(root, "error");
                result.Response = ReadNullableString(root, "response");
                result.Thinking = ReadNullableString(root, "thinking");
                result.Confidence = ReadNullableDouble(root, "confidence");
                result.TimeToFirstTokenMs = ReadNullableDouble(root, "time_to_first_token_ms");
                result.TotalTimeMs = ReadNullableDouble(root, "total_time_ms");
                result.PrefillTokensPerSecond = ReadNullableDouble(root, "prefill_tps");
                result.DecodeTokensPerSecond = ReadNullableDouble(root, "decode_tps");
                result.RamUsageMb = ReadNullableDouble(root, "ram_usage_mb");
                result.PrefillTokens = ReadNullableInt32(root, "prefill_tokens");
                result.DecodeTokens = ReadNullableInt32(root, "decode_tokens");
                result.TotalTokens = ReadNullableInt32(root, "total_tokens");

                JsonValue functionCallsValue;
                if (root.TryGetProperty("function_calls", out functionCallsValue) &&
                    functionCallsValue != null &&
                    functionCallsValue.Kind == JsonValueKind.Array &&
                    functionCallsValue.ArrayValue != null)
                {
                    for (var i = 0; i < functionCallsValue.ArrayValue.Count; i++)
                    {
                        var item = functionCallsValue.ArrayValue[i];
                        if (item == null || item.Kind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        var call = new SimulationToolCall();
                        call.Name = ReadNullableString(item, "name");

                        JsonValue argumentsValue;
                        if (item.TryGetProperty("arguments", out argumentsValue) && argumentsValue != null)
                        {
                            call.ArgumentsJson = argumentsValue.ToJson();
                        }
                        else
                        {
                            call.ArgumentsJson = "{}";
                        }

                        result.FunctionCalls.Add(call);
                    }
                }
            }
            catch (Exception ex)
            {
                result.ParseSucceeded = false;
                result.Success = false;
                result.Error = "Failed to parse completion JSON: " + ex.Message;
            }

            return result;
        }

        private static bool ReadBoolean(JsonValue root, string propertyName, bool defaultValue)
        {
            JsonValue value;
            bool parsed;
            if (root.TryGetProperty(propertyName, out value) &&
                value != null &&
                value.TryGetBoolean(out parsed))
            {
                return parsed;
            }

            return defaultValue;
        }

        private static string ReadNullableString(JsonValue root, string propertyName)
        {
            JsonValue value;
            string parsed;
            if (root.TryGetProperty(propertyName, out value) &&
                value != null &&
                value.TryGetString(out parsed))
            {
                return parsed;
            }

            return string.Empty;
        }

        private static double? ReadNullableDouble(JsonValue root, string propertyName)
        {
            JsonValue value;
            double parsed;
            if (root.TryGetProperty(propertyName, out value) &&
                value != null &&
                value.TryGetDouble(out parsed))
            {
                return parsed;
            }

            return null;
        }

        private static int? ReadNullableInt32(JsonValue root, string propertyName)
        {
            JsonValue value;
            int parsed;
            if (root.TryGetProperty(propertyName, out value) &&
                value != null &&
                value.TryGetInt32(out parsed))
            {
                return parsed;
            }

            return null;
        }
    }

    [Serializable]
    public sealed class SimulationConversationTurnResult
    {
        public string TurnId = string.Empty;
        public bool Success;
        public bool AppliedToHistory;
        public bool CloudHandoffRequested;
        public bool ReachedToolRoundTripLimit;
        public string Error = string.Empty;
        public string FinalAssistantResponse = string.Empty;
        public ConversationMessage InputMessage = new ConversationMessage();
        public List<SimulationStateSnapshot> StateSnapshots = new List<SimulationStateSnapshot>();
        public List<CactusCompletionResponse> CompletionResponses = new List<CactusCompletionResponse>();
        public List<SimulationToolResult> ToolResults = new List<SimulationToolResult>();
        public List<string> AssistantResponses = new List<string>();
        public List<SimulationConversationTraceEntry> TraceEntries = new List<SimulationConversationTraceEntry>();
    }
}
