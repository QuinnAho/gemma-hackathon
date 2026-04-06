using System;
using System.Collections.Generic;
using System.IO;
using GemmaHackathon.SimulationFramework;
using UnityEngine;

namespace GemmaHackathon.SimulationExamples
{
    public enum SimulationExampleRuntimeMode
    {
        Automatic,
        DesktopGemmaOnly,
        DesktopGemmaWithScriptedFallback,
        QuestCactusOnly,
        QuestCactusWithScriptedFallback,
        ScriptedScenarioOnly
    }

    [AddComponentMenu("Gemma Hackathon/Debug Harness/Simulation Conversation Debug Manager")]
    [DisallowMultipleComponent]
    public sealed partial class SimulationConversationDebugManager : MonoBehaviour
    {
        private static readonly IReadOnlyList<ConversationMessage> EmptyHistory = Array.Empty<ConversationMessage>();
        private static readonly IReadOnlyList<SimulationChecklistItem> EmptyChecklist =
            Array.Empty<SimulationChecklistItem>();
        private static readonly IReadOnlyList<SimulationActionRecord> EmptyActions =
            Array.Empty<SimulationActionRecord>();
        private static readonly SimulationRuntimeBootstrapStatus EmptyBootstrapStatus =
            new SimulationRuntimeBootstrapStatus();
        private static readonly SimulationRuntimeCapabilities DefaultRuntimeCapabilities =
            new SimulationRuntimeCapabilities();

        [SerializeField] private bool _createOverlayIfMissing = true;
        [SerializeField] private bool _overlayVisible = true;
        [SerializeField] [TextArea(1, 3)] private string _defaultCustomInput = "Trainee completed the first action.";
        [SerializeField] private SimulationExampleRuntimeMode _runtimeMode = SimulationExampleRuntimeMode.Automatic;
        [SerializeField] private string _desktopGemmaModelIdentifierOrPath = string.Empty;
        [SerializeField] private string _desktopGemmaPythonExecutablePath = string.Empty;
        [SerializeField] private string _cactusModelPathOverride = string.Empty;
        [SerializeField] private string _cactusModelRelativePath = "gemma-4-e2b";
        [SerializeField] private string _telemetryCachePathOverride = string.Empty;

        private string _customInput = string.Empty;
        private ExampleScenarioState _scenarioState;
        private SimulationConversationManager _conversationManager;
        private SimulationRuntimeBootstrapService _cactusBootstrapService;
        private IDisposable _activeRuntimeResource;
        private SimulationConversationTurnResult _lastTurnResult;
        private readonly List<SimulationConversationTraceEntry> _traceEntries =
            new List<SimulationConversationTraceEntry>();
        private float _startTime;
        private string _runtimeSummary = string.Empty;
        private string _activeBackendName = string.Empty;
        private SimulationRuntimeCapabilities _runtimeCapabilities = new SimulationRuntimeCapabilities();

        private void Awake()
        {
            InitializeIfNeeded();
            EnsureOverlayIfNeeded();
        }

        private void OnDestroy()
        {
            DisposeRuntimeResource();
        }

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(_defaultCustomInput))
            {
                _defaultCustomInput = "Trainee completed the first action.";
            }

