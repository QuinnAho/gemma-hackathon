using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using GemmaHackathon.SimulationFramework;

namespace GemmaHackathon.Backend.AssessmentCli
{
    internal sealed class DesktopGemmaNarrativeSettings
    {
        public string ConfigPath = "config/local.json";
        public string FallbackConfigPath = "config/local.example.json";
        public string PythonExecutableProperty = "paths.desktopPythonExecutable";
        public string ModelIdentifierProperty = "paths.desktopGemmaTransformersModel";
        public string DefaultPythonExecutable = ".venv\\Scripts\\python.exe";
        public string DefaultModelIdentifier = "google/gemma-4-E2B-it";
        public string BridgeScriptPath = "ai/desktop_gemma_bridge.py";
        public int StartupTimeoutMs = 240000;
        public int RequestTimeoutMs = 240000;
    }

    internal sealed class DesktopGemmaBridgeResponse
    {
        public bool Success;
        public string Error = string.Empty;
        public string Response = string.Empty;
        public string Backend = string.Empty;
        public string ModelIdentifier = string.Empty;
        public string Device = string.Empty;
        public string RawJson = string.Empty;
    }

    internal interface IDesktopGemmaNarrativeClient : IDisposable
    {
        DesktopGemmaBridgeResponse Complete(
            string messagesJson,
            string optionsJson = null,
            string toolsJson = "[]");
    }

    internal interface IDesktopGemmaNarrativeClientFactory
    {
        IDesktopGemmaNarrativeClient Create();
    }

    internal sealed class BackendDesktopGemmaBridgeClientFactory : IDesktopGemmaNarrativeClientFactory
    {
        public IDesktopGemmaNarrativeClient Create()
        {
            return new BackendDesktopGemmaBridgeClient();
        }
    }

    internal sealed class BackendDesktopGemmaBridgeClient : IDesktopGemmaNarrativeClient
    {
        private readonly DesktopGemmaNarrativeSettings _settings;
        private readonly object _syncRoot = new object();
        private readonly StringBuilder _capturedErrorOutput = new StringBuilder(512);

        private Process _bridgeProcess;
        private StreamWriter _bridgeInput;
        private StreamReader _bridgeOutput;
        private bool _isDisposed;

        public BackendDesktopGemmaBridgeClient(DesktopGemmaNarrativeSettings settings = null)
        {
            _settings = settings ?? new DesktopGemmaNarrativeSettings();
            EnsureBridgeIsRunning();
        }

        public string ResolvedPythonExecutable { get; private set; }

        public string ResolvedBridgeScriptPath { get; private set; }

        public string ResolvedModelIdentifierOrPath { get; private set; }

        public DesktopGemmaBridgeResponse Complete(
            string messagesJson,
            string optionsJson = null,
            string toolsJson = "[]")
        {
            lock (_syncRoot)
            {
                EnsureBridgeIsRunning();
                var responseJson = SendRequest(
                    BuildCompletionRequestJson(messagesJson, optionsJson, toolsJson),
                    "desktop Gemma narrative");
                return ParseCompletionResponse(responseJson);
            }
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                if (_isDisposed)
                {
                    return;
                }

                _isDisposed = true;
                TryShutdownBridge();
                DisposeBridgeProcess();
                GC.SuppressFinalize(this);
            }
        }

        private void EnsureBridgeIsRunning()
        {
            ThrowIfDisposed();

            if (_bridgeProcess != null && !_bridgeProcess.HasExited)
            {
                return;
            }

            StartBridgeProcess();
        }

