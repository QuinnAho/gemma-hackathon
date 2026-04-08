using System;
using GemmaHackathon.SimulationFramework;

namespace GemmaHackathon.SimulationExamples
{
    public enum SimulationRuntimeLifecycleState
    {
        Uninitialized,
        Loading,
        Ready,
        Fallback,
        Error
    }

    public enum SimulationBackendHealthState
    {
        Unknown,
        Healthy,
        Degraded,
        Recovering,
        Error
    }

    public enum SimulationLoggerHealthState
    {
        Uninitialized,
        Healthy,
        Degraded,
        Error
    }

    public enum SimulationTurnLifecycleState
    {
        Idle,
        Running,
        Cancelling,
        Cancelled,
        Succeeded,
        Failed
    }

    [Serializable]
    public sealed class SimulationConversationDiagnosticsTurnSummary
    {
        public bool Success;
        public string Error = string.Empty;
        public string FinalAssistantResponse = string.Empty;
        public SimulationToolResult[] ToolResults = Array.Empty<SimulationToolResult>();
    }

    [Serializable]
    public sealed class SimulationConversationDiagnosticsSnapshot
    {
        public SimulationRuntimeLifecycleState RuntimeState = SimulationRuntimeLifecycleState.Uninitialized;
        public SimulationTurnLifecycleState TurnState = SimulationTurnLifecycleState.Idle;
        public string RequestedRuntimeMode = string.Empty;
        public string SelectedRuntimeMode = string.Empty;
        public string ActiveBackendName = string.Empty;
        public string RuntimeSummary = string.Empty;
        public SimulationBackendHealthState BackendHealth = SimulationBackendHealthState.Unknown;
        public string BackendHealthSummary = string.Empty;
        public string ModelSource = string.Empty;
        public string ModelReference = string.Empty;
        public SimulationRuntimeCapabilities RuntimeCapabilities = new SimulationRuntimeCapabilities();
        public SimulationRuntimeBootstrapStatus BootstrapStatus = new SimulationRuntimeBootstrapStatus();
        public SimulationLoggerHealthState LoggerHealth = SimulationLoggerHealthState.Uninitialized;
        public string LoggerSessionId = string.Empty;
        public string LoggerVerbosity = string.Empty;
        public string LoggerSessionDirectory = string.Empty;
        public string LoggerEventsPath = string.Empty;
        public string LoggerManifestPath = string.Empty;
        public int LoggerWrittenEventCount;
        public int LoggerFailureCount;
        public string LoggerLastError = string.Empty;
        public float SessionElapsedSeconds;
        public int StartedTurnCount;
        public int SuccessfulTurnCount;
        public int FailedTurnCount;
        public int CancelledTurnCount;
        public int DetachedTurnCount;
        public string LastTurnError = string.Empty;
        public string RecoveryStatus = string.Empty;
        public bool CanAbandonTurn;
        public string PendingTurnDescription = string.Empty;
        public float PendingTurnElapsedSeconds;
        public string CurrentPhase = string.Empty;
        public int CurrentScore;
        public string LastUserInput = string.Empty;
        public string LastEvent = string.Empty;
        public string LastDecision = string.Empty;
        public string LastAssistantResponse = string.Empty;
        public SimulationKpiSnapshot KpiSnapshot = new SimulationKpiSnapshot();
        public SimulationConversationDiagnosticsTurnSummary LastTurn =
            new SimulationConversationDiagnosticsTurnSummary();
        public ConversationMessage[] History = Array.Empty<ConversationMessage>();
        public SimulationConversationTraceEntry[] TraceEntries = Array.Empty<SimulationConversationTraceEntry>();
        public SimulationChecklistItem[] Checklist = Array.Empty<SimulationChecklistItem>();
        public SimulationActionRecord[] Actions = Array.Empty<SimulationActionRecord>();
    }
}
