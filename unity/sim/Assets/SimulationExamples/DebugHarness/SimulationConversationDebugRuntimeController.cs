using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GemmaHackathon.SimulationFramework;
using UnityEngine;

namespace GemmaHackathon.SimulationExamples
{
    internal sealed class SimulationConversationDebugRuntimeConfiguration
    {
        public string AppId = "gemma-hackathon-debug-harness";
        public string SceneName = string.Empty;
        public RuntimePlatform Platform = RuntimePlatform.WindowsEditor;
        public int UnityThreadId;
        public string WorkingDirectoryPath = string.Empty;
        public string PersistentDataPath = string.Empty;
        public SimulationExampleRuntimeMode RequestedRuntimeMode = SimulationExampleRuntimeMode.Automatic;
        public string DesktopGemmaModelIdentifierOrPath = string.Empty;
        public string DesktopGemmaPythonExecutablePath = string.Empty;
        public string CactusModelPathOverride = string.Empty;
        public string CactusModelRelativePath = "gemma-4-e2b";
        public string TelemetryCachePathOverride = string.Empty;
    }

    internal sealed class SimulationConversationDebugRuntimeController : IDisposable
    {
        private static readonly IReadOnlyList<SimulationChecklistItem> EmptyChecklist =
            Array.Empty<SimulationChecklistItem>();
        private static readonly IReadOnlyList<SimulationActionRecord> EmptyActions =
            Array.Empty<SimulationActionRecord>();
        private static readonly SimulationRuntimeBootstrapStatus EmptyBootstrapStatus =
            new SimulationRuntimeBootstrapStatus();
        private static readonly SimulationRuntimeCapabilities DefaultRuntimeCapabilities =
            new SimulationRuntimeCapabilities();

        private readonly SimulationConversationDebugRuntimeConfiguration _configuration;
        private readonly ConcurrentQueue<PendingTraceEntry> _pendingTraceEntries =
            new ConcurrentQueue<PendingTraceEntry>();
        private readonly ConcurrentQueue<MainThreadToolExecutionRequest> _pendingToolExecutionRequests =
            new ConcurrentQueue<MainThreadToolExecutionRequest>();
        private readonly List<DetachedTurnOperation> _detachedTurnOperations =
            new List<DetachedTurnOperation>();
        private readonly List<SimulationConversationTraceEntry> _traceEntries =
            new List<SimulationConversationTraceEntry>();
        private readonly List<ConversationMessage> _historySnapshot = new List<ConversationMessage>();

        private ExampleScenarioState _scenarioState;
        private ExampleScenarioStateProvider _stateProvider;
        private SimulationConversationManager _conversationManager;
        private SimulationRuntimeBootstrapService _cactusBootstrapService;
        private ISimulationRunLogger _runLogger;
        private IDisposable _activeRuntimeResource;
        private SimulationConversationTurnResult _lastTurnResult;
        private Task<SimulationConversationTurnResult> _pendingTurnTask;
        private MainThreadSimulationToolExecutor _toolExecutor;
        private DateTime _pendingTurnStartedUtc;
        private DateTime _sessionStartedUtc;
        private string _pendingTurnDescription = string.Empty;
        private string _lastTurnError = string.Empty;
        private string _runtimeSummary = string.Empty;
        private string _activeBackendName = string.Empty;
        private string _activeModelSource = string.Empty;
        private string _activeModelReference = string.Empty;
        private SimulationRuntimeLifecycleState _runtimeLifecycleState = SimulationRuntimeLifecycleState.Uninitialized;
        private SimulationTurnLifecycleState _turnLifecycleState = SimulationTurnLifecycleState.Idle;
        private SimulationRuntimeCapabilities _runtimeCapabilities = new SimulationRuntimeCapabilities();
        private volatile bool _acceptToolExecutionRequests = true;
        private int _runtimeGeneration;
        private int _startedTurnCount;
        private int _successfulTurnCount;
        private int _failedTurnCount;
        private int _cancelledTurnCount;
        private string _recoveryStatus = string.Empty;
        private string _loggerInitializationError = string.Empty;

        public SimulationConversationDebugRuntimeController(
            SimulationConversationDebugRuntimeConfiguration configuration)
        {
            _configuration = configuration ?? new SimulationConversationDebugRuntimeConfiguration();
            if (_configuration.UnityThreadId == 0)
            {
                _configuration.UnityThreadId = Thread.CurrentThread.ManagedThreadId;
            }

            if (string.IsNullOrWhiteSpace(_configuration.CactusModelRelativePath))
            {
                _configuration.CactusModelRelativePath = "gemma-4-e2b";
            }
        }

        public void InitializeIfNeeded()
        {
            if (_conversationManager != null)
            {
                return;
            }

            _runtimeGeneration++;
            _sessionStartedUtc = DateTime.UtcNow;
            _startedTurnCount = 0;
            _successfulTurnCount = 0;
            _failedTurnCount = 0;
            _lastTurnError = string.Empty;
            _runtimeLifecycleState = SimulationRuntimeLifecycleState.Loading;
            _turnLifecycleState = SimulationTurnLifecycleState.Idle;
            _loggerInitializationError = string.Empty;
            _scenarioState = new ExampleScenarioState();
            _stateProvider = new ExampleScenarioStateProvider(_scenarioState, GetElapsedSeconds);

            InitializeRunLogger();

            var toolRegistry = new SimulationToolRegistry();
            toolRegistry.Register(new UpdateScoreTool(_scenarioState, GetElapsedSeconds));
            toolRegistry.Register(new AdvancePhaseTool(_scenarioState, GetElapsedSeconds));
            toolRegistry.Register(new LogDecisionTool(_scenarioState, GetElapsedSeconds));

            var conversationOptions = new SimulationConversationManagerOptions();
            conversationOptions.SystemPrompt =
                "You are a simulation-aware assistant. Review the current simulation state JSON, " +
                "request tools when the state should change, and then summarize the updated state briefly.";
            conversationOptions.TraceSink = CreateTraceSink(_runtimeGeneration);
            conversationOptions.ToolExecutor = ResolveToolExecutor();
            conversationOptions.RunLogger = _runLogger;

            var completionModel = BuildCompletionModel();
            _conversationManager = new SimulationConversationManager(
                completionModel,
                _stateProvider,
                toolRegistry,
                conversationOptions);

            _lastTurnResult = new SimulationConversationTurnResult();
            _lastTurnResult.Success = true;
            _lastTurnResult.FinalAssistantResponse = _runtimeCapabilities.UsesLiveModel
                ? "Runtime initialized."
                : "Scenario ready.";
            _scenarioState.LastAssistantResponse = _lastTurnResult.FinalAssistantResponse;
            _scenarioState.AddAction("system", "initialize", "Conversation debug harness started.", GetElapsedSeconds());
        }