        private void StartBridgeProcess()
        {
            DisposeBridgeProcess();
            ClearCapturedErrors();

            var repoRoot = RepositoryPaths.FindRepositoryRoot();
            ResolvedPythonExecutable = ResolvePathFromConfig(
                repoRoot,
                _settings.PythonExecutableProperty,
                _settings.DefaultPythonExecutable);
            ResolvedBridgeScriptPath = ResolveRepoPath(repoRoot, _settings.BridgeScriptPath);
            ResolvedModelIdentifierOrPath = ResolvePathFromConfig(
                repoRoot,
                _settings.ModelIdentifierProperty,
                _settings.DefaultModelIdentifier);

            if (string.IsNullOrWhiteSpace(ResolvedPythonExecutable))
            {
                throw new InvalidOperationException("Python executable could not be resolved for the desktop Gemma bridge.");
            }

            if (!File.Exists(ResolvedPythonExecutable))
            {
                throw new FileNotFoundException(
                    "Python executable was not found for the desktop Gemma bridge: " + ResolvedPythonExecutable,
                    ResolvedPythonExecutable);
            }

            if (!File.Exists(ResolvedBridgeScriptPath))
            {
                throw new FileNotFoundException(
                    "Desktop Gemma bridge script was not found: " + ResolvedBridgeScriptPath,
                    ResolvedBridgeScriptPath);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = ResolvedPythonExecutable,
                Arguments =
                    QuoteCommandLineArgument(ResolvedBridgeScriptPath) +
                    " " +
                    QuoteCommandLineArgument(ResolvedModelIdentifierOrPath),
                WorkingDirectory = repoRoot,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var process = new Process();
            process.StartInfo = startInfo;
            process.EnableRaisingEvents = false;
            process.ErrorDataReceived += OnBridgeErrorDataReceived;

            if (!process.Start())
            {
                throw new InvalidOperationException("Desktop Gemma bridge process did not start.");
            }

            process.BeginErrorReadLine();

            _bridgeProcess = process;
            _bridgeInput = process.StandardInput;
            _bridgeOutput = process.StandardOutput;

            var readyLine = ReadLineWithTimeout(
                _bridgeOutput,
                _settings.StartupTimeoutMs,
                "desktop Gemma bridge startup");

            if (string.IsNullOrWhiteSpace(readyLine))
            {
                throw new InvalidOperationException(
                    "Desktop Gemma bridge started but did not report readiness." +
                    BuildCapturedErrorSuffix());
            }

            using (var document = JsonDocument.Parse(readyLine))
            {
                var root = document.RootElement;
                var ready = ReadBooleanProperty(root, "ready");
                if (!ready)
                {
                    throw new InvalidOperationException(
                        "Desktop Gemma bridge failed to initialize. " +
                        SafeValue(ReadStringProperty(root, "error")) +
                        BuildCapturedErrorSuffix());
                }
            }
        }

        private string SendRequest(string requestJson, string operationName)
        {
            ThrowIfDisposed();

            if (_bridgeProcess == null || _bridgeProcess.HasExited || _bridgeInput == null || _bridgeOutput == null)
            {
                throw new InvalidOperationException(
                    "Desktop Gemma bridge is not running." +
                    BuildCapturedErrorSuffix());
            }

            _bridgeInput.WriteLine(requestJson);
            _bridgeInput.Flush();

            var responseLine = ReadLineWithTimeout(_bridgeOutput, _settings.RequestTimeoutMs, operationName);
            if (string.IsNullOrWhiteSpace(responseLine))
            {
                throw new InvalidOperationException(
                    "Desktop Gemma bridge returned an empty response during " +
                    operationName +
                    "." +
                    BuildCapturedErrorSuffix());
            }

            return responseLine;
        }

        private static DesktopGemmaBridgeResponse ParseCompletionResponse(string responseJson)
        {
            var response = new DesktopGemmaBridgeResponse
            {
                RawJson = responseJson ?? string.Empty
            };

            using (var document = JsonDocument.Parse(responseJson ?? "{}"))
            {
                var root = document.RootElement;
                response.Success = ReadBooleanProperty(root, "success");
                response.Error = ReadStringProperty(root, "error");
                response.Response = ReadStringProperty(root, "response");
                response.Backend = ReadStringProperty(root, "backend");
                response.ModelIdentifier = ReadStringProperty(root, "model_identifier");
                response.Device = ReadStringProperty(root, "device");
            }

            return response;
        }

        private void TryShutdownBridge()
        {
            try
            {
                if (_bridgeProcess == null || _bridgeProcess.HasExited || _bridgeInput == null)
                {
                    return;
                }

                _bridgeInput.WriteLine(BuildControlRequestJson("shutdown"));
                _bridgeInput.Flush();
            }
            catch
            {
            }
        }

        private void DisposeBridgeProcess()
        {
            if (_bridgeProcess == null)
            {
                return;
            }

            try
            {
                _bridgeProcess.ErrorDataReceived -= OnBridgeErrorDataReceived;
                if (!_bridgeProcess.HasExited)
                {
                    _bridgeProcess.Kill();
                }
            }
            catch
            {
            }
            finally
            {
                _bridgeInput?.Dispose();
                _bridgeOutput?.Dispose();
                _bridgeProcess.Dispose();
                _bridgeProcess = null;
                _bridgeInput = null;
                _bridgeOutput = null;
            }
        }

        private string ResolvePathFromConfig(string repoRoot, string propertyPath, string fallbackValue)
        {
            var configValue = ReadConfigValue(repoRoot, propertyPath);
            if (string.IsNullOrWhiteSpace(configValue))
            {
                configValue = fallbackValue ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(configValue))
            {
                return string.Empty;
            }

            if (LooksLikeModelIdentifier(configValue))
            {
                return configValue.Trim();
            }

            return ResolveRepoPath(repoRoot, configValue);
        }

        private string ResolveRepoPath(string repoRoot, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            return Path.IsPathRooted(path)
                ? Path.GetFullPath(path.Trim())
                : Path.GetFullPath(Path.Combine(repoRoot, path.Trim()));
        }

        private string ReadConfigValue(string repoRoot, string propertyPath)
        {
            var configPaths = new[]
            {
                ResolveRepoPath(repoRoot, _settings.ConfigPath),
                ResolveRepoPath(repoRoot, _settings.FallbackConfigPath)
            };

            for (var i = 0; i < configPaths.Length; i++)
            {
                var configPath = configPaths[i];
                if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
                {
                    continue;
                }

                try
                {
                    using (var document = JsonDocument.Parse(File.ReadAllText(configPath)))
                    {
                        return ReadNestedString(document.RootElement, propertyPath);
                    }
                }
                catch
                {
                }
            }

            return string.Empty;
        }

        private static string ReadNestedString(JsonElement element, string propertyPath)
        {
            if (string.IsNullOrWhiteSpace(propertyPath))
            {
                return string.Empty;
            }

            var current = element;
            var segments = propertyPath.Split('.');
            for (var i = 0; i < segments.Length; i++)
            {
                JsonElement next;
                if (!current.TryGetProperty(segments[i], out next))
                {
                    return string.Empty;
                }

                current = next;
            }

            return current.ValueKind == JsonValueKind.String
                ? (current.GetString() ?? string.Empty)
                : string.Empty;
        }

        private static bool LooksLikeModelIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (Path.IsPathRooted(value))
            {
                return false;
            }

            return value.IndexOf('/') >= 0 || value.IndexOf('\\') < 0;
        }

