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
        private const float Padding = 16f;
        private const float HeaderHeight = 28f;

        private SimulationConversationDebugManager _manager;
        private SimulationConversationDiagnosticsSnapshot _snapshot;
        private Vector2 _controlsScroll;
        private Vector2 _runtimeScroll;
        private Vector2 _activityScroll;
        private GUIStyle _headerStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _monoStyle;

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
                "Runtime",
                DrawRuntimePanel);

            DrawPanel(
                new Rect(Padding * 3f + (columnWidth * 2f), Padding, columnWidth, contentHeight),
                "Activity",
                DrawActivityPanel);
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

        private void DrawControlsPanel()
        {
            _controlsScroll = GUILayout.BeginScrollView(_controlsScroll);
            var isBusy = _snapshot.TurnState == SimulationTurnLifecycleState.Running ||
                         _snapshot.TurnState == SimulationTurnLifecycleState.Cancelling;

            GUILayout.Label("Session", _headerStyle);
            DrawStateBadge("Runtime State", DescribeRuntimeState(_snapshot.RuntimeState), GetRuntimeStateColor(_snapshot.RuntimeState));
            DrawStateBadge("Turn State", DescribeTurnState(_snapshot.TurnState), GetTurnStateColor(_snapshot.TurnState));
            DrawKeyValue("Requested Mode", SafeValue(_snapshot.RequestedRuntimeMode));
            DrawKeyValue("Selected Mode", SafeValue(_snapshot.SelectedRuntimeMode));
            DrawKeyValue("Backend", SafeValue(_snapshot.ActiveBackendName));
            DrawKeyValue("Session Time", _snapshot.SessionElapsedSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s");
            DrawKeyValue(
                "Turn Counts",
                _snapshot.StartedTurnCount.ToString(CultureInfo.InvariantCulture) +
                " started / " +
                _snapshot.SuccessfulTurnCount.ToString(CultureInfo.InvariantCulture) +
                " ok / " +
                _snapshot.FailedTurnCount.ToString(CultureInfo.InvariantCulture) +
                " failed / " +
                _snapshot.CancelledTurnCount.ToString(CultureInfo.InvariantCulture) +
                " cancelled");
            if (_snapshot.DetachedTurnCount > 0)
            {
                DrawKeyValue(
                    "Detached Turns",
                    _snapshot.DetachedTurnCount.ToString(CultureInfo.InvariantCulture) +
                    " background turn(s) still draining after recovery.");
            }
            if (!string.IsNullOrWhiteSpace(_snapshot.RecoveryStatus))
            {
                DrawKeyValue("Recovery", SafeValue(_snapshot.RecoveryStatus));
            }
            if (isBusy)
            {
                DrawKeyValue(
                    "Active Turn",
                    SafeValue(_snapshot.PendingTurnDescription) +
                    " (" +
                    _snapshot.PendingTurnElapsedSeconds.ToString("0.0", CultureInfo.InvariantCulture) +
                    "s)");
            }
            else if (!string.IsNullOrWhiteSpace(_snapshot.LastTurnError))
            {
                DrawKeyValue("Last Failure", SafeValue(_snapshot.LastTurnError));
            }

            GUILayout.Space(8f);
            GUILayout.Label("Scenario Controls", _headerStyle);
            var previousEnabledState = GUI.enabled;
            GUI.enabled = !isBusy;

            if (GUILayout.Button("Trigger Alarm Escalation"))
            {
                _manager.RunEventTurn("Routine office activity has escalated into an active fire alarm.");
            }

            if (GUILayout.Button("Acknowledge Alarm"))
            {
                _manager.RunParticipantAction(SvrFireScenarioValues.ActionAcknowledgeAlarm);
            }

            if (GUILayout.Button("Move To Exit A"))
            {
                _manager.RunParticipantAction(SvrFireScenarioValues.ActionMoveExitA);
            }

            if (GUILayout.Button("Move To Exit B"))
            {
                _manager.RunParticipantAction(SvrFireScenarioValues.ActionMoveExitB);
            }

            if (GUILayout.Button("Help Coworker"))
            {
                _manager.RunParticipantAction(SvrFireScenarioValues.ActionHelpCoworker);
            }

            if (GUILayout.Button("Request Readiness Summary"))
            {
                _manager.RunUserTurn("Give me the current fire readiness summary.");
            }

            GUILayout.Space(8f);
            GUILayout.Label("Manual Input", _headerStyle);
            _manager.CustomInput = GUILayout.TextField(_manager.CustomInput ?? string.Empty);

            if (GUILayout.Button("Run Text Turn"))
            {
                _manager.RunUserTurn(_manager.CustomInput);
            }

            GUILayout.Space(8f);
            GUILayout.Label("Voice Path", _headerStyle);
            DrawKeyValue("Speech Supported", FormatBool(_snapshot.RuntimeCapabilities.SupportsSpeechTranscription));
            if (!_snapshot.RuntimeCapabilities.SupportsSpeechTranscription)
            {
                GUILayout.Label(
                    "Windows/editor stays text-only. Microphone and STT wiring are part of the Quest/Android runtime path.",
                    _bodyStyle);
            }

            GUILayout.Space(8f);
            GUILayout.Label("Recovery", _headerStyle);
            GUI.enabled = _snapshot.CanAbandonTurn;
            if (GUILayout.Button("Abandon Active Turn"))
            {
                _manager.AbandonActiveTurn();
            }

            GUI.enabled = !isBusy;
            if (GUILayout.Button("Reset Session"))
            {
                _manager.ResetSession();
            }
            GUI.enabled = previousEnabledState;

            GUILayout.EndScrollView();
        }

        private void DrawRuntimePanel()
        {
            _runtimeScroll = GUILayout.BeginScrollView(_runtimeScroll);

            GUILayout.Label("Health", _headerStyle);
            DrawStateBadge(
                "Backend Health",
                DescribeBackendHealth(_snapshot.BackendHealth),
                GetBackendHealthColor(_snapshot.BackendHealth));
            DrawKeyValue("Backend Summary", SafeValue(_snapshot.BackendHealthSummary));
            DrawStateBadge(
                "Logger Health",
                DescribeLoggerHealth(_snapshot.LoggerHealth),
                GetLoggerHealthColor(_snapshot.LoggerHealth));
            DrawKeyValue("Logger Error", SafeValue(_snapshot.LoggerLastError));

            GUILayout.Label("Backend", _headerStyle);
            DrawKeyValue("Backend", SafeValue(_snapshot.ActiveBackendName));
            DrawKeyValue("Summary", SafeValue(_snapshot.RuntimeSummary));
            DrawKeyValue("Uses Live Model", FormatBool(_snapshot.RuntimeCapabilities.UsesLiveModel));
            DrawKeyValue("Target Runtime", FormatBool(_snapshot.RuntimeCapabilities.IsTargetRuntime));
            DrawKeyValue("Text Completion", FormatBool(_snapshot.RuntimeCapabilities.SupportsTextCompletion));
            DrawKeyValue("Tool Calling", FormatBool(_snapshot.RuntimeCapabilities.SupportsToolCalling));
            DrawKeyValue("Speech", FormatBool(_snapshot.RuntimeCapabilities.SupportsSpeechTranscription));
            DrawKeyValue("Model Source", SafeValue(_snapshot.ModelSource));
            DrawKeyValue("Model Reference", SafeValue(_snapshot.ModelReference));

            GUILayout.Space(8f);
            GUILayout.Label("Logger", _headerStyle);
            DrawKeyValue("Session Id", SafeValue(_snapshot.LoggerSessionId));
            DrawKeyValue("Verbosity", SafeValue(_snapshot.LoggerVerbosity));
            DrawKeyValue("Session Directory", SafeValue(_snapshot.LoggerSessionDirectory));
            DrawKeyValue("Events Path", SafeValue(_snapshot.LoggerEventsPath));
            DrawKeyValue("Manifest Path", SafeValue(_snapshot.LoggerManifestPath));
            DrawKeyValue("Written Events", _snapshot.LoggerWrittenEventCount.ToString(CultureInfo.InvariantCulture));
            DrawKeyValue("Failure Count", _snapshot.LoggerFailureCount.ToString(CultureInfo.InvariantCulture));

            if (_snapshot.BootstrapStatus != null &&
                _snapshot.BootstrapStatus.State != SimulationRuntimeBootstrapState.Uninitialized)
            {
                GUILayout.Space(8f);
                GUILayout.Label("Bootstrap", _headerStyle);
                DrawKeyValue("Bootstrap State", _snapshot.BootstrapStatus.State.ToString());
                DrawKeyValue("Path Source", SafeValue(_snapshot.BootstrapStatus.ModelPathSource));
                DrawKeyValue("Model Path", SafeValue(_snapshot.BootstrapStatus.ResolvedModelPath));
                DrawKeyValue("Health Check", SafeValue(_snapshot.BootstrapStatus.HealthCheckResponse));
                DrawKeyValue("Bootstrap Error", SafeValue(_snapshot.BootstrapStatus.Error));
            }

            GUILayout.Space(8f);
            GUILayout.Label("Simulation State", _headerStyle);
            DrawKeyValue("Phase", SafeValue(_snapshot.CurrentPhase));
            DrawKeyValue("Readiness Score", _snapshot.CurrentScore.ToString(CultureInfo.InvariantCulture));
            DrawKeyValue("Readiness Band", SafeValue(_snapshot.CurrentReadinessBand));
            DrawKeyValue("Participant Location", SafeValue(_snapshot.ParticipantLocation));
            DrawKeyValue("Hazard State", SafeValue(_snapshot.HazardState));
            DrawKeyValue("Coworker State", SafeValue(_snapshot.CoworkerState));
            DrawKeyValue("Audit Events", _snapshot.AuditEventCount.ToString(CultureInfo.InvariantCulture));
            DrawKeyValue("Last Participant Action", SafeValue(_snapshot.LastParticipantAction));
            DrawKeyValue("Last Scenario Event", SafeValue(_snapshot.LastScenarioEvent));
            DrawKeyValue("Last Annotation", SafeValue(_snapshot.LastAnnotation));
            DrawKeyValue("Last Freeform Input", SafeValue(_snapshot.LastFreeformInput));
            DrawKeyValue("Last Assistant Reply", SafeValue(_snapshot.LastAssistantResponse));

            GUILayout.Space(8f);
            GUILayout.Label("KPIs", _headerStyle);
            if (_snapshot.KpiSnapshot == null || !_snapshot.KpiSnapshot.HasEntries)
            {
                GUILayout.Label("No KPI snapshot available.", _bodyStyle);
            }
            else
            {
                for (var i = 0; i < _snapshot.KpiSnapshot.Entries.Count; i++)
                {
                    var entry = _snapshot.KpiSnapshot.Entries[i] ?? new SimulationKpiEntry();
                    DrawKeyValue(
                        SafeValue(entry.Label),
                        SafeValue(NormalizeJsonValue(entry.ValueJson)));
                }
            }

            GUILayout.Space(8f);
            GUILayout.Label("Checklist", _headerStyle);
            for (var i = 0; i < _snapshot.Checklist.Length; i++)
            {
                var item = _snapshot.Checklist[i] ?? new SimulationChecklistItem();
                GUILayout.Label(
                    (item.Completed ? "[x] " : "[ ] ") +
                    SafeValue(item.Label) +
                    " | " +
                    SafeValue(item.Notes),
                    _bodyStyle);
            }

            GUILayout.Space(8f);
            GUILayout.Label("Last Turn", _headerStyle);
            DrawKeyValue("Succeeded", FormatBool(_snapshot.LastTurn != null && _snapshot.LastTurn.Success));
            DrawKeyValue(
                "Assistant Reply",
                SafeValue(_snapshot.LastTurn == null ? string.Empty : _snapshot.LastTurn.FinalAssistantResponse));
            DrawKeyValue(
                "Error",
                SafeValue(_snapshot.LastTurn == null ? string.Empty : _snapshot.LastTurn.Error));
            if (_snapshot.LastTurn != null)
            {
                for (var i = 0; i < _snapshot.LastTurn.ToolResults.Length; i++)
                {
                    var toolResult = _snapshot.LastTurn.ToolResults[i] ?? new SimulationToolResult();
                    GUILayout.Label(
                        SafeValue(toolResult.Name) +
                        " | " +
                        SafeValue(toolResult.Content) +
                        (toolResult.IsError ? " | error" : string.Empty),
                        _monoStyle);
                }
            }

            GUILayout.EndScrollView();
        }

        private void DrawActivityPanel()
        {
            _activityScroll = GUILayout.BeginScrollView(_activityScroll);

            GUILayout.Label("Recent Actions", _headerStyle);
            if (_snapshot.Actions.Length == 0)
            {
                GUILayout.Label("No actions recorded yet.", _bodyStyle);
            }
            else
            {
                for (var i = 0; i < _snapshot.Actions.Length; i++)
                {
                    var action = _snapshot.Actions[i] ?? new SimulationActionRecord();
                    GUILayout.Label(
                        action.OccurredAtSeconds.ToString("0.0", CultureInfo.InvariantCulture) +
                        "s | " +
                        DescribeActor(action.Actor) +
                        " | " +
                        DescribeAction(action.Verb) +
                        " | " +
                        SafeValue(action.Details),
                        _monoStyle);
                }
            }

            GUILayout.Space(8f);
            GUILayout.Label("Conversation History", _headerStyle);
            if (_snapshot.History.Length == 0)
            {
                GUILayout.Label("No committed conversation history yet.", _bodyStyle);
            }
            else
            {
                for (var i = 0; i < _snapshot.History.Length; i++)
                {
                    var message = _snapshot.History[i] ?? new ConversationMessage();
                    GUILayout.Label(
                        DescribeMessageRole(message.Role) + ": " + SafeValue(message.Content),
                        _bodyStyle);
                }
            }

            GUILayout.Space(8f);
            GUILayout.Label("Trace", _headerStyle);
            if (_snapshot.TraceEntries.Length == 0)
            {
                GUILayout.Label("No trace entries recorded yet.", _bodyStyle);
            }
            else
            {
                for (var i = 0; i < _snapshot.TraceEntries.Length; i++)
                {
                    var entry = _snapshot.TraceEntries[i] ?? new SimulationConversationTraceEntry();
                    GUILayout.Label(
                        SafeValue(entry.TimestampUtc) +
                        " | " +
                        DescribeTraceKind(entry.Kind) +
                        " | " +
                        SafeValue(entry.Content),
                        _monoStyle);
                }
            }

            GUILayout.EndScrollView();
        }

        private void DrawKeyValue(string label, string value)
        {
            GUILayout.Label(label + ": " + SafeValue(value), _bodyStyle);
        }

        private void DrawStateBadge(string label, string value, Color color)
        {
            var previousColor = GUI.color;
            GUI.color = color;
            GUILayout.Box(label + ": " + SafeValue(value), GUILayout.ExpandWidth(true));
            GUI.color = previousColor;
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

        private static string DescribeActor(string actor)
        {
            switch (actor)
            {
                case "participant":
                    return "Participant";
                case "user":
                    return "Trainee";
                case "ai_tool":
                    return "AI Tool";
                case "assistant":
                    return "AI";
                case "tool":
                    return "Tool";
                case "system":
                    return "System";
                default:
                    return SafeValue(actor);
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
                case "update_score":
                    return "updated score";
                case "scenario_start":
                    return "started session";
                case "scenario_event":
                    return "recorded event";
                case "prompt_participant":
                    return "prompted participant";
                case "change_environment_cue":
                    return "changed environment";
                case "annotate_context":
                    return "recorded annotation";
                case "transition_phase":
                    return "transitioned phase";
                case "request_end_scenario":
                    return "requested completion";
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

        private static string DescribeMessageRole(string role)
        {
            switch (role)
            {
                case "user":
                    return "User";
                case "assistant":
                    return "Assistant";
                case "system":
                    return "System";
                case "tool":
                    return "Tool";
                default:
                    return SafeValue(role);
            }
        }

        private static string DescribeTraceKind(SimulationConversationTraceKind kind)
        {
            switch (kind)
            {
                case SimulationConversationTraceKind.TurnInput:
                    return "Turn Input";
                case SimulationConversationTraceKind.StateSnapshot:
                    return "State Snapshot";
                case SimulationConversationTraceKind.RequestMessagesJson:
                    return "Prompt Payload";
                case SimulationConversationTraceKind.CompletionJson:
                    return "Completion Payload";
                case SimulationConversationTraceKind.FunctionCall:
                    return "Function Call";
                case SimulationConversationTraceKind.ToolResult:
                    return "Tool Result";
                case SimulationConversationTraceKind.AssistantResponse:
                    return "Assistant Response";
                case SimulationConversationTraceKind.Error:
                    return "Error";
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

            _monoStyle = new GUIStyle(_bodyStyle);
            _monoStyle.wordWrap = true;
        }

        private static string NormalizeJsonValue(string valueJson)
        {
            if (string.IsNullOrWhiteSpace(valueJson))
            {
                return string.Empty;
            }

            var trimmed = valueJson.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"')
            {
                return trimmed.Substring(1, trimmed.Length - 2);
            }

            return trimmed;
        }

        private static string FormatBool(bool value)
        {
            return value ? "yes" : "no";
        }

        private static string SafeValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(none)" : value;
        }
    }
}
