using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GemmaHackathon.SimulationFramework;

namespace GemmaHackathon.SimulationExamples
{
    internal sealed class ExampleScenarioState
    {
        private readonly object _syncRoot = new object();
        private string _phase = "discovery";
        private int _score;
        private string _lastUserInput = string.Empty;
        private string _lastEvent = string.Empty;
        private string _lastDecision = string.Empty;
        private string _lastAssistantResponse = string.Empty;
        private readonly List<SimulationChecklistItem> _checklist = new List<SimulationChecklistItem>();
        private readonly List<SimulationActionRecord> _actions = new List<SimulationActionRecord>();

        public ExampleScenarioState()
        {
            _checklist.Add(new SimulationChecklistItem
            {
                Id = "score",
                Label = "Performance was updated",
                Completed = false,
                Notes = string.Empty
            });
            _checklist.Add(new SimulationChecklistItem
            {
                Id = "phase",
                Label = "Scenario stage moved forward",
                Completed = false,
                Notes = string.Empty
            });
            _checklist.Add(new SimulationChecklistItem
            {
                Id = "decision",
                Label = "Decision was recorded",
                Completed = false,
                Notes = string.Empty
            });
        }

        public string Phase
        {
            get
            {
                lock (_syncRoot)
                {
                    return _phase;
                }
            }
            set
            {
                lock (_syncRoot)
                {
                    _phase = value ?? string.Empty;
                }
            }
        }

        public int Score
        {
            get
            {
                lock (_syncRoot)
                {
                    return _score;
                }
            }
        }

        public string LastUserInput
        {
            get
            {
                lock (_syncRoot)
                {
                    return _lastUserInput;
                }
            }
            set
            {
                lock (_syncRoot)
                {
                    _lastUserInput = value ?? string.Empty;
                }
            }
        }

        public string LastEvent
        {
            get
            {
                lock (_syncRoot)
                {
                    return _lastEvent;
                }
            }
            set
            {
                lock (_syncRoot)
                {
                    _lastEvent = value ?? string.Empty;
                }
            }
        }

        public string LastDecision
        {
            get
            {
                lock (_syncRoot)
                {
                    return _lastDecision;
                }
            }
            set
            {
                lock (_syncRoot)
                {
                    _lastDecision = value ?? string.Empty;
                }
            }
        }

        public string LastAssistantResponse
        {
            get
            {
                lock (_syncRoot)
                {
                    return _lastAssistantResponse;
                }
            }
            set
            {
                lock (_syncRoot)
                {
                    _lastAssistantResponse = value ?? string.Empty;
                }
            }
        }

        public void AddAction(string actor, string verb, string details, float occurredAtSeconds)
        {
            lock (_syncRoot)
            {
                AddActionLocked(actor, verb, details, occurredAtSeconds);
            }
        }

        public void ResetForNewSession()
        {
            lock (_syncRoot)
            {
                _phase = "discovery";
                _score = 0;
                _lastUserInput = string.Empty;
                _lastEvent = string.Empty;
                _lastDecision = string.Empty;
                _lastAssistantResponse = string.Empty;
                _actions.Clear();

                for (var i = 0; i < _checklist.Count; i++)
                {
                    _checklist[i].Completed = false;
                    _checklist[i].Notes = string.Empty;
                }
            }
        }

        public IReadOnlyList<SimulationChecklistItem> GetChecklistSnapshot()
        {
            lock (_syncRoot)
            {
                var result = new List<SimulationChecklistItem>(_checklist.Count);
                for (var i = 0; i < _checklist.Count; i++)
                {
                    result.Add(CloneChecklistItem(_checklist[i]));
                }

                return result;
            }
        }

        public IReadOnlyList<SimulationActionRecord> GetActionsSnapshot()
        {
            lock (_syncRoot)
            {
                var result = new List<SimulationActionRecord>(_actions.Count);
                for (var i = 0; i < _actions.Count; i++)
                {
                    result.Add(CloneAction(_actions[i]));
                }

                return result;
            }
        }