        private void OnBridgeErrorDataReceived(object sender, DataReceivedEventArgs args)
        {
            if (args == null || string.IsNullOrWhiteSpace(args.Data))
            {
                return;
            }

            lock (_capturedErrorOutput)
            {
                if (_capturedErrorOutput.Length > 0)
                {
                    _capturedErrorOutput.Append(" | ");
                }

                _capturedErrorOutput.Append(args.Data.Trim());
            }
        }

        private string BuildCapturedErrorSuffix()
        {
            lock (_capturedErrorOutput)
            {
                return _capturedErrorOutput.Length == 0
                    ? string.Empty
                    : " Bridge stderr: " + _capturedErrorOutput.ToString().Trim();
            }
        }

        private void ClearCapturedErrors()
        {
            lock (_capturedErrorOutput)
            {
                _capturedErrorOutput.Length = 0;
            }
        }

        private static string BuildCompletionRequestJson(
            string messagesJson,
            string optionsJson,
            string toolsJson)
        {
            var builder = new StringBuilder(512);
            builder.Append('{');
            JsonText.AppendStringProperty(builder, "command", "complete");
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "messages_json", messagesJson ?? "[]");
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "options_json", optionsJson ?? "{}");
            builder.Append(',');
            JsonText.AppendStringProperty(builder, "tools_json", toolsJson ?? "[]");
            builder.Append('}');
            return builder.ToString();
        }

        private static string BuildControlRequestJson(string commandName)
        {
            var builder = new StringBuilder(64);
            builder.Append('{');
            JsonText.AppendStringProperty(builder, "command", commandName ?? string.Empty);
            builder.Append('}');
            return builder.ToString();
        }

        private static string ReadLineWithTimeout(StreamReader reader, int timeoutMs, string operationName)
        {
            var readTask = Task.Run(() => reader.ReadLine());
            if (!readTask.Wait(timeoutMs))
            {
                throw new TimeoutException(
                    "Timed out while waiting for the " +
                    operationName +
                    " response from the desktop Gemma bridge.");
            }

            return readTask.Result ?? string.Empty;
        }

        private static bool ReadBooleanProperty(JsonElement root, string propertyName)
        {
            JsonElement value;
            return root.TryGetProperty(propertyName, out value) &&
                   value.ValueKind == JsonValueKind.True;
        }

        private static string ReadStringProperty(JsonElement root, string propertyName)
        {
            JsonElement value;
            if (!root.TryGetProperty(propertyName, out value) || value.ValueKind != JsonValueKind.String)
            {
                return string.Empty;
            }

            return value.GetString() ?? string.Empty;
        }

        private static string QuoteCommandLineArgument(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "\"\"";
            }

            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static string SafeValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(none)" : value.Trim();
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException("BackendDesktopGemmaBridgeClient");
            }
        }
    }
}