        public void Tick()
        {
            DrainPendingToolExecutionRequests();
            DrainPendingTraceEntries();
            CompletePendingTurnIfReady();
            DrainDetachedTurnOperations();
        }

        public SimulationConversationDiagnosticsSnapshot CaptureDiagnosticsSnapshot()
        {
            var runtimeCapabilities = CloneRuntimeCapabilities(_runtimeCapabilities);
            var bootstrapStatus = CloneBootstrapStatus(GetBootstrapStatus());
            var loggerDiagnostics = GetLoggerDiagnostics();
            var history = CloneHistorySnapshot();
            var traceEntries = CloneTraceEntriesSnapshot();
            var checklist = _scenarioState == null ? EmptyChecklist : _scenarioState.GetChecklistSnapshot();
            var actions = _scenarioState == null ? EmptyActions : _scenarioState.GetActionsSnapshot();
            var kpiSnapshot = CaptureCurrentKpiSnapshot();

            return new SimulationConversationDiagnosticsSnapshot
            {
                RuntimeState = _runtimeLifecycleState,
                TurnState = _turnLifecycleState,
                RequestedRuntimeMode = _configuration.RequestedRuntimeMode.ToString(),
                SelectedRuntimeMode = ResolveSelectedRuntimeMode().ToString(),
                ActiveBackendName = string.IsNullOrWhiteSpace(_activeBackendName) ? "Not initialized" : _activeBackendName,
                RuntimeSummary = string.IsNullOrWhiteSpace(_runtimeSummary) ? "(none)" : _runtimeSummary,
                BackendHealth = ResolveBackendHealthState(bootstrapStatus),
                BackendHealthSummary = BuildBackendHealthSummary(bootstrapStatus),
                ModelSource = _activeModelSource ?? string.Empty,
                ModelReference = _activeModelReference ?? string.Empty,
                RuntimeCapabilities = runtimeCapabilities,
                BootstrapStatus = bootstrapStatus,
                LoggerHealth = ResolveLoggerHealthState(loggerDiagnostics),
                LoggerSessionId = _runLogger == null ? string.Empty : (_runLogger.SessionId ?? string.Empty),
                LoggerVerbosity = _runLogger == null ? string.Empty : _runLogger.Verbosity.ToString(),
                LoggerSessionDirectory = loggerDiagnostics == null ? string.Empty : (loggerDiagnostics.SessionDirectoryPath ?? string.Empty),
                LoggerEventsPath = loggerDiagnostics == null ? string.Empty : (loggerDiagnostics.EventsPath ?? string.Empty),
                LoggerManifestPath = loggerDiagnostics == null ? string.Empty : (loggerDiagnostics.ManifestPath ?? string.Empty),
                LoggerWrittenEventCount = loggerDiagnostics == null ? 0 : loggerDiagnostics.WrittenEventCount,
                LoggerFailureCount = loggerDiagnostics == null ? 0 : loggerDiagnostics.FailureCount,
                LoggerLastError = GetLoggerLastError(loggerDiagnostics),
                SessionElapsedSeconds = GetElapsedSeconds(),
                StartedTurnCount = _startedTurnCount,
                SuccessfulTurnCount = _successfulTurnCount,
                FailedTurnCount = _failedTurnCount,
                CancelledTurnCount = _cancelledTurnCount,
                DetachedTurnCount = _detachedTurnOperations.Count,
                LastTurnError = _lastTurnError ?? string.Empty,
                RecoveryStatus = _recoveryStatus ?? string.Empty,
                CanAbandonTurn = IsTurnInProgress,
                PendingTurnDescription = GetPendingTurnDescription(),
                PendingTurnElapsedSeconds = GetPendingTurnElapsedSeconds(),
                CurrentPhase = _scenarioState == null ? string.Empty : _scenarioState.Phase,
                CurrentScore = _scenarioState == null ? 0 : _scenarioState.Score,
                LastUserInput = _scenarioState == null ? string.Empty : _scenarioState.LastUserInput,
                LastEvent = _scenarioState == null ? string.Empty : _scenarioState.LastEvent,
                LastDecision = _scenarioState == null ? string.Empty : _scenarioState.LastDecision,
                LastAssistantResponse = _scenarioState == null ? string.Empty : _scenarioState.LastAssistantResponse,
                KpiSnapshot = kpiSnapshot,
                LastTurn = CloneTurnSummary(_lastTurnResult),
                History = history,
                TraceEntries = traceEntries,
                Checklist = CopyChecklistArray(checklist),
                Actions = CopyActionArray(actions)
            };
        }

        public SimulationConversationTurnResult RunUserTurn(string text)
        {
            InitializeIfNeeded();

            if (IsTurnInProgress || string.IsNullOrWhiteSpace(text))
            {
                return _lastTurnResult;
            }

            _scenarioState.LastUserInput = text;
            StartConversationTurn(
                () => _conversationManager.ProcessUserText(text),
                "Processing trainee input on the active backend.");
            return _lastTurnResult;
        }

        public SimulationConversationTurnResult RunEventTurn(string eventDescription)
        {
            InitializeIfNeeded();

            if (IsTurnInProgress || string.IsNullOrWhiteSpace(eventDescription))
            {
                return _lastTurnResult;
            }

            _scenarioState.LastEvent = eventDescription;
            StartConversationTurn(
                () => _conversationManager.ProcessSimulationEvent(eventDescription),
                "Processing simulation event on the active backend.");
            return _lastTurnResult;
        }

        public void ResetSession()
        {
            if (IsTurnInProgress)
            {
                return;
            }

            InitializeIfNeeded();

            _conversationManager.ClearHistory();
            _historySnapshot.Clear();
            _traceEntries.Clear();
            ClearPendingTraceEntries();
            _scenarioState.ResetForNewSession();
            _sessionStartedUtc = DateTime.UtcNow;
            _turnLifecycleState = SimulationTurnLifecycleState.Idle;
            _lastTurnError = string.Empty;
            _recoveryStatus = string.Empty;
            _loggerInitializationError = string.Empty;
            _lastTurnResult = new SimulationConversationTurnResult();
            _lastTurnResult.Success = true;
            _lastTurnResult.FinalAssistantResponse = "New session started.";
            _scenarioState.LastAssistantResponse = _lastTurnResult.FinalAssistantResponse;
            _scenarioState.AddAction("system", "reset", "Conversation history cleared.", GetElapsedSeconds());
            LogSessionEvent("reset", "{\"reason\":\"manual_reset\"}");
        }

