using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GemmaHackathon.SimulationFramework;

namespace GemmaHackathon.SimulationExamples
{
    public sealed partial class SimulationConversationDebugManager
    {
        private sealed class ExampleScenarioState
        {
            public string Phase = "discovery";
            public int Score;
            public string LastUserInput = string.Empty;
            public string LastEvent = string.Empty;
            public string LastDecision = string.Empty;
            public string LastAssistantResponse = string.Empty;
            public readonly List<SimulationChecklistItem> Checklist = new List<SimulationChecklistItem>();
            public readonly List<SimulationActionRecord> Actions = new List<SimulationActionRecord>();

            public ExampleScenarioState()
            {
                Checklist.Add(new SimulationChecklistItem
                {
                    Id = "score",
                    Label = "Performance was updated",
                    Completed = false,
                    Notes = string.Empty
                });
                Checklist.Add(new SimulationChecklistItem
                {
                    Id = "phase",
                    Label = "Scenario stage moved forward",
                    Completed = false,
                    Notes = string.Empty
                });
                Checklist.Add(new SimulationChecklistItem
                {
                    Id = "decision",
                    Label = "Decision was recorded",
                    Completed = false,
                    Notes = string.Empty
                });
            }

            public void AddAction(string actor, string verb, string details, float occurredAtSeconds)
            {
                Actions.Insert(0, new SimulationActionRecord
                {
                    Actor = actor ?? string.Empty,
                    Verb = verb ?? string.Empty,
                    Details = details ?? string.Empty,
                    OccurredAtSeconds = occurredAtSeconds
                });

                while (Actions.Count > 8)
                {
                    Actions.RemoveAt(Actions.Count - 1);
                }
            }

            public void ResetForNewSession()
            {
                Phase = "discovery";
                Score = 0;
                LastUserInput = string.Empty;
                LastEvent = string.Empty;
                LastDecision = string.Empty;
                LastAssistantResponse = string.Empty;
                Actions.Clear();

                for (var i = 0; i < Checklist.Count; i++)
                {
                    Checklist[i].Completed = false;
                    Checklist[i].Notes = string.Empty;
                }
            }
        }

        private sealed class ExampleScenarioStateProvider : ISimulationStateProvider
        {
            private readonly ExampleScenarioState _state;
            private readonly Func<float> _clock;

            public ExampleScenarioStateProvider(ExampleScenarioState state, Func<float> clock)
            {
                _state = state;
                _clock = clock;
            }

            public SimulationStateSnapshot CaptureState()
            {
                var snapshot = new SimulationStateSnapshot();
                snapshot.SimulationId = "debug-simulation";
                snapshot.ScenarioId = "conversation-harness";
                snapshot.ElapsedSeconds = _clock == null ? 0f : _clock();

                snapshot.Entries.Add(CreateStringEntry("status", "phase", _state.Phase));
                snapshot.Entries.Add(CreateNumberEntry("score", "value", _state.Score));
                snapshot.Entries.Add(CreateStringEntry("status", "lastUserInput", _state.LastUserInput));
                snapshot.Entries.Add(CreateStringEntry("status", "lastEvent", _state.LastEvent));
                snapshot.Entries.Add(CreateStringEntry("status", "lastDecision", _state.LastDecision));
                snapshot.Entries.Add(CreateStringEntry("status", "lastAssistantResponse", _state.LastAssistantResponse));

                for (var i = 0; i < _state.Checklist.Count; i++)
                {
                    var item = _state.Checklist[i];
                    snapshot.Checklist.Add(new SimulationChecklistItem
                    {
                        Id = item.Id,
                        Label = item.Label,
                        Completed = item.Completed,
                        Notes = item.Notes
                    });
                }

                for (var i = 0; i < _state.Actions.Count; i++)
                {
                    var action = _state.Actions[i];
                    snapshot.RecentActions.Add(new SimulationActionRecord
                    {
                        Actor = action.Actor,
                        Verb = action.Verb,
                        Details = action.Details,
                        OccurredAtSeconds = action.OccurredAtSeconds
                    });
                }

                return snapshot;
            }

            private static SimulationStateEntry CreateStringEntry(string category, string key, string value)
            {
                return new SimulationStateEntry
                {
                    Category = category,
                    Key = key,
                    ValueJson = "\"" + EscapeJson(value ?? string.Empty) + "\""
                };
            }