            if (string.IsNullOrWhiteSpace(_cactusModelRelativePath))
            {
                _cactusModelRelativePath = "gemma-4-e2b";
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

        public string ActiveBackendName
        {
            get { return string.IsNullOrWhiteSpace(_activeBackendName) ? "Not initialized" : _activeBackendName; }
        }

        public string RuntimeSummary
        {
            get { return string.IsNullOrWhiteSpace(_runtimeSummary) ? "(none)" : _runtimeSummary; }
        }

        public SimulationRuntimeCapabilities RuntimeCapabilities
        {
            get { return _runtimeCapabilities ?? DefaultRuntimeCapabilities; }
        }

        public SimulationRuntimeBootstrapStatus BootstrapStatus
        {
            get
            {
                if (_cactusBootstrapService == null || _cactusBootstrapService.Status == null)
                {
                    return EmptyBootstrapStatus;
                }

                return _cactusBootstrapService.Status;
            }
        }

        public string CurrentPhase
        {
            get { return _scenarioState == null ? string.Empty : _scenarioState.Phase; }
        }

        public int CurrentScore
        {
            get { return _scenarioState == null ? 0 : _scenarioState.Score; }
        }

        public string LastUserInput
        {
            get { return _scenarioState == null ? string.Empty : _scenarioState.LastUserInput; }
        }

        public string LastEvent
        {
            get { return _scenarioState == null ? string.Empty : _scenarioState.LastEvent; }
        }

        public string LastDecision
        {
            get { return _scenarioState == null ? string.Empty : _scenarioState.LastDecision; }
        }

        public string LastAssistantResponse
        {
            get { return _scenarioState == null ? string.Empty : _scenarioState.LastAssistantResponse; }
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
            get { return _scenarioState == null ? EmptyChecklist : _scenarioState.Checklist; }
        }

        public IReadOnlyList<SimulationActionRecord> Actions
        {
            get { return _scenarioState == null ? EmptyActions : _scenarioState.Actions; }
        }

        public SimulationConversationTurnResult RunUserTurn(string text)
        {
            InitializeIfNeeded();

            if (string.IsNullOrWhiteSpace(text))
            {
                return _lastTurnResult;
            }

            _scenarioState.LastUserInput = text;
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

            _scenarioState.LastEvent = eventDescription;
            _lastTurnResult = _conversationManager.ProcessSimulationEvent(eventDescription);
            ApplyTurnResult(_lastTurnResult);
            return _lastTurnResult;
        }

        public void ResetSession()
        {
            InitializeIfNeeded();

            _conversationManager.ClearHistory();
            _traceEntries.Clear();
            _scenarioState.ResetForNewSession();
            _startTime = Time.realtimeSinceStartup;
            _customInput = _defaultCustomInput;
            _lastTurnResult = new SimulationConversationTurnResult();
            _lastTurnResult.Success = true;
            _lastTurnResult.FinalAssistantResponse = "New session started.";
            _scenarioState.LastAssistantResponse = _lastTurnResult.FinalAssistantResponse;
            _scenarioState.AddAction("system", "reset", "Conversation history cleared.", GetElapsedSeconds());
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
            _scenarioState = new ExampleScenarioState();

            var toolRegistry = new SimulationToolRegistry();
            toolRegistry.Register(new UpdateScoreTool(_scenarioState, GetElapsedSeconds));
            toolRegistry.Register(new AdvancePhaseTool(_scenarioState, GetElapsedSeconds));
            toolRegistry.Register(new LogDecisionTool(_scenarioState, GetElapsedSeconds));

            var conversationOptions = new SimulationConversationManagerOptions();
            conversationOptions.SystemPrompt =
                "You are a simulation-aware assistant. Review the current simulation state JSON, " +
                "request tools when the state should change, and then summarize the updated state briefly.";
            conversationOptions.TraceSink = OnTraceEntry;

            var completionModel = BuildCompletionModel();
            _conversationManager = new SimulationConversationManager(
                completionModel,
                new ExampleScenarioStateProvider(_scenarioState, GetElapsedSeconds),
                toolRegistry,
                conversationOptions);

            _lastTurnResult = new SimulationConversationTurnResult();
            _lastTurnResult.Success = true;
            _lastTurnResult.FinalAssistantResponse = RuntimeCapabilities.UsesLiveModel
                ? "Runtime initialized."
                : "Scenario ready.";
            _scenarioState.LastAssistantResponse = _lastTurnResult.FinalAssistantResponse;
            _scenarioState.AddAction("system", "initialize", "Conversation debug harness started.", GetElapsedSeconds());
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
                _scenarioState.LastAssistantResponse = result.FinalAssistantResponse;
            }

            if (!result.Success && !string.IsNullOrWhiteSpace(result.Error))
            {
                _scenarioState.AddAction("system", "error", result.Error, GetElapsedSeconds());
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
                return "System Ready\n" + ActiveBackendName + "\nLatest reply: " + SafeValue(_lastTurnResult.FinalAssistantResponse);
            }

            return "Needs Attention\n" + ActiveBackendName + "\n" + SafeValue(_lastTurnResult.Error);
        }

