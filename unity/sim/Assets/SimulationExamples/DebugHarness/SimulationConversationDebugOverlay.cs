using System;
using System.Globalization;
using UnityEngine;

namespace GemmaHackathon.SimulationExamples
{
    [AddComponentMenu("Gemma Hackathon/Debug Harness/Simulation Conversation Debug Overlay")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SimulationConversationDebugManager))]
    public sealed class SimulationConversationDebugOverlay : MonoBehaviour
    {
        private const float Padding = 16f;
        private const float HeaderHeight = 28f;

        private SimulationConversationDebugManager _manager;
        private Vector2 _controlsScroll;
        private Vector2 _stateScroll;
        private Vector2 _historyScroll;
        private Vector2 _traceScroll;
        private GUIStyle _headerStyle;
        private GUIStyle _bodyStyle;

        private void Awake()
        {
            _manager = GetComponent<SimulationConversationDebugManager>();
        }

        private void OnGUI()
        {
            if (_manager == null)
            {
                _manager = GetComponent<SimulationConversationDebugManager>();
            }

            if (_manager == null || !_manager.OverlayVisible)
            {
                return;
            }

            EnsureStyles();

            var contentWidth = Screen.width - (Padding * 4f);
            var columnWidth = contentWidth / 3f;
            var contentHeight = Screen.height - (Padding * 2f);

            DrawPanel(
                new Rect(Padding, Padding, columnWidth, contentHeight),
                "Try The Flow",
                DrawControls);

            DrawPanel(
                new Rect(Padding * 2f + columnWidth, Padding, columnWidth, contentHeight),
                "Current Situation",
                DrawStatePanel);

            DrawPanel(
                new Rect(Padding * 3f + (columnWidth * 2f), Padding, columnWidth, contentHeight),
                "What Happened",
                DrawHistoryPanel);
        }

        private void DrawPanel(Rect rect, string title, Action body)
        {
            GUI.Box(rect, GUIContent.none);

            var titleRect = new Rect(rect.x + 12f, rect.y + 8f, rect.width - 24f, HeaderHeight);
            GUI.Label(titleRect, title, _headerStyle);

            var contentRect = new Rect(
                rect.x + 12f,
                rect.y + HeaderHeight + 16f,
                rect.width - 24f,
                rect.height - HeaderHeight - 28f);

            GUILayout.BeginArea(contentRect);
            body();
            GUILayout.EndArea();
        }

        private void DrawControls()
        {
            _controlsScroll = GUILayout.BeginScrollView(_controlsScroll);

            GUILayout.Label("Overview", _headerStyle);
            GUILayout.Label(
                "This screen shows the product loop at a high level: an input comes in, the AI reviews the current situation, the system updates, and a final response is shown.",
                _bodyStyle);

            GUILayout.Space(8f);
            GUILayout.Label("Current Status", _headerStyle);
            var lastTurnResult = _manager.LastTurnResult;
            var statusColor = lastTurnResult != null && lastTurnResult.Success
                ? Color.green
                : new Color(1f, 0.45f, 0.3f);
            var previousColor = GUI.color;
            GUI.color = statusColor;
            GUILayout.Box(_manager.StatusText, GUILayout.ExpandWidth(true));
            GUI.color = previousColor;

            GUILayout.Space(8f);
            GUILayout.Label("Example Actions", _headerStyle);

            if (GUILayout.Button("Simulate Good Step"))
            {
                _manager.RunUserTurn("Trainee completed the first action.");
            }

            if (GUILayout.Button("Move Scenario Forward"))
            {
                _manager.RunUserTurn("Advance the scenario to the next phase.");
            }

            if (GUILayout.Button("Simulate Wrong Step"))
            {
                _manager.RunUserTurn("The trainee made a mistake, apply a penalty.");
            }

            if (GUILayout.Button("Ask For Summary"))
            {
                _manager.RunUserTurn("Give me a quick status summary.");
            }

            if (GUILayout.Button("Trigger Scenario Change"))
            {
                _manager.RunEventTurn("Unexpected simulation event triggered.");
            }

            GUILayout.Space(8f);
            GUILayout.Label("Custom Prompt", _headerStyle);
            _manager.CustomInput = GUILayout.TextField(_manager.CustomInput ?? string.Empty);

            if (GUILayout.Button("Send To AI"))
            {
                _manager.RunUserTurn(_manager.CustomInput);
            }

            GUILayout.Space(8f);
            GUILayout.Label("Reset", _headerStyle);

            if (GUILayout.Button("Start Fresh"))
            {
                _manager.ResetSession();
            }

            GUILayout.EndScrollView();
        }

        private void DrawStatePanel()
        {
            _stateScroll = GUILayout.BeginScrollView(_stateScroll);

            GUILayout.Label("Live Summary", _headerStyle);
            GUILayout.Label("Scenario Stage: " + _manager.CurrentPhase, _bodyStyle);
            GUILayout.Label("Performance Score: " + _manager.CurrentScore.ToString(CultureInfo.InvariantCulture), _bodyStyle);
            GUILayout.Label("Time Running: " + _manager.ElapsedSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s", _bodyStyle);
            GUILayout.Label("Latest Trainee Input: " + SafeValue(_manager.LastUserInput), _bodyStyle);
            GUILayout.Label("Latest Scenario Change: " + SafeValue(_manager.LastEvent), _bodyStyle);
            GUILayout.Label("Latest System Action: " + SafeValue(_manager.LastDecision), _bodyStyle);
            GUILayout.Label("Latest AI Reply: " + SafeValue(_manager.LastAssistantResponse), _bodyStyle);

            GUILayout.Space(8f);
            GUILayout.Label("Progress Signals", _headerStyle);
            for (var i = 0; i < _manager.Checklist.Count; i++)
            {
                var item = _manager.Checklist[i];
                var prefix = item.Completed ? "[x] " : "[ ] ";
                GUILayout.Label(prefix + item.Label + " - " + SafeValue(item.Notes), _bodyStyle);
            }

            GUILayout.Space(8f);
            GUILayout.Label("Recent Activity", _headerStyle);
            for (var i = 0; i < _manager.Actions.Count; i++)
            {
                var action = _manager.Actions[i];
                GUILayout.Label(
                    action.OccurredAtSeconds.ToString("0.0", CultureInfo.InvariantCulture) +
                    "s | " +
                    DescribeActor(action.Actor) +
                    " | " +
                    DescribeAction(action.Verb) +
                    " | " +
                    SafeValue(action.Details),
                    _bodyStyle);
            }

            GUILayout.Space(8f);
            GUILayout.Label("Latest Outcome", _headerStyle);
            if (_manager.LastTurnResult != null)
            {
                GUILayout.Label("Completed: " + (_manager.LastTurnResult.Success ? "yes" : "no"), _bodyStyle);
                GUILayout.Label("Final Reply: " + SafeValue(_manager.LastTurnResult.FinalAssistantResponse), _bodyStyle);
                GUILayout.Label("Issue: " + SafeValue(_manager.LastTurnResult.Error), _bodyStyle);

                for (var i = 0; i < _manager.LastTurnResult.ToolResults.Count; i++)
                {
                    var toolResult = _manager.LastTurnResult.ToolResults[i];
                    GUILayout.Label(
                        "System Action " +
                        toolResult.Name +
                        ": " +
                        toolResult.Content +
                        (toolResult.IsError ? " (error)" : string.Empty),
                        _bodyStyle);
                }
            }

            GUILayout.EndScrollView();
        }

        private void DrawHistoryPanel()
        {
            GUILayout.Label("Conversation So Far", _headerStyle);
            _historyScroll = GUILayout.BeginScrollView(_historyScroll, GUILayout.Height(Screen.height * 0.36f));

            var history = _manager.History;
            for (var i = 0; i < history.Count; i++)
            {
                var message = history[i];
                GUILayout.Label(DescribeMessageRole(message.Role) + ": " + SafeValue(message.Content), _bodyStyle);
            }

            GUILayout.EndScrollView();

            GUILayout.Space(8f);
            GUILayout.Label("Behind The Scenes", _headerStyle);
            _traceScroll = GUILayout.BeginScrollView(_traceScroll);

            for (var i = 0; i < _manager.TraceEntries.Count; i++)
            {
                var entry = _manager.TraceEntries[i];
                GUILayout.Label(
                    entry.TimestampUtc + " | " + DescribeTraceKind(entry.Kind) + " | " + SafeValue(entry.Content),
                    _bodyStyle);
            }

            GUILayout.EndScrollView();
        }

        private static string DescribeActor(string actor)
        {
            switch (actor)
            {
                case "user":
                    return "Trainee";
                case "assistant":
                    return "AI";
                case "tool":
                    return "System";
                case "system":
                    return "Platform";
                default:
                    return SafeValue(actor);
            }
        }

        private static string DescribeAction(string verb)
        {
            switch (verb)
            {
                case "initialize":
                    return "started";
                case "reset":
                    return "reset";
                case "update_score":
                    return "updated score";
                case "advance_phase":
                    return "moved scenario forward";
                case "log_decision":
                    return "recorded decision";
                case "error":
                    return "reported issue";
                default:
                    return SafeValue(verb);
            }
        }

        private static string DescribeMessageRole(string role)
        {
            switch (role)
            {
                case "user":
                    return "Trainee";
                case "assistant":
                    return "AI";
                case "system":
                    return "System Context";
                case "tool":
                    return "System Update";
                default:
                    return SafeValue(role);
            }
        }

        private static string DescribeTraceKind(GemmaHackathon.SimulationFramework.SimulationConversationTraceKind kind)
        {
            switch (kind)
            {
                case GemmaHackathon.SimulationFramework.SimulationConversationTraceKind.TurnInput:
                    return "Input Received";
                case GemmaHackathon.SimulationFramework.SimulationConversationTraceKind.StateSnapshot:
                    return "Situation Captured";
                case GemmaHackathon.SimulationFramework.SimulationConversationTraceKind.RequestMessagesJson:
                    return "Prompt Prepared";
                case GemmaHackathon.SimulationFramework.SimulationConversationTraceKind.CompletionJson:
                    return "Model Returned";
                case GemmaHackathon.SimulationFramework.SimulationConversationTraceKind.FunctionCall:
                    return "System Action Requested";
                case GemmaHackathon.SimulationFramework.SimulationConversationTraceKind.ToolResult:
                    return "System Updated";
                case GemmaHackathon.SimulationFramework.SimulationConversationTraceKind.AssistantResponse:
                    return "Final Reply";
                case GemmaHackathon.SimulationFramework.SimulationConversationTraceKind.Error:
                    return "Issue";
                default:
                    return kind.ToString();
            }
        }

        private void EnsureStyles()
        {
            if (_headerStyle != null)
            {
                return;
            }

            _headerStyle = new GUIStyle(GUI.skin.label);
            _headerStyle.fontStyle = FontStyle.Bold;
            _headerStyle.fontSize = 13;
            _headerStyle.wordWrap = true;

            _bodyStyle = new GUIStyle(GUI.skin.label);
            _bodyStyle.wordWrap = true;
            _bodyStyle.fontSize = 11;
        }

        private static string SafeValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(none)" : value;
        }
    }
}