        public bool AbandonActiveTurn()
        {
            if (!IsTurnInProgress)
            {
                return false;
            }

            _turnLifecycleState = SimulationTurnLifecycleState.Cancelling;

            var detachedOperation = new DetachedTurnOperation
            {
                Task = _pendingTurnTask,
                RunLogger = _runLogger,
                ActiveRuntimeResource = _activeRuntimeResource,
                FinalBackendName = _activeBackendName ?? string.Empty,
                FinalRuntimeMode = ResolveSelectedRuntimeMode().ToString(),
                TotalTurns = _startedTurnCount,
                SuccessfulTurns = _successfulTurnCount,
                FailedTurns = _failedTurnCount,
                LastError = "Turn was abandoned during diagnostics recovery."
            };

            _detachedTurnOperations.Add(detachedOperation);
            _cancelledTurnCount++;
            var cancelledTurnResult = CreateCancelledTurnResult(_pendingTurnDescription);
            _recoveryStatus =
                "Abandoned the in-flight turn and rebuilt the active runtime session. " +
                "The old backend is draining in the background.";

            ResetCurrentRuntimeStateForRecovery();
            InitializeIfNeeded();
            _lastTurnError = detachedOperation.LastError;
            _lastTurnResult = cancelledTurnResult;
            if (_scenarioState != null)
            {
                _scenarioState.LastAssistantResponse = _lastTurnResult.FinalAssistantResponse;
                _scenarioState.AddAction("system", "recovery", _recoveryStatus, GetElapsedSeconds());
            }

            LogSessionEvent("recovered", "{\"reason\":\"abandoned_turn\"}");
            _turnLifecycleState = SimulationTurnLifecycleState.Cancelled;
            return true;
        }

        public void Dispose()
        {
            _acceptToolExecutionRequests = false;
            FailPendingToolExecutionRequests("Tool execution dispatcher is shutting down.");
            EndRunLoggerSession("destroyed");
            DisposeRunLogger();
            DisposeRuntimeResource();
            DisposeDetachedTurnOperations();
        }

        private bool IsTurnInProgress
        {
            get { return _pendingTurnTask != null; }
        }

        private float GetElapsedSeconds()
        {
            return Math.Max(0f, (float)(DateTime.UtcNow - _sessionStartedUtc).TotalSeconds);
        }

        private string GetPendingTurnDescription()
        {
            return string.IsNullOrWhiteSpace(_pendingTurnDescription)
                ? "Waiting for the active backend."
                : _pendingTurnDescription;
        }

        private float GetPendingTurnElapsedSeconds()
        {
            if (!IsTurnInProgress)
            {
                return 0f;
            }

            return Math.Max(0f, (float)(DateTime.UtcNow - _pendingTurnStartedUtc).TotalSeconds);
        }