            private static SimulationStateEntry CreateNumberEntry(string category, string key, int value)
            {
                return new SimulationStateEntry
                {
                    Category = category,
                    Key = key,
                    ValueJson = value.ToString(CultureInfo.InvariantCulture)
                };
            }
        }

        private sealed class ScriptedScenarioCompletionModel : ISimulationCompletionModel
        {
            private readonly ExampleScenarioState _state;

            public ScriptedScenarioCompletionModel(ExampleScenarioState state)
            {
                _state = state;
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
                var lastRole = ExtractLastRole(messagesJson);
                if (string.Equals(lastRole, "tool", StringComparison.Ordinal))
                {
                    return BuildFollowUpCompletion();
                }

                var lastUserInput = ExtractLastUserContent(messagesJson);
                var lowered = (lastUserInput ?? string.Empty).ToLowerInvariant();

                if (lowered.Contains("summary") || lowered.Contains("status"))
                {
                    return BuildResponseCompletion(
                        "Current phase is " +
                        _state.Phase +
                        " with score " +
                        _state.Score.ToString(CultureInfo.InvariantCulture) +
                        ".");
                }

                var functionCalls = new List<string>();

                if (lowered.Contains("advance"))
                {
                    functionCalls.Add(BuildFunctionCall("advance_phase", "{\"phase\":\"intervention\"}"));
                }

                if (lowered.Contains("mistake") || lowered.Contains("penalty"))
                {
                    functionCalls.Add(BuildFunctionCall("update_score", "{\"delta\":-2,\"reason\":\"negative_outcome\"}"));
                }
                else
                {
                    functionCalls.Add(BuildFunctionCall("update_score", "{\"delta\":1,\"reason\":\"positive_outcome\"}"));
                }

                if (lowered.Contains("event"))
                {
                    functionCalls.Add(BuildFunctionCall("log_decision", "{\"note\":\"Processed simulation event.\"}"));
                }
                else
                {
                    functionCalls.Add(BuildFunctionCall("log_decision", "{\"note\":\"Processed trainee input.\"}"));
                }

                return BuildFunctionCallCompletion(functionCalls);
            }

            private string BuildFollowUpCompletion()
            {
                var builder = new StringBuilder();
                builder.Append("Updated state. Phase: ");
                builder.Append(_state.Phase);
                builder.Append(". Score: ");
                builder.Append(_state.Score.ToString(CultureInfo.InvariantCulture));

                if (!string.IsNullOrWhiteSpace(_state.LastDecision))
                {
                    builder.Append(". Decision: ");
                    builder.Append(_state.LastDecision);
                }

                builder.Append(".");
                return BuildResponseCompletion(builder.ToString());
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

                return UnescapeJson(matches[matches.Count - 1].Groups[1].Value);
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

                return UnescapeJson(matches[matches.Count - 1].Groups[1].Value);
            }

            private static string BuildResponseCompletion(string response)
            {
                return "{\"success\":true,\"error\":null,\"cloud_handoff\":false,\"response\":\"" +
                       EscapeJson(response) +
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
                       EscapeJson(name) +
                       "\",\"arguments\":" +
                       (string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson) +
                       "}";
            }
        }

        private sealed class UpdateScoreTool : ISimulationToolHandler
        {
            private readonly ExampleScenarioState _state;
            private readonly Func<float> _clock;

            public UpdateScoreTool(ExampleScenarioState state, Func<float> clock)
            {
                _state = state;
                _clock = clock;
                Definition = new SimulationToolDefinition
                {
                    Name = "update_score",
                    Description = "Adjusts the score for the current simulation.",
                    ParametersJson =
                        "{\"type\":\"object\",\"properties\":{\"delta\":{\"type\":\"integer\"},\"reason\":{\"type\":\"string\"}},\"required\":[\"delta\"]}"
                };
            }

            public SimulationToolDefinition Definition { get; private set; }

