using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace GemmaHackathon.SimulationFramework
{
    public sealed class DesktopGemmaBridgeSettings
    {
        public string PythonExecutablePath = string.Empty;
        public string ConfigPath = "config/local.json";
        public string PythonExecutableProperty = "paths.desktopPythonExecutable";
        public string ModelIdentifierOrPath = string.Empty;
        public string ModelIdentifierProperty = "paths.desktopGemmaTransformersModel";
        public string BridgeScriptPath = "ai/desktop_gemma_bridge.py";
        public int StartupTimeoutMs = 240000;
        public int RequestTimeoutMs = 240000;
    }

    public sealed class DesktopGemmaBridgeCompletionModel : ISimulationCompletionModel, IDisposable
    {
        private readonly DesktopGemmaBridgeSettings _settings;
        private readonly object _syncRoot = new object();
        private readonly StringBuilder _capturedErrorOutput = new StringBuilder(512);

        private Process _bridgeProcess;
        private StreamWriter _bridgeInput;
        private StreamReader _bridgeOutput;
        private bool _isDisposed;

        public DesktopGemmaBridgeCompletionModel(DesktopGemmaBridgeSettings settings = null)
        {
            _settings = settings ?? new DesktopGemmaBridgeSettings();
            EnsureBridgeIsRunning();
        }

        public string ResolvedPythonExecutable { get; private set; }

        public string ResolvedBridgeScriptPath { get; private set; }

        public string ResolvedModelIdentifierOrPath { get; private set; }

        public string ReadySummary { get; private set; }

        public void Reset()
        {
            lock (_syncRoot)
            {
                EnsureBridgeIsRunning();
                SendControlCommand("reset", false);
            }
        }

        public string CompleteJson(
            string messagesJson,
            string optionsJson = null,
            string toolsJson = null,
            Action<string, uint> tokenCallback = null,
            byte[] pcm16Mono = null,
            int responseBufferBytes = CactusNative.DefaultJsonBufferSize)
        {
            lock (_syncRoot)
            {
                EnsureBridgeIsRunning();

                var requestJson = BuildCompletionRequestJson(
                    messagesJson,
                    optionsJson,
                    toolsJson);

                return SendRequest(requestJson, "desktop Gemma completion");
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

            var repoRoot = ResolveRepoRoot();
            if (string.IsNullOrWhiteSpace(repoRoot))
            {
                throw new InvalidOperationException(
                    "Repo root could not be resolved for the desktop Gemma bridge.");
            }

            ResolvedPythonExecutable = ResolvePythonExecutable(repoRoot);
            ResolvedBridgeScriptPath = ResolveBridgeScriptPath(repoRoot);
            ResolvedModelIdentifierOrPath = ResolveModelIdentifierOrPath(repoRoot);

            if (string.IsNullOrWhiteSpace(ResolvedPythonExecutable))
            {
                throw new InvalidOperationException(
                    "Python executable could not be resolved for the desktop Gemma bridge.");
            }

            if (!File.Exists(ResolvedBridgeScriptPath))
            {
                throw new FileNotFoundException(
                    "Desktop Gemma bridge script was not found: " + ResolvedBridgeScriptPath,
                    ResolvedBridgeScriptPath);
            }

            var startInfo = new ProcessStartInfo();
            startInfo.FileName = ResolvedPythonExecutable;
            startInfo.Arguments =
                QuoteCommandLineArgument(ResolvedBridgeScriptPath) +
                " " +
                QuoteCommandLineArgument(ResolvedModelIdentifierOrPath);
            startInfo.WorkingDirectory = repoRoot;
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardInput = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.CreateNoWindow = true;

            var process = new Process();
            process.StartInfo = startInfo;
            process.EnableRaisingEvents = false;
            process.ErrorDataReceived += OnBridgeErrorDataReceived;

            try
            {
                if (!process.Start())
                {
                    throw new InvalidOperationException("Desktop Gemma bridge process did not start.");
                }
            }
            catch (Win32Exception ex)
            {
                throw new InvalidOperationException(
                    "Failed to start the desktop Gemma bridge. Check the configured Python executable. " +
                    "Resolved executable: " +
                    ResolvedPythonExecutable,
                    ex);
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
                    "Desktop Gemma bridge started but did not report readiness.");
            }

            var readyPayload = JsonDom.Parse(readyLine);
            if (readyPayload == null || readyPayload.Kind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    "Desktop Gemma bridge returned an invalid startup payload: " + readyLine);
            }

            var isReady = ReadBooleanProperty(readyPayload, "ready");
            var startupError = ReadStringProperty(readyPayload, "error");
            var modelIdentifier = ReadStringProperty(readyPayload, "model_identifier");
            var deviceName = ReadStringProperty(readyPayload, "device");

            if (!isReady)
            {
                throw new InvalidOperationException(
                    "Desktop Gemma bridge failed to initialize. " +
                    SafeValue(startupError) +
                    BuildCapturedErrorSuffix());
            }

            ReadySummary =
                "Desktop Gemma bridge ready. Model: " +
                SafeValue(modelIdentifier) +
                ". Device: " +
                SafeValue(deviceName) +
                ".";
        }

        private string SendRequest(string requestJson, string operationName)
        {
            ThrowIfDisposed();

            if (_bridgeProcess == null || _bridgeProcess.HasExited || _bridgeInput == null || _bridgeOutput == null)
            {
                throw new InvalidOperationException(
                    "Desktop Gemma bridge is not running." + BuildCapturedErrorSuffix());
            }

            try
            {
                _bridgeInput.WriteLine(requestJson);
                _bridgeInput.Flush();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Failed to send a request to the desktop Gemma bridge during " +
                    operationName +
                    "." +
                    BuildCapturedErrorSuffix(),
                    ex);
            }

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

        private void SendControlCommand(string commandName, bool throwOnFailure)
        {
            var responseLine = SendRequest(BuildControlRequestJson(commandName), commandName);
            var response = SimulationCompletionResponse.Parse(responseLine);
            if (!throwOnFailure)
            {
                return;
            }

            if (!response.ParseSucceeded || !response.Success)
            {
                throw new InvalidOperationException(
                    "Desktop Gemma bridge command failed: " +
                    commandName +
                    ". " +
                    SafeValue(response.Error));
            }
        }

        private void TryShutdownBridge()
        {
            try
            {
                if (_bridgeProcess == null || _bridgeProcess.HasExited)
                {
                    return;
                }

                var shutdownRequest = BuildControlRequestJson("shutdown");
                _bridgeInput.WriteLine(shutdownRequest);
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
            }
            catch
            {
            }

            try
            {
                if (!_bridgeProcess.HasExited)
                {
                    _bridgeProcess.Kill();
                }
            }
            catch
            {
            }

            try
            {
                _bridgeInput?.Dispose();
                _bridgeOutput?.Dispose();
                _bridgeProcess.Dispose();
            }
            catch
            {
            }
            finally
            {
                _bridgeProcess = null;
                _bridgeInput = null;
                _bridgeOutput = null;
            }
        }

        private void OnBridgeErrorDataReceived(object sender, DataReceivedEventArgs args)
        {
            if (string.IsNullOrWhiteSpace(args.Data))
            {
                return;
            }

            lock (_capturedErrorOutput)
            {
                if (_capturedErrorOutput.Length > 0)
                {
                    _capturedErrorOutput.AppendLine();
                }

                _capturedErrorOutput.Append(args.Data.Trim());
            }
        }

        private string ResolvePythonExecutable(string repoRoot)
        {
            if (!string.IsNullOrWhiteSpace(_settings.PythonExecutablePath))
            {
                return ResolvePathOrCommand(_settings.PythonExecutablePath, repoRoot);
            }

            JsonValue configRoot;
            if (TryLoadConfig(repoRoot, out configRoot))
            {
                var configuredPython = ReadConfigString(configRoot, _settings.PythonExecutableProperty);
                if (!string.IsNullOrWhiteSpace(configuredPython))
                {
                    return ResolvePathOrCommand(configuredPython, repoRoot);
                }
            }

            var windowsVirtualEnvironmentPython = Path.Combine(repoRoot, ".venv", "Scripts", "python.exe");
            if (File.Exists(windowsVirtualEnvironmentPython))
            {
                return windowsVirtualEnvironmentPython;
            }

            var posixVirtualEnvironmentPython = Path.Combine(repoRoot, ".venv", "bin", "python");
            if (File.Exists(posixVirtualEnvironmentPython))
            {
                return posixVirtualEnvironmentPython;
            }

            return Application.platform == RuntimePlatform.WindowsEditor ||
                   Application.platform == RuntimePlatform.WindowsPlayer
                ? "python"
                : "python3";
        }

        private string ResolveBridgeScriptPath(string repoRoot)
        {
            return ResolvePathOrCommand(_settings.BridgeScriptPath, repoRoot);
        }

        private string ResolveModelIdentifierOrPath(string repoRoot)
        {
            if (!string.IsNullOrWhiteSpace(_settings.ModelIdentifierOrPath))
            {
                return ResolveModelIdentifierValue(_settings.ModelIdentifierOrPath, repoRoot);
            }

            JsonValue configRoot;
            if (TryLoadConfig(repoRoot, out configRoot))
            {
                var configuredModelIdentifier = ReadConfigString(configRoot, _settings.ModelIdentifierProperty);
                if (!string.IsNullOrWhiteSpace(configuredModelIdentifier))
                {
                    return ResolveModelIdentifierValue(configuredModelIdentifier, repoRoot);
                }
            }

            return "google/gemma-4-E2B-it";
        }

        private bool TryLoadConfig(string repoRoot, out JsonValue configRoot)
        {
            configRoot = null;

            if (string.IsNullOrWhiteSpace(repoRoot))
            {
                return false;
            }

            var configPath = string.IsNullOrWhiteSpace(_settings.ConfigPath)
                ? Path.Combine(repoRoot, "config", "local.json")
                : ResolvePathOrCommand(_settings.ConfigPath, repoRoot);

            if (!File.Exists(configPath))
            {
                return false;
            }

            try
            {
                configRoot = JsonDom.Parse(File.ReadAllText(configPath));
                return configRoot != null && configRoot.Kind == JsonValueKind.Object;
            }
            catch
            {
                return false;
            }
        }

        private static string ReadConfigString(JsonValue root, string propertyPath)
        {
            if (root == null || root.Kind != JsonValueKind.Object || string.IsNullOrWhiteSpace(propertyPath))
            {
                return string.Empty;
            }

            JsonValue current = root;
            var segments = propertyPath.Split('.');
            for (var i = 0; i < segments.Length; i++)
            {
                JsonValue next;
                if (current == null || !current.TryGetProperty(segments[i], out next))
                {
                    return string.Empty;
                }

                current = next;
            }

            string value;
            return current != null && current.TryGetString(out value)
                ? value
                : string.Empty;
        }

        private static string ResolvePathOrCommand(string value, string repoRoot)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            if (Path.IsPathRooted(value))
            {
                return Path.GetFullPath(value);
            }

            if (!string.IsNullOrWhiteSpace(repoRoot))
            {
                var repoRelativeCandidate = Path.GetFullPath(Path.Combine(repoRoot, value));
                if (value.StartsWith(".", StringComparison.Ordinal) ||
                    value.Contains("\\") ||
                    value.Contains("/") ||
                    File.Exists(repoRelativeCandidate) ||
                    Directory.Exists(repoRelativeCandidate))
                {
                    return repoRelativeCandidate;
                }
            }

            return value;
        }

        private static string ResolveModelIdentifierValue(string value, string repoRoot)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            if (Path.IsPathRooted(value) || value.StartsWith(".", StringComparison.Ordinal) || value.Contains("\\"))
            {
                return ResolvePathOrCommand(value, repoRoot);
            }

            if (!string.IsNullOrWhiteSpace(repoRoot) && value.Contains("/"))
            {
                var repoRelativeCandidate = Path.GetFullPath(Path.Combine(repoRoot, value));
                if (File.Exists(repoRelativeCandidate) || Directory.Exists(repoRelativeCandidate))
                {
                    return repoRelativeCandidate;
                }
            }

            return value;
        }

        private static string ResolveRepoRoot()
        {
            var candidates = new List<string>();
            AddCandidate(candidates, Directory.GetCurrentDirectory());
            AddCandidate(candidates, AppDomain.CurrentDomain.BaseDirectory);

            if (!string.IsNullOrWhiteSpace(Application.dataPath))
            {
                AddCandidate(candidates, Application.dataPath);
                AddCandidate(candidates, Path.GetDirectoryName(Application.dataPath));
            }

            for (var i = 0; i < candidates.Count; i++)
            {
                var currentDirectory = new DirectoryInfo(candidates[i]);
                while (currentDirectory != null)
                {
                    if (Directory.Exists(Path.Combine(currentDirectory.FullName, "unity", "sim")) &&
                        File.Exists(Path.Combine(currentDirectory.FullName, "config", "local.example.json")))
                    {
                        return currentDirectory.FullName;
                    }

                    currentDirectory = currentDirectory.Parent;
                }
            }

            return string.Empty;
        }

        private static void AddCandidate(List<string> candidates, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            try
            {
                var fullPath = Path.GetFullPath(value);
                if (!candidates.Contains(fullPath))
                {
                    candidates.Add(fullPath);
                }
            }
            catch
            {
            }
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

        private static bool ReadBooleanProperty(JsonValue root, string propertyName)
        {
            JsonValue value;
            bool parsed;
            return root != null &&
                   root.TryGetProperty(propertyName, out value) &&
                   value != null &&
                   value.TryGetBoolean(out parsed) &&
                   parsed;
        }

        private static string ReadStringProperty(JsonValue root, string propertyName)
        {
            JsonValue value;
            string parsed;
            if (root != null &&
                root.TryGetProperty(propertyName, out value) &&
                value != null &&
                value.TryGetString(out parsed))
            {
                return parsed;
            }

            return string.Empty;
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

        private string BuildCapturedErrorSuffix()
        {
            var capturedErrors = ReadCapturedErrors();
            return string.IsNullOrWhiteSpace(capturedErrors)
                ? string.Empty
                : " Bridge stderr: " + capturedErrors;
        }

        private string ReadCapturedErrors()
        {
            lock (_capturedErrorOutput)
            {
                return _capturedErrorOutput.ToString().Trim();
            }
        }

        private void ClearCapturedErrors()
        {
            lock (_capturedErrorOutput)
            {
                _capturedErrorOutput.Length = 0;
            }
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
            return string.IsNullOrWhiteSpace(value) ? "(none)" : value;
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException("DesktopGemmaBridgeCompletionModel");
            }
        }
    }
}