        private void ResetCurrentRuntimeStateForRecovery()
        {
            _conversationManager = null;
            _stateProvider = null;
            _scenarioState = null;
            _cactusBootstrapService = null;
            _runLogger = null;
            _activeRuntimeResource = null;
            _pendingTurnTask = null;
            _pendingTurnStartedUtc = default(DateTime);
            _pendingTurnDescription = string.Empty;
            _runtimeSummary = string.Empty;
            _activeBackendName = string.Empty;
            _activeModelSource = string.Empty;
            _activeModelReference = string.Empty;
            _runtimeCapabilities = new SimulationRuntimeCapabilities();
            _runtimeLifecycleState = SimulationRuntimeLifecycleState.Uninitialized;
            _loggerInitializationError = string.Empty;
            _historySnapshot.Clear();
            _traceEntries.Clear();
            ClearPendingTraceEntries();
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

        private Action<SimulationConversationTraceEntry> CreateTraceSink(int runtimeGeneration)
        {
            return delegate(SimulationConversationTraceEntry entry)
            {
                if (entry == null)
                {
                    return;
                }

                _pendingTraceEntries.Enqueue(new PendingTraceEntry
                {
                    RuntimeGeneration = runtimeGeneration,
                    Entry = entry
                });
            };
        }

        private SimulationRuntimeBootstrapStatus GetBootstrapStatus()
        {
            if (_cactusBootstrapService == null || _cactusBootstrapService.Status == null)
            {
                return EmptyBootstrapStatus;
            }

            return _cactusBootstrapService.Status;
        }

        private ISimulationRunLoggerDiagnostics GetLoggerDiagnostics()
        {
            return _runLogger as ISimulationRunLoggerDiagnostics;
        }

        private SimulationLoggerHealthState ResolveLoggerHealthState(ISimulationRunLoggerDiagnostics diagnostics)
        {
            if (!string.IsNullOrWhiteSpace(_loggerInitializationError))
            {
                return SimulationLoggerHealthState.Error;
            }

            if (_runLogger == null)
            {
                return _conversationManager == null
                    ? SimulationLoggerHealthState.Uninitialized
                    : SimulationLoggerHealthState.Error;
            }

            if (diagnostics == null)
            {
                return SimulationLoggerHealthState.Healthy;
            }

            if (diagnostics.FailureCount > 0)
            {
                return SimulationLoggerHealthState.Degraded;
            }

            return diagnostics.IsSessionStarted
                ? SimulationLoggerHealthState.Healthy
                : SimulationLoggerHealthState.Uninitialized;
        }

        private string GetLoggerLastError(ISimulationRunLoggerDiagnostics diagnostics)
        {
            if (!string.IsNullOrWhiteSpace(_loggerInitializationError))
            {
                return _loggerInitializationError;
            }

            return diagnostics == null ? string.Empty : (diagnostics.LastError ?? string.Empty);
        }

        private SimulationBackendHealthState ResolveBackendHealthState(
            SimulationRuntimeBootstrapStatus bootstrapStatus)
        {
            if (_turnLifecycleState == SimulationTurnLifecycleState.Cancelling || _detachedTurnOperations.Count > 0)
            {
                return SimulationBackendHealthState.Recovering;
            }

            if (_runtimeLifecycleState == SimulationRuntimeLifecycleState.Error)
            {
                return SimulationBackendHealthState.Error;
            }

            if (_runtimeLifecycleState == SimulationRuntimeLifecycleState.Fallback ||
                !string.IsNullOrWhiteSpace(_lastTurnError))
            {
                return SimulationBackendHealthState.Degraded;
            }

            if (_runtimeLifecycleState == SimulationRuntimeLifecycleState.Ready ||
                _turnLifecycleState == SimulationTurnLifecycleState.Running ||
                _turnLifecycleState == SimulationTurnLifecycleState.Succeeded)
            {
                return SimulationBackendHealthState.Healthy;
            }

            if (bootstrapStatus != null && bootstrapStatus.State == SimulationRuntimeBootstrapState.Error)
            {
                return SimulationBackendHealthState.Error;
            }

            return SimulationBackendHealthState.Unknown;
        }

        private string BuildBackendHealthSummary(SimulationRuntimeBootstrapStatus bootstrapStatus)
        {
            if (_turnLifecycleState == SimulationTurnLifecycleState.Cancelling)
            {
                return "Recovery is abandoning the active turn and rebuilding the runtime session.";
            }

            if (_detachedTurnOperations.Count > 0)
            {
                return _detachedTurnOperations.Count.ToString() +
                       " detached turn(s) are still draining after recovery.";
            }

            if (_runtimeLifecycleState == SimulationRuntimeLifecycleState.Error)
            {
                return FirstNonEmpty(
                    bootstrapStatus == null ? string.Empty : bootstrapStatus.Error,
                    _runtimeSummary,
                    _lastTurnError,
                    "Backend is in an error state.");
            }

            if (_runtimeLifecycleState == SimulationRuntimeLifecycleState.Fallback)
            {
                return FirstNonEmpty(_runtimeSummary, "Fallback backend is active.");
            }

            if (_turnLifecycleState == SimulationTurnLifecycleState.Running)
            {
                return FirstNonEmpty(_pendingTurnDescription, "Backend is processing a turn.");
            }

            if (!string.IsNullOrWhiteSpace(_lastTurnError))
            {
                return "Last turn failed: " + _lastTurnError;
            }

            if (bootstrapStatus != null && bootstrapStatus.State == SimulationRuntimeBootstrapState.Ready)
            {
                return FirstNonEmpty(
                    bootstrapStatus.HealthCheckResponse,
                    "Bootstrap health check passed.");
            }

            if (_runtimeCapabilities.UsesLiveModel)
            {
                return "Live backend is ready.";
            }

            if (_runtimeLifecycleState == SimulationRuntimeLifecycleState.Ready)
            {
                return "Scripted or fallback backend is ready.";
            }

            if (_runtimeLifecycleState == SimulationRuntimeLifecycleState.Loading)
            {
                return "Backend is initializing.";
            }

            return "Backend is not initialized.";
        }

        private ISimulationCompletionModel BuildCompletionModel()
        {
            DisposeRuntimeResource();
            _runtimeLifecycleState = SimulationRuntimeLifecycleState.Loading;
            _runtimeCapabilities = new SimulationRuntimeCapabilities();
            _activeBackendName = string.Empty;
            _runtimeSummary = string.Empty;
            _activeModelSource = string.Empty;
            _activeModelReference = string.Empty;

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
            if (_configuration.RequestedRuntimeMode != SimulationExampleRuntimeMode.Automatic)
            {
                return _configuration.RequestedRuntimeMode;
            }

            return _configuration.Platform == RuntimePlatform.Android
                ? SimulationExampleRuntimeMode.QuestCactusOnly
                : SimulationExampleRuntimeMode.DesktopGemmaWithScriptedFallback;
        }

        private ISimulationCompletionModel BuildDesktopGemmaModel(bool allowScriptedFallback)
        {
            try
            {
                var bridgeSettings = new DesktopGemmaBridgeSettings();
                bridgeSettings.PythonExecutablePath = _configuration.DesktopGemmaPythonExecutablePath;
                bridgeSettings.ModelIdentifierOrPath = _configuration.DesktopGemmaModelIdentifierOrPath;

                var bridgeModel = new DesktopGemmaBridgeCompletionModel(bridgeSettings);
                _activeRuntimeResource = bridgeModel;
                _activeBackendName = "Desktop Gemma";
                _activeModelSource = "desktop_gemma_bridge";
                _activeModelReference = SanitizeModelReference(bridgeModel.ResolvedModelIdentifierOrPath);
                _runtimeSummary =
                    bridgeModel.ReadySummary +
                    " This is the editor validation path, so speech transcription stays disabled here.";
                _runtimeCapabilities.SupportsTextCompletion = true;
                _runtimeCapabilities.SupportsToolCalling = true;
                _runtimeCapabilities.SupportsSpeechTranscription = false;
                _runtimeCapabilities.UsesLiveModel = true;
                _runtimeCapabilities.IsTargetRuntime = false;
                _runtimeLifecycleState = SimulationRuntimeLifecycleState.Ready;
                _scenarioState.AddAction("system", "runtime_ready", "Desktop Gemma bridge initialized.", GetElapsedSeconds());
                LogRuntimeEvent("backend_ready", false);
                return bridgeModel;
            }
            catch (Exception ex)
            {
                if (allowScriptedFallback)
                {
                    _scenarioState.AddAction("system", "runtime_fallback", ex.Message, GetElapsedSeconds());
                    var fallbackModel = BuildScriptedScenarioModel(
                        "Desktop Gemma was requested but could not start, so the scene fell back to scripted responses. Issue: " +
                        ex.Message,
                        false);
                    _runtimeLifecycleState = SimulationRuntimeLifecycleState.Fallback;
                    LogRuntimeEvent("backend_fallback", true);
                    return fallbackModel;
                }

                _activeBackendName = "Desktop Gemma (Unavailable)";
                _runtimeSummary = ex.Message;
                _runtimeCapabilities.SupportsTextCompletion = false;
                _runtimeCapabilities.SupportsToolCalling = false;
                _runtimeCapabilities.SupportsSpeechTranscription = false;
                _runtimeCapabilities.UsesLiveModel = false;
                _runtimeCapabilities.IsTargetRuntime = false;
                _runtimeLifecycleState = SimulationRuntimeLifecycleState.Error;
                _scenarioState.AddAction("system", "runtime_error", ex.Message, GetElapsedSeconds());
                LogRuntimeEvent("backend_error", false);
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
                _activeModelSource = _cactusBootstrapService.Status.ModelPathSource ?? string.Empty;
                _activeModelReference = SanitizeModelReference(_cactusBootstrapService.Status.ResolvedModelPath);
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
                _runtimeLifecycleState = SimulationRuntimeLifecycleState.Ready;
                _scenarioState.AddAction("system", "runtime_ready", "Quest Cactus runtime initialized.", GetElapsedSeconds());
                LogRuntimeEvent("backend_ready", false);
                return _cactusBootstrapService.CompletionModel;
            }

            var error = _cactusBootstrapService.Status == null || string.IsNullOrWhiteSpace(_cactusBootstrapService.Status.Error)
                ? "Quest Cactus runtime initialization failed."
                : _cactusBootstrapService.Status.Error;

            if (allowScriptedFallback)
            {
                _scenarioState.AddAction("system", "runtime_fallback", error, GetElapsedSeconds());
                var fallbackModel = BuildScriptedScenarioModel(
                    "Quest Cactus was requested but could not start, so the scene fell back to scripted responses. Issue: " +
                    error,
                    false);
                _runtimeLifecycleState = SimulationRuntimeLifecycleState.Fallback;
                LogRuntimeEvent("backend_fallback", true);
                return fallbackModel;
            }

            _activeBackendName = "Quest Cactus (Unavailable)";
            _runtimeSummary = error;
            _runtimeCapabilities.SupportsTextCompletion = false;
            _runtimeCapabilities.SupportsToolCalling = false;
            _runtimeCapabilities.SupportsSpeechTranscription = false;
            _runtimeCapabilities.UsesLiveModel = false;
            _runtimeCapabilities.IsTargetRuntime = true;
            _runtimeLifecycleState = SimulationRuntimeLifecycleState.Error;
            _scenarioState.AddAction("system", "runtime_error", error, GetElapsedSeconds());
            LogRuntimeEvent("backend_error", false);
            return new UnavailableSimulationCompletionModel(error);
        }

        private ISimulationCompletionModel BuildScriptedScenarioModel(string reason, bool logRuntimeReadyEvent = true)
        {
            _activeBackendName = "Scripted Scenario Model";
            _activeModelSource = "scripted_scenario";
            _activeModelReference = string.Empty;
            _runtimeSummary = reason;
            _runtimeCapabilities.SupportsTextCompletion = true;
            _runtimeCapabilities.SupportsToolCalling = true;
            _runtimeCapabilities.SupportsSpeechTranscription = false;
            _runtimeCapabilities.UsesLiveModel = false;
            _runtimeCapabilities.IsTargetRuntime = false;
            _runtimeLifecycleState = SimulationRuntimeLifecycleState.Ready;
            if (logRuntimeReadyEvent)
            {
                LogRuntimeEvent("backend_ready", false);
            }

            return new ScriptedScenarioCompletionModel(_scenarioState);
        }

        private SimulationRuntimeBootstrapOptions BuildCactusBootstrapOptions()
        {
            var options = new SimulationRuntimeBootstrapOptions();
            options.AppId = _configuration.AppId;
            options.TelemetryCachePath = ResolveTelemetryCachePath();
            options.LogLevel = CactusLogLevel.Info;
            options.ModelPathResolver = new ConfiguredCactusModelPathResolver
            {
                ExplicitModelPath = _configuration.CactusModelPathOverride,
                RelativeModelPath = _configuration.CactusModelRelativePath
            };
            return options;
        }

        private string ResolveTelemetryCachePath()
        {
            if (!string.IsNullOrWhiteSpace(_configuration.TelemetryCachePathOverride))
            {
                return ResolvePathAgainstWorkingDirectory(_configuration.TelemetryCachePathOverride);
            }

            var persistentDataPath = string.IsNullOrWhiteSpace(_configuration.PersistentDataPath)
                ? Application.persistentDataPath
                : _configuration.PersistentDataPath;
            return Path.Combine(persistentDataPath, "cactus-telemetry");
        }

        private string ResolvePathAgainstWorkingDirectory(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            if (Path.IsPathRooted(value))
            {
                return Path.GetFullPath(value);
            }

            var workingDirectory = string.IsNullOrWhiteSpace(_configuration.WorkingDirectoryPath)
                ? Directory.GetCurrentDirectory()
                : _configuration.WorkingDirectoryPath;
            return Path.GetFullPath(Path.Combine(workingDirectory, value));
        }

        private void InitializeRunLogger()
        {
            DisposeRunLogger();
            _loggerInitializationError = string.Empty;

            try
            {
                var loggerOptions = new LocalJsonlSimulationRunLoggerOptions();
                loggerOptions.RootPath = SimulationRunLogging.ResolveRunLogRootPath("simulation-runs");
                loggerOptions.SessionGroupPath = BuildRunLogGroupPath();
                loggerOptions.SessionPrefix = "debug-harness";
                loggerOptions.Verbosity = SimulationRunLogVerbosity.Compact;

                _runLogger = new LocalJsonlSimulationRunLogger(loggerOptions);
                _runLogger.StartSession(new SimulationRunSessionMetadata
                {
                    SessionLabel = "Simulation Conversation Debug Harness",
                    AppId = _configuration.AppId,
                    StartedAtUtc = _sessionStartedUtc.ToString("o"),
                    SceneName = _configuration.SceneName ?? string.Empty,
                    Platform = _configuration.Platform.ToString(),
                    RequestedRuntimeMode = _configuration.RequestedRuntimeMode.ToString(),
                    SelectedRuntimeMode = ResolveSelectedRuntimeMode().ToString(),
                    LoggerVerbosity = _runLogger.Verbosity.ToString()
                });
            }
            catch (Exception ex)
            {
                _runLogger = null;
                _loggerInitializationError = ex.Message;
                if (_scenarioState != null)
                {
                    _scenarioState.AddAction("system", "logger_error", ex.Message, GetElapsedSeconds());
                }
            }
        }

        private void EndRunLoggerSession(string endReason)
        {
            if (_runLogger == null)
            {
                return;
            }

            try
            {
                _runLogger.EndSession(new SimulationRunSessionSummary
                {
                    EndedAtUtc = DateTime.UtcNow.ToString("o"),
                    EndReason = endReason ?? string.Empty,
                    FinalBackendName = _activeBackendName ?? string.Empty,
                    FinalRuntimeMode = ResolveSelectedRuntimeMode().ToString(),
                    TotalTurns = _startedTurnCount,
                    SuccessfulTurns = _successfulTurnCount,
                    FailedTurns = _failedTurnCount,
                    LastError = _lastTurnError ?? string.Empty
                });
                _runLogger.Flush();
            }
            catch
            {
            }
        }

        private void DisposeRunLogger()
        {
            try
            {
                _runLogger?.Dispose();
            }
            catch
            {
            }
            finally
            {
                _runLogger = null;
            }
        }

        private void LogRuntimeEvent(string kind, bool isFallback)
        {
            if (_runLogger == null)
            {
                return;
            }

            var bootstrapStatus = GetBootstrapStatus();
            var payload = new SimulationRunRuntimeRecord
            {
                RequestedRuntimeMode = _configuration.RequestedRuntimeMode.ToString(),
                SelectedRuntimeMode = ResolveSelectedRuntimeMode().ToString(),
                ActiveBackendName = _activeBackendName ?? string.Empty,
                ModelSource = _activeModelSource ?? string.Empty,
                SafeModelReference = _activeModelReference ?? string.Empty,
                RuntimeSummary = _runtimeSummary ?? string.Empty,
                BootstrapState = bootstrapStatus == null ? string.Empty : bootstrapStatus.State.ToString(),
                BootstrapError = bootstrapStatus == null ? string.Empty : bootstrapStatus.Error,
                HealthCheckResponse = bootstrapStatus == null ? string.Empty : bootstrapStatus.HealthCheckResponse,
                SupportsTextCompletion = _runtimeCapabilities.SupportsTextCompletion,
                SupportsToolCalling = _runtimeCapabilities.SupportsToolCalling,
                SupportsSpeechTranscription = _runtimeCapabilities.SupportsSpeechTranscription,
                UsesLiveModel = _runtimeCapabilities.UsesLiveModel,
                IsTargetRuntime = _runtimeCapabilities.IsTargetRuntime,
                IsFallback = isFallback
            };

            try
            {
                _runLogger.LogEvent(new SimulationRunLogEvent
                {
                    Family = "runtime",
                    Kind = kind ?? string.Empty,
                    PayloadJson = payload.ToJson(_runLogger.Verbosity)
                });
            }
            catch
            {
            }
        }

        private void LogSessionEvent(string kind, string payloadJson)
        {
            if (_runLogger == null)
            {
                return;
            }

            try
            {
                _runLogger.LogEvent(new SimulationRunLogEvent
                {
                    Family = "session",
                    Kind = kind ?? string.Empty,
                    PayloadJson = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson
                });
            }
            catch
            {
            }
        }

        private void StartConversationTurn(
            Func<SimulationConversationTurnResult> turnExecutor,
            string description)
        {
            if (turnExecutor == null || IsTurnInProgress)
            {
                return;
            }

            _pendingTurnDescription = description ?? string.Empty;
            _pendingTurnStartedUtc = DateTime.UtcNow;
            _startedTurnCount++;
            _turnLifecycleState = SimulationTurnLifecycleState.Running;
            _pendingTurnTask = Task.Run(turnExecutor);
        }

        private void CompletePendingTurnIfReady()
        {
            if (_pendingTurnTask == null || !_pendingTurnTask.IsCompleted)
            {
                return;
            }

            SimulationConversationTurnResult completedResult;
            try
            {
                completedResult = _pendingTurnTask.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                completedResult = CreateFailedTurnResult(ex);
            }

            _pendingTurnTask = null;
            _pendingTurnStartedUtc = default(DateTime);
            _pendingTurnDescription = string.Empty;
            _lastTurnResult = completedResult ?? CreateFailedTurnResult(null);
            _lastTurnError = _lastTurnResult.Success ? string.Empty : (_lastTurnResult.Error ?? string.Empty);
            if (_lastTurnResult.Success)
            {
                _successfulTurnCount++;
                _turnLifecycleState = SimulationTurnLifecycleState.Succeeded;
            }
            else
            {
                _failedTurnCount++;
                _turnLifecycleState = SimulationTurnLifecycleState.Failed;
            }

            ApplyTurnResult(_lastTurnResult);
            RefreshHistorySnapshot();
        }

        private void RefreshHistorySnapshot()
        {
            _historySnapshot.Clear();

            var history = _conversationManager == null ? null : _conversationManager.History;
            if (history == null)
            {
                return;
            }

            for (var i = 0; i < history.Count; i++)
            {
                var message = history[i] ?? new ConversationMessage();
                _historySnapshot.Add(new ConversationMessage
                {
                    Role = message.Role ?? string.Empty,
                    Content = message.Content ?? string.Empty,
                    Images = message.Images == null ? Array.Empty<string>() : (string[])message.Images.Clone(),
                    Audio = message.Audio == null ? Array.Empty<string>() : (string[])message.Audio.Clone()
                });
            }
        }

        private ConversationMessage[] CloneHistorySnapshot()
        {
            if (_historySnapshot.Count == 0)
            {
                return Array.Empty<ConversationMessage>();
            }

            var result = new ConversationMessage[_historySnapshot.Count];
            for (var i = 0; i < _historySnapshot.Count; i++)
            {
                var message = _historySnapshot[i] ?? new ConversationMessage();
                result[i] = new ConversationMessage
                {
                    Role = message.Role ?? string.Empty,
                    Content = message.Content ?? string.Empty,
                    Images = message.Images == null ? Array.Empty<string>() : (string[])message.Images.Clone(),
                    Audio = message.Audio == null ? Array.Empty<string>() : (string[])message.Audio.Clone()
                };
            }

            return result;
        }

        private SimulationConversationTraceEntry[] CloneTraceEntriesSnapshot()
        {
            if (_traceEntries.Count == 0)
            {
                return Array.Empty<SimulationConversationTraceEntry>();
            }

            var result = new SimulationConversationTraceEntry[_traceEntries.Count];
            for (var i = 0; i < _traceEntries.Count; i++)
            {
                var entry = _traceEntries[i] ?? new SimulationConversationTraceEntry();
                result[i] = new SimulationConversationTraceEntry
                {
                    Kind = entry.Kind,
                    Content = entry.Content ?? string.Empty,
                    TimestampUtc = entry.TimestampUtc ?? string.Empty
                };
            }

            return result;
        }

        private SimulationKpiSnapshot CaptureCurrentKpiSnapshot()
        {
            if (_stateProvider == null)
            {
                return new SimulationKpiSnapshot();
            }

            try
            {
                var snapshot = _stateProvider.CaptureKpis();
                return snapshot == null ? new SimulationKpiSnapshot() : snapshot.Clone();
            }
            catch
            {
                return new SimulationKpiSnapshot();
            }
        }

        private ISimulationToolExecutor ResolveToolExecutor()
        {
            if (_toolExecutor == null)
            {
                _toolExecutor = new MainThreadSimulationToolExecutor(
                    _configuration.UnityThreadId,
                    EnqueueToolExecutionRequest,
                    IsToolExecutionDispatcherAvailable);
            }

            return _toolExecutor;
        }

        private bool IsToolExecutionDispatcherAvailable()
        {
            return _acceptToolExecutionRequests;
        }

        private void EnqueueToolExecutionRequest(MainThreadToolExecutionRequest request)
        {
            if (request == null)
            {
                return;
            }

            if (!_acceptToolExecutionRequests)
            {
                request.Complete(
                    SimulationToolResult.CreateError(
                        request.ToolName,
                        "Tool execution dispatcher is unavailable."));
                return;
            }

            _pendingToolExecutionRequests.Enqueue(request);
        }

        private void DrainPendingToolExecutionRequests()
        {
            MainThreadToolExecutionRequest request;
            while (_pendingToolExecutionRequests.TryDequeue(out request))
            {
                if (request == null)
                {
                    continue;
                }

                if (!_acceptToolExecutionRequests)
                {
                    request.Complete(
                        SimulationToolResult.CreateError(
                            request.ToolName,
                            "Tool execution dispatcher is unavailable."));
                    continue;
                }

                request.Complete(SimulationToolRegistry.ExecuteInline(request.Registry, request.Call));
            }
        }

        private void DrainPendingTraceEntries()
        {
            PendingTraceEntry pendingTraceEntry;
            while (_pendingTraceEntries.TryDequeue(out pendingTraceEntry))
            {
                if (pendingTraceEntry == null ||
                    pendingTraceEntry.Entry == null ||
                    pendingTraceEntry.RuntimeGeneration != _runtimeGeneration)
                {
                    continue;
                }

                _traceEntries.Add(pendingTraceEntry.Entry);
                if (_traceEntries.Count > 64)
                {
                    _traceEntries.RemoveAt(0);
                }
            }
        }

        private void ClearPendingTraceEntries()
        {
            PendingTraceEntry ignored;
            while (_pendingTraceEntries.TryDequeue(out ignored))
            {
            }
        }

        private void DrainDetachedTurnOperations()
        {
            for (var i = _detachedTurnOperations.Count - 1; i >= 0; i--)
            {
                var operation = _detachedTurnOperations[i];
                if (operation == null || operation.Task == null || !operation.Task.IsCompleted)
                {
                    continue;
                }

                try
                {
                    operation.Task.GetAwaiter().GetResult();
                }
                catch
                {
                }

                CleanupDetachedTurnOperation(operation);
                _detachedTurnOperations.RemoveAt(i);
            }
        }

        private void FailPendingToolExecutionRequests(string message)
        {
            MainThreadToolExecutionRequest request;
            while (_pendingToolExecutionRequests.TryDequeue(out request))
            {
                if (request == null)
                {
                    continue;
                }

                request.Complete(
                    SimulationToolResult.CreateError(
                        request.ToolName,
                        string.IsNullOrWhiteSpace(message)
                            ? "Tool execution dispatcher is unavailable."
                            : message));
            }
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
                _activeModelSource = string.Empty;
                _activeModelReference = string.Empty;
            }
        }