            public SimulationToolResult Execute(SimulationToolCall call)
            {
                var delta = ReadIntArgument(call.ArgumentsJson, "delta", 0);
                var reason = ReadStringArgument(call.ArgumentsJson, "reason", "score updated");

                _state.Score += delta;
                _state.Checklist[0].Completed = true;
                _state.Checklist[0].Notes = "Delta " + delta.ToString(CultureInfo.InvariantCulture);
                _state.AddAction("tool", "update_score", reason, _clock());

                return new SimulationToolResult
                {
                    Name = call.Name,
                    Content = "Score changed by " + delta.ToString(CultureInfo.InvariantCulture) + ".",
                    IsError = false
                };
            }
        }

        private sealed class AdvancePhaseTool : ISimulationToolHandler
        {
            private readonly ExampleScenarioState _state;
            private readonly Func<float> _clock;

            public AdvancePhaseTool(ExampleScenarioState state, Func<float> clock)
            {
                _state = state;
                _clock = clock;
                Definition = new SimulationToolDefinition
                {
                    Name = "advance_phase",
                    Description = "Moves the simulation into a new phase.",
                    ParametersJson =
                        "{\"type\":\"object\",\"properties\":{\"phase\":{\"type\":\"string\"}},\"required\":[\"phase\"]}"
                };
            }

            public SimulationToolDefinition Definition { get; private set; }

            public SimulationToolResult Execute(SimulationToolCall call)
            {
                var phase = ReadStringArgument(call.ArgumentsJson, "phase", _state.Phase);

                _state.Phase = phase;
                _state.Checklist[1].Completed = true;
                _state.Checklist[1].Notes = phase;
                _state.AddAction("tool", "advance_phase", phase, _clock());

                return new SimulationToolResult
                {
                    Name = call.Name,
                    Content = "Phase changed to " + phase + ".",
                    IsError = false
                };
            }
        }

        private sealed class LogDecisionTool : ISimulationToolHandler
        {
            private readonly ExampleScenarioState _state;
            private readonly Func<float> _clock;

            public LogDecisionTool(ExampleScenarioState state, Func<float> clock)
            {
                _state = state;
                _clock = clock;
                Definition = new SimulationToolDefinition
                {
                    Name = "log_decision",
                    Description = "Stores a human-readable decision summary.",
                    ParametersJson =
                        "{\"type\":\"object\",\"properties\":{\"note\":{\"type\":\"string\"}},\"required\":[\"note\"]}"
                };
            }

            public SimulationToolDefinition Definition { get; private set; }

            public SimulationToolResult Execute(SimulationToolCall call)
            {
                var note = ReadStringArgument(call.ArgumentsJson, "note", "decision logged");

                _state.LastDecision = note;
                _state.Checklist[2].Completed = true;
                _state.Checklist[2].Notes = note;
                _state.AddAction("tool", "log_decision", note, _clock());

                return new SimulationToolResult
                {
                    Name = call.Name,
                    Content = note,
                    IsError = false
                };
            }
        }

        private static int ReadIntArgument(string json, string key, int defaultValue)
        {
            var match = Regex.Match(
                json ?? string.Empty,
                "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?\\d+)");

            if (!match.Success)
            {
                return defaultValue;
            }

            int parsed;
            return int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : defaultValue;
        }

        private static string ReadStringArgument(string json, string key, string defaultValue)
        {
            var match = Regex.Match(
                json ?? string.Empty,
                "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:\\\\.|[^\"])*)\"");

            if (!match.Success)
            {
                return defaultValue;
            }

            return UnescapeJson(match.Groups[1].Value);
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length + 16);
            for (var i = 0; i < value.Length; i++)
            {
                var character = value[i];
                switch (character)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (character < 32)
                        {
                            builder.Append("\\u");
                            builder.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(character);
                        }

                        break;
                }
            }

            return builder.ToString();
        }

        private static string UnescapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                var character = value[i];
                if (character != '\\' || i + 1 >= value.Length)
                {
                    builder.Append(character);
                    continue;
                }

                i++;
                switch (value[i])
                {
                    case '\\':
                        builder.Append('\\');
                        break;
                    case '"':
                        builder.Append('"');
                        break;
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'u':
                        if (i + 4 < value.Length)
                        {
                            var hex = value.Substring(i + 1, 4);
                            int codePoint;
                            if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out codePoint))
                            {
                                builder.Append((char)codePoint);
                                i += 4;
                            }
                        }

                        break;
                    default:
                        builder.Append(value[i]);
                        break;
                }
            }

            return builder.ToString();
        }
    }
}
