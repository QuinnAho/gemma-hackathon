using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GemmaHackathon.SimulationFramework
{
    public interface ISimulationStateProvider
    {
        SimulationStateSnapshot CaptureState();
    }

    [Serializable]
    public sealed class SimulationStateEntry
    {
        public string Category = string.Empty;
        public string Key = string.Empty;
        public string ValueJson = "null";
    }

    [Serializable]
    public sealed class SimulationChecklistItem
    {
        public string Id = string.Empty;
        public string Label = string.Empty;
        public bool Completed;
        public string Notes = string.Empty;
    }

    [Serializable]
    public sealed class SimulationActionRecord
    {
        public string Actor = string.Empty;
        public string Verb = string.Empty;
        public string Details = string.Empty;
        public float OccurredAtSeconds;
    }

    [Serializable]
    public sealed class SimulationStateSnapshot
    {
        public string SimulationId = string.Empty;
        public string ScenarioId = string.Empty;
        public float ElapsedSeconds;
        public List<SimulationStateEntry> Entries = new List<SimulationStateEntry>();
        public List<SimulationChecklistItem> Checklist = new List<SimulationChecklistItem>();
        public List<SimulationActionRecord> RecentActions = new List<SimulationActionRecord>();

        public string ToStructuredJson()
        {
            var builder = new StringBuilder(1024);
            builder.Append('{');
            JsonText.AppendStringProperty(builder, "simulationId", SimulationId);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "scenarioId", ScenarioId);
            builder.Append(",\"elapsedSeconds\":");
            builder.Append(ElapsedSeconds.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"entries\":[");

            for (var i = 0; i < Entries.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                var entry = Entries[i] ?? new SimulationStateEntry();
                builder.Append('{');
                JsonText.AppendStringProperty(builder, "category", entry.Category);
                builder.Append(',');
                JsonText.AppendStringProperty(builder, "key", entry.Key);
                builder.Append(",\"value\":");
                builder.Append(string.IsNullOrWhiteSpace(entry.ValueJson) ? "null" : entry.ValueJson);
                builder.Append('}');
            }

            builder.Append("],\"checklist\":[");
            for (var i = 0; i < Checklist.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                var item = Checklist[i] ?? new SimulationChecklistItem();
                builder.Append('{');
                JsonText.AppendStringProperty(builder, "id", item.Id);
                builder.Append(',');
                JsonText.AppendStringProperty(builder, "label", item.Label);
                builder.Append(",\"completed\":");
                builder.Append(item.Completed ? "true" : "false");
                builder.Append(',');
                JsonText.AppendStringProperty(builder, "notes", item.Notes);
                builder.Append('}');
            }

            builder.Append("],\"recentActions\":[");
            for (var i = 0; i < RecentActions.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                var action = RecentActions[i] ?? new SimulationActionRecord();
                builder.Append('{');
                JsonText.AppendStringProperty(builder, "actor", action.Actor);
                builder.Append(',');
                JsonText.AppendStringProperty(builder, "verb", action.Verb);
                builder.Append(',');
                JsonText.AppendStringProperty(builder, "details", action.Details);
                builder.Append(",\"occurredAtSeconds\":");
                builder.Append(action.OccurredAtSeconds.ToString(CultureInfo.InvariantCulture));
                builder.Append('}');
            }

            builder.Append("]}");
            return builder.ToString();
        }

        public string ToContextBlock()
        {
            var builder = new StringBuilder();
            builder.AppendLine("Simulation state JSON:");
            builder.AppendLine(ToStructuredJson());
            return builder.ToString();
        }
    }

    [Serializable]
    public sealed class ConversationMessage
    {
        public string Role = string.Empty;
        public string Content = string.Empty;
        public string[] Images = Array.Empty<string>();
        public string[] Audio = Array.Empty<string>();
    }

    public static class ConversationMessageJson
    {
        public static string Serialize(IReadOnlyList<ConversationMessage> messages)
        {
            if (messages == null || messages.Count == 0)
            {
                return "[]";
            }

            var builder = new StringBuilder(1024);
            builder.Append('[');

            for (var i = 0; i < messages.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                AppendMessage(builder, messages[i] ?? new ConversationMessage());
            }

            builder.Append(']');
            return builder.ToString();
        }

        private static void AppendMessage(StringBuilder builder, ConversationMessage message)
        {
            builder.Append('{');
            JsonText.AppendStringProperty(builder, "role", message.Role);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "content", message.Content);

            if (message.Images != null && message.Images.Length > 0)
            {
                builder.Append(",\"images\":");
                JsonText.AppendStringArray(builder, message.Images);
            }

            if (message.Audio != null && message.Audio.Length > 0)
            {
                builder.Append(",\"audio\":");
                JsonText.AppendStringArray(builder, message.Audio);
            }

            builder.Append('}');
        }
    }

    [Serializable]
    public sealed class SimulationToolDefinition
    {
        public string Name = string.Empty;
        public string Description = string.Empty;
        public string ParametersJson = "{\"type\":\"object\",\"properties\":{},\"required\":[]}";

        public string ToToolJson()
        {
            var builder = new StringBuilder(256);
            builder.Append("{\"type\":\"function\",\"function\":{");
            JsonText.AppendStringProperty(builder, "name", Name);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "description", Description);
            builder.Append(",\"parameters\":");
            builder.Append(string.IsNullOrWhiteSpace(ParametersJson)
                ? "{\"type\":\"object\",\"properties\":{},\"required\":[]}"
                : ParametersJson);
            builder.Append("}}");
            return builder.ToString();
        }
    }

    [Serializable]
    public sealed class SimulationToolCall
    {
        public string Name = string.Empty;
        public string ArgumentsJson = "{}";
    }

    [Serializable]
    public sealed class SimulationToolResult
    {
        public string Name = string.Empty;
        public string Content = string.Empty;
        public bool IsError;

        public ConversationMessage ToConversationMessage()
        {
            return new ConversationMessage
            {
                Role = "tool",
                Content = ToToolMessageContentJson()
            };
        }

        public string ToToolMessageContentJson()
        {
            var builder = new StringBuilder(128);
            builder.Append('{');
            JsonText.AppendStringProperty(builder, "name", Name);
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "content", Content);
            builder.Append(",\"is_error\":");
            builder.Append(IsError ? "true" : "false");
            builder.Append('}');
            return builder.ToString();
        }

        public static SimulationToolResult CreateError(string name, string content)
        {
            return new SimulationToolResult
            {
                Name = name ?? string.Empty,
                Content = content ?? string.Empty,
                IsError = true
            };
        }
    }

    public interface ISimulationToolHandler
    {
        SimulationToolDefinition Definition { get; }
        SimulationToolResult Execute(SimulationToolCall call);
    }

    public sealed class SimulationToolRegistry
    {
        private readonly Dictionary<string, ISimulationToolHandler> _handlers =
            new Dictionary<string, ISimulationToolHandler>(StringComparer.Ordinal);

        private readonly List<ISimulationToolHandler> _orderedHandlers = new List<ISimulationToolHandler>();

        public void Register(ISimulationToolHandler handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException("handler");
            }

            var definition = handler.Definition;
            if (definition == null)
            {
                throw new ArgumentException("Handler must provide a tool definition.", "handler");
            }

            if (string.IsNullOrWhiteSpace(definition.Name))
            {
                throw new ArgumentException("Tool definition must include a name.", "handler");
            }

            if (_handlers.ContainsKey(definition.Name))
            {
                throw new InvalidOperationException("A tool named `" + definition.Name + "` is already registered.");
            }

            _handlers.Add(definition.Name, handler);
            _orderedHandlers.Add(handler);
        }

        public bool TryExecute(SimulationToolCall call, out SimulationToolResult result)
        {
            if (call == null || string.IsNullOrWhiteSpace(call.Name))
            {
                result = SimulationToolResult.CreateError(string.Empty, "Tool call is missing a tool name.");
                return false;
            }

            ISimulationToolHandler handler;
            if (!_handlers.TryGetValue(call.Name, out handler))
            {
                result = SimulationToolResult.CreateError(call.Name, "Tool is not registered.");
                return false;
            }

            try
            {
                result = handler.Execute(call) ?? SimulationToolResult.CreateError(call.Name, "Tool returned no result.");
            }
            catch (Exception ex)
            {
                result = SimulationToolResult.CreateError(call.Name, ex.Message);
            }

            return !result.IsError;
        }

        public string BuildToolsJson()
        {
            var builder = new StringBuilder(512);
            builder.Append('[');

            for (var i = 0; i < _orderedHandlers.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(_orderedHandlers[i].Definition.ToToolJson());
            }

            builder.Append(']');
            return builder.ToString();
        }
    }

    internal static class JsonText
    {
        internal static void AppendStringProperty(StringBuilder builder, string name, string value)
        {
            builder.Append('"');
            builder.Append(Escape(name));
            builder.Append("\":\"");
            builder.Append(Escape(value ?? string.Empty));
            builder.Append('"');
        }

        internal static void AppendStringArray(StringBuilder builder, string[] values)
        {
            builder.Append('[');

            if (values != null)
            {
                for (var i = 0; i < values.Length; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(',');
                    }

                    builder.Append('"');
                    builder.Append(Escape(values[i] ?? string.Empty));
                    builder.Append('"');
                }
            }

            builder.Append(']');
        }

        internal static string Escape(string value)
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
    }
}