        private void CleanupDetachedTurnOperation(DetachedTurnOperation operation)
        {
            if (operation == null)
            {
                return;
            }

            try
            {
                operation.RunLogger?.EndSession(new SimulationRunSessionSummary
                {
                    EndedAtUtc = DateTime.UtcNow.ToString("o"),
                    EndReason = "abandoned_turn_recovery",
                    FinalBackendName = operation.FinalBackendName ?? string.Empty,
                    FinalRuntimeMode = operation.FinalRuntimeMode ?? string.Empty,
                    TotalTurns = operation.TotalTurns,
                    SuccessfulTurns = operation.SuccessfulTurns,
                    FailedTurns = operation.FailedTurns,
                    LastError = operation.LastError ?? string.Empty
                });
                operation.RunLogger?.Flush();
            }
            catch
            {
            }

            try
            {
                operation.RunLogger?.Dispose();
            }
            catch
            {
            }

            try
            {
                operation.ActiveRuntimeResource?.Dispose();
            }
            catch
            {
            }
        }

        private void DisposeDetachedTurnOperations()
        {
            for (var i = 0; i < _detachedTurnOperations.Count; i++)
            {
                CleanupDetachedTurnOperation(_detachedTurnOperations[i]);
            }

            _detachedTurnOperations.Clear();
        }

