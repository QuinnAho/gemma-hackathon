using System.IO;
using System.Threading;
using GemmaHackathon.SimulationFramework;
using UnityEngine;
using UnityEngine.Serialization;

namespace GemmaHackathon.SimulationTooling.DebugHarness
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
    public sealed class SimulationConversationDebugManager : MonoBehaviour
    {
        [SerializeField] private bool _createOverlayIfMissing = true;
        [SerializeField] private bool _overlayVisible = true;
        [SerializeField] [TextArea(1, 3)] private string _defaultCustomInput = "Give me the current office fire readiness summary.";
        [SerializeField] private SimulationExampleRuntimeMode _runtimeMode = SimulationExampleRuntimeMode.Automatic;
        [SerializeField] private string _desktopGemmaModelIdentifierOrPath = string.Empty;
        [SerializeField] private string _desktopGemmaPythonExecutablePath = string.Empty;
        [FormerlySerializedAs("_localModelPathOverride")]
        [SerializeField] private string _cactusModelPathOverride = string.Empty;
        [FormerlySerializedAs("_localModelRelativePath")]
        [SerializeField] private string _cactusModelRelativePath = "gemma-4-e2b";
        [SerializeField] private string _telemetryCachePathOverride = string.Empty;

        private string _customInput = string.Empty;
        private SimulationConversationDebugRuntimeController _runtimeController;

        private void Awake()
        {
            EnsureRuntimeController();
            _runtimeController.InitializeIfNeeded();
            EnsureOverlayIfNeeded();
        }

        private void Update()
        {
            _runtimeController?.Tick();
        }

        private void OnDestroy()
        {
            try
            {
                _runtimeController?.Dispose();
            }
            finally
            {
                _runtimeController = null;
            }
        }

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(_defaultCustomInput))
            {
                _defaultCustomInput = "Give me the current office fire readiness summary.";
            }

            if (string.IsNullOrWhiteSpace(_cactusModelRelativePath))
            {
                _cactusModelRelativePath = "gemma-4-e2b";
            }

            if (string.IsNullOrWhiteSpace(_customInput))
            {
                _customInput = _defaultCustomInput;
            }
        }

        public bool OverlayVisible
        {
            get { return _overlayVisible; }
            set { _overlayVisible = value; }
        }

        public string CustomInput
        {
            get { return _customInput; }
            set { _customInput = value ?? string.Empty; }
        }

        public SimulationConversationDiagnosticsSnapshot CaptureDiagnosticsSnapshot()
        {
            EnsureRuntimeController();
            return _runtimeController.CaptureDiagnosticsSnapshot();
        }

        public SimulationConversationTurnResult RunUserTurn(string text)
        {
            EnsureRuntimeController();
            return _runtimeController.RunUserTurn(text);
        }

        public SimulationConversationTurnResult RunEventTurn(string eventDescription)
        {
            EnsureRuntimeController();
            return _runtimeController.RunEventTurn(eventDescription);
        }

        public SimulationConversationTurnResult RunParticipantAction(string actionCode, string source = "ui")
        {
            EnsureRuntimeController();
            return _runtimeController.RunParticipantAction(new ParticipantAction
            {
                ActionCode = actionCode ?? string.Empty,
                Source = source ?? string.Empty
            });
        }

        public void ResetSession()
        {
            EnsureRuntimeController();
            _runtimeController.ResetSession();
            _customInput = _defaultCustomInput;
        }

        public bool AbandonActiveTurn()
        {
            EnsureRuntimeController();
            var abandoned = _runtimeController.AbandonActiveTurn();
            if (abandoned)
            {
                _customInput = _defaultCustomInput;
            }

            return abandoned;
        }

        private void EnsureRuntimeController()
        {
            if (_runtimeController != null)
            {
                return;
            }

            _runtimeController = new SimulationConversationDebugRuntimeController(BuildRuntimeConfiguration());
            if (string.IsNullOrWhiteSpace(_customInput))
            {
                _customInput = _defaultCustomInput;
            }
        }

        private SimulationConversationDebugRuntimeConfiguration BuildRuntimeConfiguration()
        {
            return new SimulationConversationDebugRuntimeConfiguration
            {
                AppId = "gemma-hackathon-debug-harness",
                SceneName = gameObject == null ? string.Empty : gameObject.scene.name,
                Platform = Application.platform,
                UnityThreadId = Thread.CurrentThread.ManagedThreadId,
                WorkingDirectoryPath = Directory.GetCurrentDirectory(),
                PersistentDataPath = Application.persistentDataPath,
                RequestedRuntimeMode = _runtimeMode,
                DesktopGemmaModelIdentifierOrPath = _desktopGemmaModelIdentifierOrPath,
                DesktopGemmaPythonExecutablePath = _desktopGemmaPythonExecutablePath,
                CactusModelPathOverride = _cactusModelPathOverride,
                CactusModelRelativePath = _cactusModelRelativePath,
                TelemetryCachePathOverride = _telemetryCachePathOverride
            };
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
    }
}
