using System;
using System.Globalization;
using UnityEngine;

namespace GemmaHackathon.SimulationExamples
{
    public sealed partial class SimulationConversationDebugHarness
    {
        private void OnGUI()
        {
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
            var statusColor = _lastTurnResult != null && _lastTurnResult.Success ? Color.green : new Color(1f, 0.45f, 0.3f);
            var previousColor = GUI.color;
            GUI.color = statusColor;
            GUILayout.Box(BuildStatusText(), GUILayout.ExpandWidth(true));
            GUI.color = previousColor;

            GUILayout.Space(8f);
            GUILayout.Label("Example Actions", _headerStyle);

            if (GUILayout.Button("Simulate Good Step"))
            {
                RunUserTurn("Trainee completed the first action.");
            }

            if (GUILayout.Button("Move Scenario Forward"))
            {
                RunUserTurn("Advance the scenario to the next phase.");
            }

            if (GUILayout.Button("Simulate Wrong Step"))
            {
                RunUserTurn("The trainee made a mistake, apply a penalty.");
            }

            if (GUILayout.Button("Ask For Summary"))
            {
                RunUserTurn("Give me a quick status summary.");
            }

            if (GUILayout.Button("Trigger Scenario Change"))
            {
                RunEventTurn("Unexpected simulation event triggered.");
            }

            GUILayout.Space(8f);
            GUILayout.Label("Custom Prompt", _headerStyle);
            _customInput = GUILayout.TextField(_customInput ?? string.Empty);

            if (GUILayout.Button("Send To AI"))
            {
                RunUserTurn(_customInput);
            }

            GUILayout.Space(8f);
            GUILayout.Label("Reset", _headerStyle);

            if (GUILayout.Button("Start Fresh"))
            {
                _manager.ClearHistory();
                _traceEntries.Clear();
                _state.ResetForNewSession();
                _lastTurnResult = new GemmaHackathon.SimulationFramework.SimulationConversationTurnResult();
                _lastTurnResult.Success = true;
                _lastTurnResult.FinalAssistantResponse = "New session started.";
                _state.LastAssistantResponse = _lastTurnResult.FinalAssistantResponse;
                _state.AddAction("system", "reset", "Conversation history cleared.", GetElapsedSeconds());
            }

            GUILayout.EndScrollView();
        }

        private void DrawStatePanel()
        {
            _stateScroll = GUILayout.BeginScrollView(_stateScroll);

            GUILayout.Label("Live Summary", _headerStyle);
            GUILayout.Label("Scenario Stage: " + _state.Phase, _bodyStyle);
            GUILayout.Label("Performance Score: " + _state.Score.ToString(CultureInfo.InvariantCulture), _bodyStyle);
            GUILayout.Label("Time Running: " + GetElapsedSeconds().ToString("0.0", CultureInfo.InvariantCulture) + "s", _bodyStyle);
            GUILayout.Label("Latest Trainee Input: " + SafeValue(_state.LastUserInput), _bodyStyle);
            GUILayout.Label("Latest Scenario Change: " + SafeValue(_state.LastEvent), _bodyStyle);
            GUILayout.Label("Latest System Action: " + SafeValue(_state.LastDecision), _bodyStyle);
            GUILayout.Label("Latest AI Reply: " + SafeValue(_state.LastAssistantResponse), _bodyStyle);

            GUILayout.Space(8f);
            GUILayout.Label("Progress Signals", _headerStyle);
            for (var i = 0; i < _state.Checklist.Count; i++)
            {
                var item = _state.Checklist[i];
                var prefix = item.Completed ? "[x] " : "[ ] ";
                GUILayout.Label(prefix + item.Label + " - " + SafeValue(item.Notes), _bodyStyle);
            }

            GUILayout.Space(8f);
            GUILayout.Label("Recent Activity", _headerStyle);
            for (var i = 0; i < _state.Actions.Count; i++)
            {
                var action = _state.Actions[i];
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
            if (_lastTurnResult != null)
            {
                GUILayout.Label("Completed: " + (_lastTurnResult.Success ? "yes" : "no"), _bodyStyle);
                GUILayout.Label("Final Reply: " + SafeValue(_lastTurnResult.FinalAssistantResponse), _bodyStyle);
                GUILayout.Label("Issue: " + SafeValue(_lastTurnResult.Error), _bodyStyle);

                for (var i = 0; i < _lastTurnResult.ToolResults.Count; i++)
                {
                    var toolResult = _lastTurnResult.ToolResults[i];
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

            for (var i = 0; i < _traceEntries.Count; i++)
            {
                var entry = _traceEntries[i];
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
    }
}