        private ISimulationCompletionModel BuildCompletionModel()
        {
            DisposeRuntimeResource();
            _runtimeCapabilities = new SimulationRuntimeCapabilities();

            switch (ResolveSelectedRuntimeMode())
            {
                case SimulationExampleRuntimeMode.DesktopGemmaOnly:
                    return BuildDesktopGemmaModel(false);
                case SimulationExampleRuntimeMode.DesktopGemmaWithScriptedFallback:
                    return BuildDesktopGemmaModel(true);
                case SimulationExampleRuntimeMode.QuestCactusOnly:
                    return BuildQuestCactusModel(false);
                case SimulationExampleRuntimeMode.QuestCactusWithScriptedFallback:
                    return BuildQuestCactusModel(true);
                case SimulationExampleRuntimeMode.ScriptedScenarioOnly:
                default:
                    return BuildScriptedScenarioModel(
                        "Using the scripted scenario model for architecture and scene-flow validation.");
            }
        }

        private SimulationExampleRuntimeMode ResolveSelectedRuntimeMode()
        {
            if (_runtimeMode != SimulationExampleRuntimeMode.Automatic)
            {
                return _runtimeMode;
            }

            return Application.platform == RuntimePlatform.Android
                ? SimulationExampleRuntimeMode.QuestCactusOnly
                : SimulationExampleRuntimeMode.DesktopGemmaWithScriptedFallback;
        }

        private ISimulationCompletionModel BuildDesktopGemmaModel(bool allowScriptedFallback)
        {
            try
            {
                var bridgeSettings = new DesktopGemmaBridgeSettings();
                bridgeSettings.PythonExecutablePath = _desktopGemmaPythonExecutablePath;
                bridgeSettings.ModelIdentifierOrPath = _desktopGemmaModelIdentifierOrPath;

                var bridgeModel = new DesktopGemmaBridgeCompletionModel(bridgeSettings);
                _activeRuntimeResource = bridgeModel;
                _activeBackendName = "Desktop Gemma";
                _runtimeSummary =
                    bridgeModel.ReadySummary +
                    " This is the editor validation path, so speech transcription stays disabled here.";
                _runtimeCapabilities.SupportsTextCompletion = true;
                _runtimeCapabilities.SupportsToolCalling = true;
                _runtimeCapabilities.SupportsSpeechTranscription = false;
                _runtimeCapabilities.UsesLiveModel = true;
                _runtimeCapabilities.IsTargetRuntime = false;
                _scenarioState.AddAction("system", "runtime_ready", "Desktop Gemma bridge initialized.", GetElapsedSeconds());
                return bridgeModel;
            }
            catch (Exception ex)
            {
                if (allowScriptedFallback)
                {
                    _scenarioState.AddAction("system", "runtime_fallback", ex.Message, GetElapsedSeconds());
                    return BuildScriptedScenarioModel(
                        "Desktop Gemma was requested but could not start, so the scene fell back to scripted responses. Issue: " +
                        ex.Message);
                }

                _activeBackendName = "Desktop Gemma (Unavailable)";
                _runtimeSummary = ex.Message;
                _runtimeCapabilities.SupportsTextCompletion = false;
                _runtimeCapabilities.SupportsToolCalling = false;
                _runtimeCapabilities.SupportsSpeechTranscription = false;
                _runtimeCapabilities.UsesLiveModel = false;
                _runtimeCapabilities.IsTargetRuntime = false;
                _scenarioState.AddAction("system", "runtime_error", ex.Message, GetElapsedSeconds());
                return new UnavailableSimulationCompletionModel(ex.Message);
            }
        }