        public SimulationStateSnapshot CreateSnapshot(float elapsedSeconds)
        {
            lock (_syncRoot)
            {
                var snapshot = new SimulationStateSnapshot();
                snapshot.SimulationId = "debug-simulation";
                snapshot.ScenarioId = "conversation-harness";
                snapshot.ElapsedSeconds = elapsedSeconds;

                snapshot.Entries.Add(CreateStringEntry("status", "phase", _phase));
                snapshot.Entries.Add(CreateNumberEntry("score", "value", _score));
                snapshot.Entries.Add(CreateStringEntry("status", "lastUserInput", _lastUserInput));
                snapshot.Entries.Add(CreateStringEntry("status", "lastEvent", _lastEvent));
                snapshot.Entries.Add(CreateStringEntry("status", "lastDecision", _lastDecision));
                snapshot.Entries.Add(CreateStringEntry("status", "lastAssistantResponse", _lastAssistantResponse));

                for (var i = 0; i < _checklist.Count; i++)
                {
                    snapshot.Checklist.Add(CloneChecklistItem(_checklist[i]));
                }

                for (var i = 0; i < _actions.Count; i++)
                {
                    snapshot.RecentActions.Add(CloneAction(_actions[i]));
                }

                return snapshot;
            }
        }

        public void ApplyScoreDelta(int delta, string reason, float occurredAtSeconds)
        {
            lock (_syncRoot)
            {
                _score += delta;
                _checklist[0].Completed = true;
                _checklist[0].Notes = "Delta " + delta.ToString(CultureInfo.InvariantCulture);
                AddActionLocked("tool", "update_score", reason, occurredAtSeconds);
            }
        }

        public void AdvancePhase(string phase, float occurredAtSeconds)
        {
            lock (_syncRoot)
            {
                _phase = phase ?? string.Empty;
                _checklist[1].Completed = true;
                _checklist[1].Notes = _phase;
                AddActionLocked("tool", "advance_phase", _phase, occurredAtSeconds);
            }
        }

        public void RecordDecision(string note, float occurredAtSeconds)
        {
            lock (_syncRoot)
            {
                _lastDecision = note ?? string.Empty;
                _checklist[2].Completed = true;
                _checklist[2].Notes = _lastDecision;
                AddActionLocked("tool", "log_decision", _lastDecision, occurredAtSeconds);
            }
        }

        private static SimulationStateEntry CreateStringEntry(string category, string key, string value)
        {
            return new SimulationStateEntry
            {
                Category = category,
                Key = key,
                ValueJson = "\"" + ExampleScenarioJson.Escape(value ?? string.Empty) + "\""
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

        private void AddActionLocked(string actor, string verb, string details, float occurredAtSeconds)
        {
            _actions.Insert(0, new SimulationActionRecord
            {
                Actor = actor ?? string.Empty,
                Verb = verb ?? string.Empty,
                Details = details ?? string.Empty,
                OccurredAtSeconds = occurredAtSeconds
            });

            while (_actions.Count > 8)
            {
                _actions.RemoveAt(_actions.Count - 1);
            }
        }

        private static SimulationChecklistItem CloneChecklistItem(SimulationChecklistItem item)
        {
            var value = item ?? new SimulationChecklistItem();
            return new SimulationChecklistItem
            {
                Id = value.Id ?? string.Empty,
                Label = value.Label ?? string.Empty,
                Completed = value.Completed,
                Notes = value.Notes ?? string.Empty
            };
        }

        private static SimulationActionRecord CloneAction(SimulationActionRecord action)
        {
            var value = action ?? new SimulationActionRecord();
            return new SimulationActionRecord
            {
                Actor = value.Actor ?? string.Empty,
                Verb = value.Verb ?? string.Empty,
                Details = value.Details ?? string.Empty,
                OccurredAtSeconds = value.OccurredAtSeconds
            };
        }
    }

    internal sealed class ExampleScenarioStateProvider : ISimulationStateProvider, ISimulationKpiProvider
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
            return _state.CreateSnapshot(_clock == null ? 0f : _clock());
        }

