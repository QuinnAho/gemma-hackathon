using System;
using System.Collections.Generic;
using GemmaHackathon.SimulationFramework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GemmaHackathon.SimulationExamples
{
    [DisallowMultipleComponent]
    public sealed partial class SimulationConversationDebugHarness : MonoBehaviour
    {
        private const float Padding = 16f;
        private const float HeaderHeight = 28f;

        private ConversationDebugState _state;
        private SimulationConversationManager _manager;
        private SimulationConversationTurnResult _lastTurnResult;
        private readonly List<SimulationConversationTraceEntry> _traceEntries =
            new List<SimulationConversationTraceEntry>();

        private string _customInput = "Trainee completed the first action.";
        private Vector2 _controlsScroll;
        private Vector2 _stateScroll;
        private Vector2 _historyScroll;
        private Vector2 _traceScroll;
        private float _startTime;
        private GUIStyle _headerStyle;
        private GUIStyle _bodyStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (!ShouldAutoCreate())
            {
                return;
            }

            if (Object.FindFirstObjectByType<SimulationConversationDebugHarness>() != null)
            {
                return;
            }

            var gameObject = new GameObject("Simulation Conversation Debug Harness");
            DontDestroyOnLoad(gameObject);
            gameObject.AddComponent<SimulationConversationDebugHarness>();
        }

        private static bool ShouldAutoCreate()
        {
            if (!Application.isPlaying)
            {
                return false;
            }

            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor:
                case RuntimePlatform.OSXEditor:
                case RuntimePlatform.LinuxEditor:
                case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.OSXPlayer:
                case RuntimePlatform.LinuxPlayer:
                    return true;
                default:
                    return false;
            }
        }

        private void Awake()
        {
            _startTime = Time.realtimeSinceStartup;
            _state = new ConversationDebugState();

            var registry = new SimulationToolRegistry();
            registry.Register(new UpdateScoreTool(_state, GetElapsedSeconds));
            registry.Register(new AdvancePhaseTool(_state, GetElapsedSeconds));
            registry.Register(new LogDecisionTool(_state, GetElapsedSeconds));

            var options = new SimulationConversationManagerOptions();
            options.SystemPrompt =
                "You are a simulation-aware debug assistant. Review the simulation state JSON, " +
                "request tools when state should change, and then summarize the updated state briefly.";
            options.TraceSink = OnTraceEntry;

            _manager = new SimulationConversationManager(
                new DebugCompletionModel(_state),
                new DebugStateProvider(_state, GetElapsedSeconds),
                registry,
                options);

            _lastTurnResult = new SimulationConversationTurnResult();
            _lastTurnResult.Success = true;
            _lastTurnResult.FinalAssistantResponse = "Demo ready.";
            _state.LastAssistantResponse = _lastTurnResult.FinalAssistantResponse;
            _state.AddAction("system", "initialize", "Conversation debug harness started.", GetElapsedSeconds());
        }

        private void RunUserTurn(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            _state.LastUserInput = text;
            _lastTurnResult = _manager.ProcessUserText(text);
            ApplyTurnResult(_lastTurnResult);
        }

        private void RunEventTurn(string eventDescription)
        {
            if (string.IsNullOrWhiteSpace(eventDescription))
            {
                return;
            }

            _state.LastEvent = eventDescription;
            _lastTurnResult = _manager.ProcessSimulationEvent(eventDescription);
            ApplyTurnResult(_lastTurnResult);
        }

        private void ApplyTurnResult(SimulationConversationTurnResult result)
        {
            if (result == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(result.FinalAssistantResponse))
            {
                _state.LastAssistantResponse = result.FinalAssistantResponse;
            }

            if (!result.Success && !string.IsNullOrWhiteSpace(result.Error))
            {
                _state.AddAction("system", "error", result.Error, GetElapsedSeconds());
            }
        }

        private void OnTraceEntry(SimulationConversationTraceEntry entry)
        {
            _traceEntries.Add(entry);
            if (_traceEntries.Count > 64)
            {
                _traceEntries.RemoveAt(0);
            }
        }

        private float GetElapsedSeconds()
        {
            return Time.realtimeSinceStartup - _startTime;
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

        private string BuildStatusText()
        {
            if (_lastTurnResult == null)
            {
                return "Ready\nRun any example action to see the full flow.";
            }

            if (_lastTurnResult.Success)
            {
                return "System Ready\nLatest reply: " + SafeValue(_lastTurnResult.FinalAssistantResponse);
            }

            return "Needs Attention\n" + SafeValue(_lastTurnResult.Error);
        }

        private static string SafeValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(none)" : value;
        }
    }
}
