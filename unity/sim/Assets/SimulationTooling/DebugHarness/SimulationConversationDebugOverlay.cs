using System;
using System.Globalization;
using GemmaHackathon.SimulationFramework;
using GemmaHackathon.SimulationScenarios.SvrFire;
using UnityEngine;

namespace GemmaHackathon.SimulationTooling.DebugHarness
{
    [AddComponentMenu("Gemma Hackathon/Debug Harness/Simulation Conversation Debug Overlay")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SimulationConversationDebugManager))]
    public sealed class SimulationConversationDebugOverlay : MonoBehaviour
    {
        private const float Padding = 12f;
        private const float HeaderHeight = 24f;
        private const float SectionSpacing = 12f;
        private const float ItemSpacing = 2f;

        private SimulationConversationDebugManager _manager;
        private SimulationConversationDiagnosticsSnapshot _snapshot;
        private Vector2 _controlsScroll;
        private Vector2 _runtimeScroll;
        private Vector2 _activityScroll;
        private GUIStyle _headerStyle;
        private GUIStyle _sectionStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _monoStyle;
        private GUIStyle _dimStyle;
        private GUIStyle _errorStyle;

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
            _snapshot = _manager.CaptureDiagnosticsSnapshot();

            var contentWidth = Screen.width - (Padding * 4f);
            var columnWidth = contentWidth / 3f;
            var contentHeight = Screen.height - (Padding * 2f);

            DrawPanel(
                new Rect(Padding, Padding, columnWidth, contentHeight),
                "Controls",
                DrawControlsPanel);

            DrawPanel(
                new Rect(Padding * 2f + columnWidth, Padding, columnWidth, contentHeight),
                "State",
                DrawRuntimePanel);

            DrawPanel(
                new Rect(Padding * 3f + (columnWidth * 2f), Padding, columnWidth, contentHeight),
                "Activity",
                DrawActivityPanel);
        }

        private void DrawPanel(Rect rect, string title, Action body)
        {
            GUI.Box(rect, GUIContent.none);

            var titleRect = new Rect(rect.x + 10f, rect.y + 6f, rect.width - 20f, HeaderHeight);
            GUI.Label(titleRect, title, _headerStyle);

            var contentRect = new Rect(
                rect.x + 10f,
                rect.y + HeaderHeight + 10f,
                rect.width - 20f,
                rect.height - HeaderHeight - 20f);

            GUILayout.BeginArea(contentRect);
            body();
            GUILayout.EndArea();
        }

        private void DrawControlsPanel()
        {
            _controlsScroll = GUILayout.BeginScrollView(_controlsScroll);
            var isBusy = _snapshot.TurnState == SimulationTurnLifecycleState.Running ||
                         _snapshot.TurnState == SimulationTurnLifecycleState.Cancelling;
            var isSessionReady = string.Equals(_snapshot.SessionState, SvrFireScenarioValues.SessionStateReady, StringComparison.Ordinal);
            var isSessionRunning = string.Equals(_snapshot.SessionState, SvrFireScenarioValues.SessionStateRunning, StringComparison.Ordinal);
            var isSessionComplete = string.Equals(_snapshot.SessionState, SvrFireScenarioValues.SessionStateComplete, StringComparison.Ordinal);
            var runtimeReady = _snapshot.RuntimeState == SimulationRuntimeLifecycleState.Ready ||
                               _snapshot.RuntimeState == SimulationRuntimeLifecycleState.Fallback;

            DrawInlineStatus();

            GUILayout.Space(SectionSpacing);
            DrawSectionHeader("Session");
            var previousEnabledState = GUI.enabled;
            GUI.enabled = !isBusy && runtimeReady && isSessionReady;
            if (GUILayout.Button("Start Sim"))
            {
                _manager.StartSimulation();
            }

            GUILayout.Space(SectionSpacing);
            DrawSectionHeader("Assessment");
            GUI.enabled = !isBusy && isSessionRunning;

            if (GUILayout.Button("Trigger Alarm"))
            {
                _manager.RunAlarmEscalation();
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Ack Alarm"))
            {
                _manager.RunParticipantAction(SvrFireScenarioValues.ActionAcknowledgeAlarm);
            }
            if (GUILayout.Button("Help Coworker"))
            {
                _manager.RunParticipantAction(SvrFireScenarioValues.ActionHelpCoworker);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Exit A"))
            {
                _manager.RunParticipantAction(SvrFireScenarioValues.ActionMoveExitA);
            }
            if (GUILayout.Button("Exit B"))
            {
                _manager.RunParticipantAction(SvrFireScenarioValues.ActionMoveExitB);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(SectionSpacing);
            DrawSectionHeader("Post-Run AI");
            GUI.enabled = !isBusy && isSessionComplete;
            _manager.CustomInput = GUILayout.TextField(_manager.CustomInput ?? string.Empty);
            if (GUILayout.Button("Send"))
            {
                _manager.RunUserTurn(_manager.CustomInput);
            }
            GUI.enabled = previousEnabledState;
            if (!isSessionComplete)
            {
                GUILayout.Label("AI summary/report turns unlock after the deterministic run completes.", _dimStyle);
            }

            GUILayout.Space(SectionSpacing);
            DrawSectionHeader("Recovery");
            GUILayout.BeginHorizontal();
            GUI.enabled = _snapshot.CanAbandonTurn;
            if (GUILayout.Button("Abandon Turn"))
            {
                _manager.AbandonActiveTurn();
            }
            GUI.enabled = !isBusy;
            if (GUILayout.Button("Reset"))
            {
                _manager.ResetSession();
            }
            GUILayout.EndHorizontal();
            GUI.enabled = previousEnabledState;

            GUILayout.EndScrollView();
        }

        private void DrawInlineStatus()
        {
            var isBusy = _snapshot.TurnState == SimulationTurnLifecycleState.Running ||
                         _snapshot.TurnState == SimulationTurnLifecycleState.Cancelling;

            GUILayout.BeginHorizontal();
            DrawStateBadge(DescribeRuntimeState(_snapshot.RuntimeState), GetRuntimeStateColor(_snapshot.RuntimeState));
            DrawStateBadge(DescribeSessionState(_snapshot.SessionState), GetSessionStateColor(_snapshot.SessionState));
            if (isBusy)
            {
                DrawStateBadge(DescribeTurnState(_snapshot.TurnState), GetTurnStateColor(_snapshot.TurnState));
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label(FormatTime(_snapshot.SessionElapsedSeconds), _dimStyle);
            GUILayout.EndHorizontal();

            if (isBusy && !string.IsNullOrWhiteSpace(_snapshot.PendingTurnDescription))
            {
                GUILayout.Label(
                    _snapshot.PendingTurnElapsedSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s",
                    _dimStyle);
            }
            else if (!string.IsNullOrWhiteSpace(_snapshot.LastTurnError))
            {
                GUILayout.Label(TruncateText(_snapshot.LastTurnError, 60), _errorStyle);
            }
        }

        private void DrawRuntimePanel()
        {
            _runtimeScroll = GUILayout.BeginScrollView(_runtimeScroll);

            DrawSectionHeader("Backend");
            GUILayout.BeginHorizontal();
            DrawStateBadge(DescribeBackendHealth(_snapshot.BackendHealth), GetBackendHealthColor(_snapshot.BackendHealth));
            if (!string.IsNullOrWhiteSpace(_snapshot.ActiveBackendName) && _snapshot.ActiveBackendName != "Not initialized")
            {
                GUILayout.Label(_snapshot.ActiveBackendName, _bodyStyle);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (_snapshot.RuntimeCapabilities.UsesLiveModel)
            {
                DrawCompactCapabilities();
            }

            if (!string.IsNullOrWhiteSpace(_snapshot.BackendHealthSummary) &&
                _snapshot.BackendHealth != SimulationBackendHealthState.Healthy)
            {
                GUILayout.Label(TruncateText(_snapshot.BackendHealthSummary, 80), _dimStyle);
            }

            GUILayout.Space(SectionSpacing);
            DrawSectionHeader("Scenario");
            DrawCompactRow("Session", DescribeSessionState(_snapshot.SessionState));
            if (!string.IsNullOrWhiteSpace(_snapshot.CurrentPhase))
            {
                DrawCompactRow("Phase", _snapshot.CurrentPhase);
            }
            DrawCompactRow("Score", _snapshot.CurrentScore + " (" + SafeValueOrDash(_snapshot.CurrentReadinessBand) + ")");
            if (!string.IsNullOrWhiteSpace(_snapshot.HazardState))
            {
                DrawCompactRow("Hazard", _snapshot.HazardState);
            }
            if (!string.IsNullOrWhiteSpace(_snapshot.ParticipantLocation))
            {
                DrawCompactRow("Location", _snapshot.ParticipantLocation);
            }
            if (!string.IsNullOrWhiteSpace(_snapshot.CoworkerState))
            {
                DrawCompactRow("Coworker", _snapshot.CoworkerState);
            }

            GUILayout.Space(SectionSpacing);
            DrawSectionHeader("Checklist");
            if (_snapshot.Checklist.Length == 0)
            {
                GUILayout.Label("No items", _dimStyle);
            }
            else
            {
                for (var i = 0; i < _snapshot.Checklist.Length; i++)
                {
                    var item = _snapshot.Checklist[i] ?? new SimulationChecklistItem();
                    var checkMark = item.Completed ? "[x]" : "[ ]";
                    var label = string.IsNullOrWhiteSpace(item.Label) ? "Item " + i : item.Label;
                    GUILayout.Label(checkMark + " " + label, item.Completed ? _dimStyle : _bodyStyle);
                }
            }

            if (_snapshot.KpiSnapshot != null && _snapshot.KpiSnapshot.HasEntries)
            {
                GUILayout.Space(SectionSpacing);
                DrawSectionHeader("KPIs");
                for (var i = 0; i < _snapshot.KpiSnapshot.Entries.Count; i++)
                {
                    var entry = _snapshot.KpiSnapshot.Entries[i] ?? new SimulationKpiEntry();
                    if (!string.IsNullOrWhiteSpace(entry.Label))
                    {
                        DrawCompactRow(entry.Label, NormalizeJsonValue(entry.ValueJson));
                    }
                }
            }

            if (_snapshot.LastTurn != null && !string.IsNullOrWhiteSpace(_snapshot.LastTurn.FinalAssistantResponse))
            {
                GUILayout.Space(SectionSpacing);
                DrawSectionHeader("Last Response");
                GUILayout.Label(TruncateText(_snapshot.LastTurn.FinalAssistantResponse, 200), _bodyStyle);
                if (_snapshot.LastTurn.ToolResults.Length > 0)
                {
                    GUILayout.Space(ItemSpacing);
                    for (var i = 0; i < _snapshot.LastTurn.ToolResults.Length; i++)
                    {
                        var toolResult = _snapshot.LastTurn.ToolResults[i] ?? new SimulationToolResult();
                        var toolStyle = toolResult.IsError ? _errorStyle : _monoStyle;
                        GUILayout.Label(toolResult.Name + (toolResult.IsError ? " (err)" : ""), toolStyle);
                    }
                }
            }

            GUILayout.EndScrollView();
        }

        private void DrawCompactCapabilities()
        {
            var caps = new System.Text.StringBuilder();
            if (_snapshot.RuntimeCapabilities.SupportsTextCompletion) caps.Append("txt ");
            if (_snapshot.RuntimeCapabilities.SupportsToolCalling) caps.Append("tools ");
            if (_snapshot.RuntimeCapabilities.SupportsSpeechTranscription) caps.Append("stt ");
            if (_snapshot.RuntimeCapabilities.IsTargetRuntime) caps.Append("target");
            if (caps.Length > 0)
            {
                GUILayout.Label(caps.ToString().Trim(), _dimStyle);
            }
        }

        private void DrawCompactRow(string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "-")
            {
                return;
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ":", _dimStyle, GUILayout.Width(70));
            GUILayout.Label(value, _bodyStyle);
            GUILayout.EndHorizontal();
        }

        private void DrawActivityPanel()
        {
            _activityScroll = GUILayout.BeginScrollView(_activityScroll);

            DrawSectionHeader("Actions");
            if (_snapshot.Actions.Length == 0)
            {
                GUILayout.Label("No actions yet", _dimStyle);
            }
            else
            {
                var actionsToShow = Math.Min(_snapshot.Actions.Length, 12);
                var startIndex = _snapshot.Actions.Length - actionsToShow;
                for (var i = startIndex; i < _snapshot.Actions.Length; i++)
                {
                    var action = _snapshot.Actions[i] ?? new SimulationActionRecord();
                    var timeStr = action.OccurredAtSeconds.ToString("0.0", CultureInfo.InvariantCulture);
                    var actorStr = DescribeActorShort(action.Actor);
                    var actionStr = DescribeAction(action.Verb);
                    GUILayout.Label(timeStr + "s " + actorStr + " " + actionStr, _monoStyle);
                }
            }

            GUILayout.Space(SectionSpacing);
            DrawSectionHeader("Conversation");
            if (_snapshot.History.Length == 0)
            {
                GUILayout.Label("No messages yet", _dimStyle);
            }
            else
            {
                var historyToShow = Math.Min(_snapshot.History.Length, 8);
                var startIndex = _snapshot.History.Length - historyToShow;
                for (var i = startIndex; i < _snapshot.History.Length; i++)
                {
                    var message = _snapshot.History[i] ?? new ConversationMessage();
                    var rolePrefix = DescribeMessageRoleShort(message.Role);
                    var content = TruncateText(message.Content ?? string.Empty, 100);
                    GUILayout.Label(rolePrefix + " " + content, _bodyStyle);
                }
            }

            if (_snapshot.TraceEntries.Length > 0)
            {
                GUILayout.Space(SectionSpacing);
                DrawSectionHeader("Trace");
                var traceToShow = Math.Min(_snapshot.TraceEntries.Length, 6);
                var startIndex = _snapshot.TraceEntries.Length - traceToShow;
                for (var i = startIndex; i < _snapshot.TraceEntries.Length; i++)
                {
                    var entry = _snapshot.TraceEntries[i] ?? new SimulationConversationTraceEntry();
                    var kindStr = DescribeTraceKindShort(entry.Kind);
                    var content = TruncateText(entry.Content ?? string.Empty, 60);
                    GUILayout.Label(kindStr + " " + content, _monoStyle);
                }
            }

            GUILayout.EndScrollView();
        }

        private void DrawSectionHeader(string title)
        {
            GUILayout.Label(title, _sectionStyle);
        }

        private void DrawStateBadge(string value, Color color)
        {
            var previousColor = GUI.color;
            GUI.color = color;
            GUILayout.Box(value, GUILayout.ExpandWidth(false));
            GUI.color = previousColor;
        }

        private static string FormatTime(float seconds)
        {
            if (seconds < 60f)
            {
                return seconds.ToString("0", CultureInfo.InvariantCulture) + "s";
            }
            var minutes = (int)(seconds / 60f);
            var remainingSeconds = (int)(seconds % 60f);
            return minutes + ":" + remainingSeconds.ToString("00");
        }

        private static string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }
            text = text.Replace("\n", " ").Replace("\r", " ");
            if (text.Length <= maxLength)
            {
                return text;
            }
            return text.Substring(0, maxLength - 3) + "...";
        }

        private static string SafeValueOrDash(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }

        private static string DescribeRuntimeState(SimulationRuntimeLifecycleState state)
        {
            switch (state)
            {
                case SimulationRuntimeLifecycleState.Loading:
                    return "Loading";
                case SimulationRuntimeLifecycleState.Ready:
                    return "Ready";
                case SimulationRuntimeLifecycleState.Fallback:
                    return "Fallback";
                case SimulationRuntimeLifecycleState.Error:
                    return "Error";
                default:
                    return "Uninitialized";
            }
        }

        private static string DescribeBackendHealth(SimulationBackendHealthState state)
        {
            switch (state)
            {
                case SimulationBackendHealthState.Healthy:
                    return "Healthy";
                case SimulationBackendHealthState.Degraded:
                    return "Degraded";
                case SimulationBackendHealthState.Recovering:
                    return "Recovering";
                case SimulationBackendHealthState.Error:
                    return "Error";
                default:
                    return "Unknown";
            }
        }

        private static string DescribeLoggerHealth(SimulationLoggerHealthState state)
        {
            switch (state)
            {
                case SimulationLoggerHealthState.Healthy:
                    return "Healthy";
                case SimulationLoggerHealthState.Degraded:
                    return "Degraded";
                case SimulationLoggerHealthState.Error:
                    return "Error";
                default:
                    return "Uninitialized";
            }
        }

        private static string DescribeTurnState(SimulationTurnLifecycleState state)
        {
            switch (state)
            {
                case SimulationTurnLifecycleState.Cancelling:
                    return "Cancelling";
                case SimulationTurnLifecycleState.Cancelled:
                    return "Cancelled";
                case SimulationTurnLifecycleState.Running:
                    return "Running";
                case SimulationTurnLifecycleState.Succeeded:
                    return "Succeeded";
                case SimulationTurnLifecycleState.Failed:
                    return "Failed";
                default:
                    return "Idle";
            }
        }

        private static string DescribeSessionState(string state)
        {
            if (string.Equals(state, SvrFireScenarioValues.SessionStateRunning, StringComparison.Ordinal))
            {
                return "Running";
            }

            if (string.Equals(state, SvrFireScenarioValues.SessionStateComplete, StringComparison.Ordinal))
            {
                return "Complete";
            }

            return "Ready";
        }

        private static Color GetRuntimeStateColor(SimulationRuntimeLifecycleState state)
        {
            switch (state)
            {
                case SimulationRuntimeLifecycleState.Ready:
                    return new Color(0.35f, 0.7f, 0.35f);
                case SimulationRuntimeLifecycleState.Fallback:
                    return new Color(0.9f, 0.7f, 0.25f);
                case SimulationRuntimeLifecycleState.Error:
                    return new Color(0.95f, 0.35f, 0.3f);
                case SimulationRuntimeLifecycleState.Loading:
                    return new Color(0.8f, 0.7f, 0.25f);
                default:
                    return new Color(0.7f, 0.7f, 0.7f);
            }
        }

        private static Color GetSessionStateColor(string state)
        {
            if (string.Equals(state, SvrFireScenarioValues.SessionStateRunning, StringComparison.Ordinal))
            {
                return new Color(0.35f, 0.7f, 0.35f);
            }

            if (string.Equals(state, SvrFireScenarioValues.SessionStateComplete, StringComparison.Ordinal))
            {
                return new Color(0.25f, 0.55f, 0.85f);
            }

            return new Color(0.7f, 0.7f, 0.7f);
        }

        private static Color GetBackendHealthColor(SimulationBackendHealthState state)
        {
            switch (state)
            {
                case SimulationBackendHealthState.Healthy:
                    return new Color(0.35f, 0.7f, 0.35f);
                case SimulationBackendHealthState.Degraded:
                    return new Color(0.9f, 0.7f, 0.25f);
                case SimulationBackendHealthState.Recovering:
                    return new Color(0.95f, 0.6f, 0.2f);
                case SimulationBackendHealthState.Error:
                    return new Color(0.95f, 0.35f, 0.3f);
                default:
                    return new Color(0.7f, 0.7f, 0.7f);
            }
        }

        private static Color GetLoggerHealthColor(SimulationLoggerHealthState state)
        {
            switch (state)
            {
                case SimulationLoggerHealthState.Healthy:
                    return new Color(0.35f, 0.7f, 0.35f);
                case SimulationLoggerHealthState.Degraded:
                    return new Color(0.9f, 0.7f, 0.25f);
                case SimulationLoggerHealthState.Error:
                    return new Color(0.95f, 0.35f, 0.3f);
                default:
                    return new Color(0.7f, 0.7f, 0.7f);
            }
        }

        private static Color GetTurnStateColor(SimulationTurnLifecycleState state)
        {
            switch (state)
            {
                case SimulationTurnLifecycleState.Cancelled:
                    return new Color(0.85f, 0.55f, 0.2f);
                case SimulationTurnLifecycleState.Cancelling:
                    return new Color(0.95f, 0.65f, 0.2f);
                case SimulationTurnLifecycleState.Succeeded:
                    return new Color(0.35f, 0.7f, 0.35f);
                case SimulationTurnLifecycleState.Failed:
                    return new Color(0.95f, 0.35f, 0.3f);
                case SimulationTurnLifecycleState.Running:
                    return new Color(0.9f, 0.7f, 0.25f);
                default:
                    return new Color(0.7f, 0.7f, 0.7f);
            }
        }

        private static string DescribeActorShort(string actor)
        {
            switch (actor)
            {
                case "participant":
                    return "[P]";
                case "user":
                    return "[U]";
                case "ai_tool":
                case "assistant":
                    return "[A]";
                case "tool":
                    return "[T]";
                case "system":
                    return "[S]";
                default:
                    return "[?]";
            }
        }

        private static string DescribeAction(string verb)
        {
            switch (verb)
            {
                case "initialize":
                    return "initialized";
                case "reset":
                    return "reset";
                case "scenario_start":
                    return "started session";
                case "scenario_end":
                    return "ended session";
                case "alarm_triggered":
                    return "triggered alarm";
                case "critical_error":
                    return "recorded critical error";
                case "acknowledge_alarm":
                    return "acknowledged alarm";
                case "move_exit_a":
                    return "moved to Exit A";
                case "move_exit_b":
                    return "moved to Exit B";
                case "help_coworker":
                    return "helped coworker";
                case "runtime_ready":
                    return "runtime ready";
                case "runtime_fallback":
                    return "runtime fallback";
                case "runtime_error":
                    return "runtime error";
                case "error":
                    return "reported issue";
                case "logger_error":
                    return "logger issue";
                case "recovery":
                    return "recovered";
                default:
                    return SafeValue(verb);
            }
        }

        private static string DescribeMessageRoleShort(string role)
        {
            switch (role)
            {
                case "user":
                    return ">";
                case "assistant":
                    return "<";
                case "system":
                    return "#";
                case "tool":
                    return "*";
                default:
                    return "?";
            }
        }

        private static string DescribeTraceKindShort(SimulationConversationTraceKind kind)
        {
            switch (kind)
            {
                case SimulationConversationTraceKind.TurnInput:
                    return "IN";
                case SimulationConversationTraceKind.StateSnapshot:
                    return "ST";
                case SimulationConversationTraceKind.RequestMessagesJson:
                    return "RQ";
                case SimulationConversationTraceKind.CompletionJson:
                    return "CP";
                case SimulationConversationTraceKind.FunctionCall:
                    return "FN";
                case SimulationConversationTraceKind.ToolResult:
                    return "TR";
                case SimulationConversationTraceKind.AssistantResponse:
                    return "AS";
                case SimulationConversationTraceKind.Error:
                    return "ER";
                default:
                    return "??";
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
            _headerStyle.fontSize = 14;
            _headerStyle.wordWrap = false;

            _sectionStyle = new GUIStyle(GUI.skin.label);
            _sectionStyle.fontStyle = FontStyle.Bold;
            _sectionStyle.fontSize = 11;
            _sectionStyle.wordWrap = false;
            _sectionStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);

            _bodyStyle = new GUIStyle(GUI.skin.label);
            _bodyStyle.wordWrap = true;
            _bodyStyle.fontSize = 11;

            _monoStyle = new GUIStyle(_bodyStyle);
            _monoStyle.wordWrap = false;
            _monoStyle.fontSize = 10;

            _dimStyle = new GUIStyle(_bodyStyle);
            _dimStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f);
            _dimStyle.fontSize = 10;

            _errorStyle = new GUIStyle(_bodyStyle);
            _errorStyle.normal.textColor = new Color(0.95f, 0.4f, 0.35f);
            _errorStyle.fontSize = 10;
        }

        private static string NormalizeJsonValue(string valueJson)
        {
            if (string.IsNullOrWhiteSpace(valueJson))
            {
                return "-";
            }

            var trimmed = valueJson.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"')
            {
                return trimmed.Substring(1, trimmed.Length - 2);
            }

            return trimmed;
        }

        private static string SafeValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }
    }
}