        private string BuildRunLogGroupPath()
        {
            var sceneName = string.IsNullOrWhiteSpace(_configuration.SceneName)
                ? "scene-unknown"
                : _configuration.SceneName;
            return Path.Combine(
                _configuration.AppId ?? "gemma-hackathon-debug-harness",
                _configuration.Platform.ToString(),
                sceneName);
        }

        private static SimulationConversationTurnResult CreateFailedTurnResult(Exception ex)
        {
            var result = new SimulationConversationTurnResult();
            result.Success = false;
            result.Error = ex == null
                ? "Conversation turn failed."
                : ex.GetType().Name + ": " + ex.Message;
            return result;
        }

        private static SimulationConversationTurnResult CreateCancelledTurnResult(string description)
        {
            var result = new SimulationConversationTurnResult();
            result.Success = false;
            result.Error = "Turn was abandoned during diagnostics recovery.";
            result.FinalAssistantResponse = string.IsNullOrWhiteSpace(description)
                ? "Turn abandoned."
                : "Turn abandoned. " + description;
            return result;
        }

        private static SimulationRuntimeCapabilities CloneRuntimeCapabilities(SimulationRuntimeCapabilities value)
        {
            var source = value ?? DefaultRuntimeCapabilities;
            return new SimulationRuntimeCapabilities
            {
                SupportsTextCompletion = source.SupportsTextCompletion,
                SupportsToolCalling = source.SupportsToolCalling,
                SupportsSpeechTranscription = source.SupportsSpeechTranscription,
                UsesLiveModel = source.UsesLiveModel,
                IsTargetRuntime = source.IsTargetRuntime
            };
        }