        public SimulationKpiSnapshot CaptureKpis()
        {
            var checklist = _state.GetChecklistSnapshot();
            var completedCount = 0;

            for (var i = 0; i < checklist.Count; i++)
            {
                if (checklist[i] != null && checklist[i].Completed)
                {
                    completedCount++;
                }
            }

            var snapshot = new SimulationKpiSnapshot();
            snapshot.Entries.Add(CreateNumberKpi("score", "Score", _state.Score));
            snapshot.Entries.Add(CreateNumberKpi("elapsed_seconds", "Elapsed Seconds", _clock == null ? 0f : _clock()));
            snapshot.Entries.Add(CreateStringKpi("phase", "Phase", _state.Phase));
            snapshot.Entries.Add(CreateNumberKpi("checklist_completed", "Checklist Completed", completedCount));
            snapshot.Entries.Add(CreateNumberKpi("checklist_total", "Checklist Total", checklist.Count));
            return snapshot;
        }

        private static SimulationKpiEntry CreateStringKpi(string key, string label, string value)
        {
            return new SimulationKpiEntry
            {
                Key = key ?? string.Empty,
                Label = label ?? string.Empty,
                ValueJson = "\"" + ExampleScenarioJson.Escape(value ?? string.Empty) + "\""
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

        private static SimulationKpiEntry CreateNumberKpi(string key, string label, float value)
        {
            return new SimulationKpiEntry
            {
                Key = key ?? string.Empty,
                Label = label ?? string.Empty,
                ValueJson = value.ToString("0.###", CultureInfo.InvariantCulture)
            };
        }
    }

    internal sealed class ScriptedScenarioCompletionModel : ISimulationCompletionModel
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

            return ExampleScenarioJson.Unescape(matches[matches.Count - 1].Groups[1].Value);
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

            return ExampleScenarioJson.Unescape(matches[matches.Count - 1].Groups[1].Value);
        }

        private static string BuildResponseCompletion(string response)
        {
            return "{\"success\":true,\"error\":null,\"cloud_handoff\":false,\"response\":\"" +
                   ExampleScenarioJson.Escape(response) +
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
                   ExampleScenarioJson.Escape(name) +
                   "\",\"arguments\":" +
                   (string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson) +
                   "}";
        }
    }

    internal sealed class UpdateScoreTool : ISimulationToolHandler
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
            var delta = ExampleScenarioArgumentReader.ReadInt(
                call == null ? string.Empty : call.ArgumentsJson,
                "delta",
                0);
            var reason = ExampleScenarioArgumentReader.ReadString(
                call == null ? string.Empty : call.ArgumentsJson,
                "reason",
                "score updated");

            _state.ApplyScoreDelta(delta, reason, _clock());

            return new SimulationToolResult
            {
                Name = call == null ? string.Empty : call.Name,
                Content = "Score changed by " + delta.ToString(CultureInfo.InvariantCulture) + ".",
                IsError = false
            };
        }
    }

    internal sealed class AdvancePhaseTool : ISimulationToolHandler
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
            var phase = ExampleScenarioArgumentReader.ReadString(
                call == null ? string.Empty : call.ArgumentsJson,
                "phase",
                _state.Phase);

            _state.AdvancePhase(phase, _clock());

            return new SimulationToolResult
            {
                Name = call == null ? string.Empty : call.Name,
                Content = "Phase changed to " + phase + ".",
                IsError = false
            };
        }
    }

    internal sealed class LogDecisionTool : ISimulationToolHandler
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
            var note = ExampleScenarioArgumentReader.ReadString(
                call == null ? string.Empty : call.ArgumentsJson,
                "note",
                "decision logged");

            _state.RecordDecision(note, _clock());

            return new SimulationToolResult
            {
                Name = call == null ? string.Empty : call.Name,
                Content = note,
                IsError = false
            };
        }
    }

    internal static class ExampleScenarioArgumentReader
    {
        public static int ReadInt(string json, string key, int defaultValue)
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

        public static string ReadString(string json, string key, string defaultValue)
        {
            var match = Regex.Match(
                json ?? string.Empty,
                "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:\\\\.|[^\"])*)\"");

            if (!match.Success)
            {
                return defaultValue;
            }

            return ExampleScenarioJson.Unescape(match.Groups[1].Value);
        }
    }

    internal static class ExampleScenarioJson
    {
        public static string Escape(string value)
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

        public static string Unescape(string value)
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