        private ISimulationCompletionModel BuildQuestCactusModel(bool allowScriptedFallback)
        {
            _cactusBootstrapService = new SimulationRuntimeBootstrapService(BuildCactusBootstrapOptions());
            _activeRuntimeResource = _cactusBootstrapService;

            if (_cactusBootstrapService.Initialize() && _cactusBootstrapService.CompletionModel != null)
            {
                _activeBackendName = "Quest Cactus";
                _runtimeSummary =
                    "Using a real Cactus session. Model source: " +
                    SafeValue(_cactusBootstrapService.Status.ModelPathSource) +
                    ". Health check reply: " +
                    SafeValue(_cactusBootstrapService.Status.HealthCheckResponse);
                _runtimeCapabilities.SupportsTextCompletion = true;
                _runtimeCapabilities.SupportsToolCalling = true;
                _runtimeCapabilities.SupportsSpeechTranscription = true;
                _runtimeCapabilities.UsesLiveModel = true;
                _runtimeCapabilities.IsTargetRuntime = true;
                _scenarioState.AddAction("system", "runtime_ready", "Quest Cactus runtime initialized.", GetElapsedSeconds());
                return _cactusBootstrapService.CompletionModel;
            }

            var error = _cactusBootstrapService.Status == null || string.IsNullOrWhiteSpace(_cactusBootstrapService.Status.Error)
                ? "Quest Cactus runtime initialization failed."
                : _cactusBootstrapService.Status.Error;

            if (allowScriptedFallback)
            {
                _scenarioState.AddAction("system", "runtime_fallback", error, GetElapsedSeconds());
                return BuildScriptedScenarioModel(
                    "Quest Cactus was requested but could not start, so the scene fell back to scripted responses. Issue: " +
                    error);
            }

            _activeBackendName = "Quest Cactus (Unavailable)";
            _runtimeSummary = error;
            _runtimeCapabilities.SupportsTextCompletion = false;
            _runtimeCapabilities.SupportsToolCalling = false;
            _runtimeCapabilities.SupportsSpeechTranscription = false;
            _runtimeCapabilities.UsesLiveModel = false;
            _runtimeCapabilities.IsTargetRuntime = true;
            _scenarioState.AddAction("system", "runtime_error", error, GetElapsedSeconds());
            return new UnavailableSimulationCompletionModel(error);
        }

        private ISimulationCompletionModel BuildScriptedScenarioModel(string reason)
        {
            _activeBackendName = "Scripted Scenario Model";
            _runtimeSummary = reason;
            _runtimeCapabilities.SupportsTextCompletion = true;
            _runtimeCapabilities.SupportsToolCalling = true;
            _runtimeCapabilities.SupportsSpeechTranscription = false;
            _runtimeCapabilities.UsesLiveModel = false;
            _runtimeCapabilities.IsTargetRuntime = false;
            return new ScriptedScenarioCompletionModel(_scenarioState);
        }

        private SimulationRuntimeBootstrapOptions BuildCactusBootstrapOptions()
        {
            var options = new SimulationRuntimeBootstrapOptions();
            options.AppId = "gemma-hackathon-debug-harness";
            options.TelemetryCachePath = ResolveTelemetryCachePath();
            options.LogLevel = CactusLogLevel.Info;
            options.ModelPathResolver = new ConfiguredCactusModelPathResolver
            {
                ExplicitModelPath = _cactusModelPathOverride,
                RelativeModelPath = _cactusModelRelativePath
            };
            return options;
        }

        private string ResolveTelemetryCachePath()
        {
            if (!string.IsNullOrWhiteSpace(_telemetryCachePathOverride))
            {
                return ResolvePathAgainstCurrentDirectory(_telemetryCachePathOverride);
            }

            return Path.Combine(Application.persistentDataPath, "cactus-telemetry");
        }

        private static string ResolvePathAgainstCurrentDirectory(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return Path.IsPathRooted(value)
                ? Path.GetFullPath(value)
                : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), value));
        }

        private void DisposeRuntimeResource()
        {
            try
            {
                _activeRuntimeResource?.Dispose();
            }
            catch
            {
            }
            finally
            {
                _activeRuntimeResource = null;
                _cactusBootstrapService = null;
            }
        }

        private static string SafeValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(none)" : value;
        }
    }
}