        private static SimulationRuntimeBootstrapStatus CloneBootstrapStatus(SimulationRuntimeBootstrapStatus value)
        {
            var source = value ?? EmptyBootstrapStatus;
            return new SimulationRuntimeBootstrapStatus
            {
                State = source.State,
                BackendName = source.BackendName ?? string.Empty,
                ModelPathSource = source.ModelPathSource ?? string.Empty,
                ResolvedModelPath = source.ResolvedModelPath ?? string.Empty,
                TelemetryCachePath = source.TelemetryCachePath ?? string.Empty,
                HealthCheckResponse = source.HealthCheckResponse ?? string.Empty,
                HealthCheckRawJson = source.HealthCheckRawJson ?? string.Empty,
                Error = source.Error ?? string.Empty
            };
        }

        private static SimulationConversationDiagnosticsTurnSummary CloneTurnSummary(
            SimulationConversationTurnResult result)
        {
            var source = result ?? new SimulationConversationTurnResult();
            var toolResults = source.ToolResults == null
                ? Array.Empty<SimulationToolResult>()
                : new SimulationToolResult[source.ToolResults.Count];

            if (source.ToolResults != null)
            {
                for (var i = 0; i < source.ToolResults.Count; i++)
                {
                    var toolResult = source.ToolResults[i] ?? new SimulationToolResult();
                    toolResults[i] = new SimulationToolResult
                    {
                        Name = toolResult.Name ?? string.Empty,
                        Content = toolResult.Content ?? string.Empty,
                        IsError = toolResult.IsError
                    };
                }
            }

            return new SimulationConversationDiagnosticsTurnSummary
            {
                Success = source.Success,
                Error = source.Error ?? string.Empty,
                FinalAssistantResponse = source.FinalAssistantResponse ?? string.Empty,
                ToolResults = toolResults
            };
        }

