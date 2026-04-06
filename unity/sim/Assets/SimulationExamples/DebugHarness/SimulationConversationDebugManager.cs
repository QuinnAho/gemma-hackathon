using System;
using System.Collections.Generic;
using GemmaHackathon.SimulationFramework;
using UnityEngine;

namespace GemmaHackathon.SimulationExamples
{
    [AddComponentMenu("Gemma Hackathon/Debug Harness/Simulation Conversation Debug Manager")]
    [DisallowMultipleComponent]
    public sealed partial class SimulationConversationDebugManager : MonoBehaviour
    {
        private static readonly IReadOnlyList<ConversationMessage> EmptyHistory = Array.Empty<ConversationMessage>();
        private static readonly IReadOnlyList<SimulationChecklistItem> EmptyChecklist =
            Array.Empty<SimulationChecklistItem>();
        private static readonly IReadOnlyList<SimulationActionRecord> EmptyActions =
            Array.Empty<SimulationActionRecord>();

        [SerializeField] private bool _createOverlayIfMissing = true;
        [SerializeField] private bool _overlayVisible = true;
        [SerializeField] [TextArea(1, 3)] private string _defaultCustomInput = "Trainee completed the first action.";

        private string _customInput = string.Empty;
        private ConversationDebugState _state;
        private SimulationConversationManager _conversationManager;
        private SimulationConversationTurnResult _lastTurnResult;
        private readonly List<SimulationConversationTraceEntry> _traceEntries =
            new List<SimulationConversationTraceEntry>();
        private float _startTime;

        private void Awake()
        {
            InitializeIfNeeded();
            EnsureOverlayIfNeeded();
        }

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(_defaultCustomInput))
            {
                _defaultCustomInput = "Trainee completed the first action.";
            }
        }

        public bool OverlayVisible
        {
            get { return _overlayVisible; }
            set { _overlayVisible = value; }
        }

        public bool IsInitialized
        {
            get { return _conversationManager != null; }
        }

        public string StatusText
        {
            get { return BuildStatusText(); }
        }

        public string CurrentPhase
        {
            get { return _state == null ? string.Empty : _state.Phase; }
        }

        public int CurrentScore
        {
            get { return _state == null ? 0 : _state.Score; }
        }

        public string LastUserInput
        {
            get { return _state == null ? string.Empty : _state.LastUserInput; }
        }

        public string LastEvent
        {
            get { return _state == null ? string.Empty : _state.LastEvent; }
        }

        public string LastDecision
        {
            get { return _state == null ? string.Empty : _state.LastDecision; }
        }

        public string LastAssistantResponse
        {
            get { return _state == null ? string.Empty : _state.LastAssistantResponse; }
        }

        public float ElapsedSeconds
        {
            get { return GetElapsedSeconds(); }
        }

        public string CustomInput
        {
            get { return _customInput; }
            set { _customInput = value ?? string.Empty; }
        }

        public SimulationConversationTurnResult LastTurnResult
        {
            get { return _lastTurnResult; }
        }

        public IReadOnlyList<ConversationMessage> History
        {
            get { return _conversationManager == null ? EmptyHistory : _conversationManager.History; }
        }

        public IReadOnlyList<SimulationConversationTraceEntry> TraceEntries
        {
            get { return _traceEntries; }
        }

        public IReadOnlyList<SimulationChecklistItem> Checklist
        {
            get { return _state == null ? EmptyChecklist : _state.Checklist; }
        }

        public IReadOnlyList<SimulationActionRecord> Actions
        {
            get { return _state == null ? EmptyActions : _state.Actions; }
        }

        public SimulationConversationTurnResult RunUserTurn(string text)
        {
            InitializeIfNeeded();

            if (string.IsNullOrWhiteSpace(text))
            {
                return _lastTurnResult;
            }

            _state.LastUserInput = text;
            _lastTurnResult = _conversationManager.ProcessUserText(text);
            ApplyTurnResult(_lastTurnResult);
            return _lastTurnResult;
        }

        public SimulationConversationTurnResult RunEventTurn(string eventDescription)
        {
            InitializeIfNeeded();

            if (string.IsNullOrWhiteSpace(eventDescription))
            {
                return _lastTurnResult;
            }

            _state.LastEvent = eventDescription;
            _lastTurnResult = _conversationManager.ProcessSimulationEvent(eventDescription);
            ApplyTurnResult(_lastTurnResult);
            return _lastTurnResult;
        }

        public void ResetSession()
        {
            InitializeIfNeeded();

            _conversationManager.ClearHistory();
            _traceEntries.Clear();
            _state.ResetForNewSession();
            _startTime = Time.realtimeSinceStartup;
            _customInput = _defaultCustomInput;
            _lastTurnResult = new SimulationConversationTurnResult();
            _lastTurnResult.Success = true;
            _lastTurnResult.FinalAssistantResponse = "New session started.";
            _state.LastAssistantResponse = _lastTurnResult.FinalAssistantResponse;
            _state.AddAction("system", "reset", "Conversation history cleared.", GetElapsedSeconds());
        }

        private void InitializeIfNeeded()
        {
            if (_conversationManager != null)
            {
                return;
            }

            _startTime = Time.realtimeSinceStartup;
            _customInput = string.IsNullOrWhiteSpace(_defaultCustomInput)
                ? "Trainee completed the first action."
                : _defaultCustomInput;
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

            _conversationManager = new SimulationConversationManager(
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

        private void EnsureOverlayIfNeeded()
        {
            if (!_createOverlayIfMissing)
            {
                return;
            }

            if (GetComponent<SimulationConversationDebugOverlay>() != null)
            {
                return;
            }

            gameObject.AddComponent<SimulationConversationDebugOverlay>();
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