        private static SimulationChecklistItem[] CopyChecklistArray(IReadOnlyList<SimulationChecklistItem> items)
        {
            if (items == null || items.Count == 0)
            {
                return Array.Empty<SimulationChecklistItem>();
            }

            var result = new SimulationChecklistItem[items.Count];
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i] ?? new SimulationChecklistItem();
                result[i] = new SimulationChecklistItem
                {
                    Id = item.Id ?? string.Empty,
                    Label = item.Label ?? string.Empty,
                    Completed = item.Completed,
                    Notes = item.Notes ?? string.Empty
                };
            }

            return result;
        }

        private static SimulationActionRecord[] CopyActionArray(IReadOnlyList<SimulationActionRecord> actions)
        {
            if (actions == null || actions.Count == 0)
            {
                return Array.Empty<SimulationActionRecord>();
            }

            var result = new SimulationActionRecord[actions.Count];
            for (var i = 0; i < actions.Count; i++)
            {
                var action = actions[i] ?? new SimulationActionRecord();
                result[i] = new SimulationActionRecord
                {
                    Actor = action.Actor ?? string.Empty,
                    Verb = action.Verb ?? string.Empty,
                    Details = action.Details ?? string.Empty,
                    OccurredAtSeconds = action.OccurredAtSeconds
                };
            }

            return result;
        }

        private static string SafeValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(none)" : value;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
            {
                return string.Empty;
            }

            for (var i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                {
                    return values[i];
                }
            }

            return string.Empty;
        }

        private static string SanitizeModelReference(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            try
            {
                if (Path.IsPathRooted(value))
                {
                    var trimmed = value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    return Path.GetFileName(trimmed);
                }
            }
            catch
            {
            }

            return value.Trim();
        }

        private sealed class PendingTraceEntry
        {
            public int RuntimeGeneration;
            public SimulationConversationTraceEntry Entry;
        }

        private sealed class DetachedTurnOperation
        {
            public Task<SimulationConversationTurnResult> Task;
            public ISimulationRunLogger RunLogger;
            public IDisposable ActiveRuntimeResource;
            public string FinalBackendName = string.Empty;
            public string FinalRuntimeMode = string.Empty;
            public int TotalTurns;
            public int SuccessfulTurns;
            public int FailedTurns;
            public string LastError = string.Empty;
        }

        private sealed class MainThreadSimulationToolExecutor : ISimulationToolExecutor
        {
            private readonly int _unityThreadId;
            private readonly Action<MainThreadToolExecutionRequest> _enqueueRequest;
            private readonly Func<bool> _isDispatcherAvailable;

            public MainThreadSimulationToolExecutor(
                int unityThreadId,
                Action<MainThreadToolExecutionRequest> enqueueRequest,
                Func<bool> isDispatcherAvailable)
            {
                _unityThreadId = unityThreadId;
                _enqueueRequest = enqueueRequest;
                _isDispatcherAvailable = isDispatcherAvailable;
            }

            public SimulationToolResult Execute(SimulationToolRegistry registry, SimulationToolCall call)
            {
                if (Thread.CurrentThread.ManagedThreadId == _unityThreadId)
                {
                    return SimulationToolRegistry.ExecuteInline(registry, call);
                }

                if (_isDispatcherAvailable == null || !_isDispatcherAvailable())
                {
                    return SimulationToolResult.CreateError(
                        call == null ? string.Empty : call.Name,
                        "Tool execution dispatcher is unavailable.");
                }

                var request = new MainThreadToolExecutionRequest(registry, call);
                _enqueueRequest(request);
                return request.WaitForResult();
            }
        }

        private sealed class MainThreadToolExecutionRequest
        {
            private readonly TaskCompletionSource<SimulationToolResult> _completion =
                new TaskCompletionSource<SimulationToolResult>();

            public MainThreadToolExecutionRequest(SimulationToolRegistry registry, SimulationToolCall call)
            {
                Registry = registry;
                Call = CloneCall(call);
                ToolName = Call == null ? string.Empty : Call.Name;
            }

            public SimulationToolRegistry Registry { get; private set; }

            public SimulationToolCall Call { get; private set; }

            public string ToolName { get; private set; }

            public SimulationToolResult WaitForResult()
            {
                try
                {
                    return _completion.Task.GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    return SimulationToolResult.CreateError(
                        ToolName,
                        ex.GetType().Name + ": " + ex.Message);
                }
            }

            public void Complete(SimulationToolResult result)
            {
                _completion.TrySetResult(
                    result ?? SimulationToolResult.CreateError(ToolName, "Tool execution returned no result."));
            }

            private static SimulationToolCall CloneCall(SimulationToolCall call)
            {
                var value = call ?? new SimulationToolCall();
                return new SimulationToolCall
                {
                    Name = value.Name ?? string.Empty,
                    ArgumentsJson = string.IsNullOrWhiteSpace(value.ArgumentsJson) ? "{}" : value.ArgumentsJson
                };
            }
        }
    }
}
