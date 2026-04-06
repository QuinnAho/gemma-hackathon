using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GemmaHackathon.SimulationFramework
{
    public enum SimulationRuntimeBootstrapState
    {
        Uninitialized,
        Initializing,
        Ready,
        Error
    }

    [Serializable]
    public sealed class SimulationModelPathResolution
    {
        public bool Success;
        public string ModelPath = string.Empty;
        public string Source = string.Empty;
        public string Error = string.Empty;

        public static SimulationModelPathResolution CreateSuccess(string modelPath, string source)
        {
            return new SimulationModelPathResolution
            {
                Success = true,
                ModelPath = modelPath ?? string.Empty,
                Source = source ?? string.Empty,
                Error = string.Empty
            };
        }

        public static SimulationModelPathResolution CreateFailure(string error)
        {
            return new SimulationModelPathResolution
            {
                Success = false,
                ModelPath = string.Empty,
                Source = string.Empty,
                Error = error ?? "Model path could not be resolved."
            };
        }
    }

    public interface ISimulationModelPathResolver
    {
        SimulationModelPathResolution ResolveModelPath();
    }

    [Serializable]
    public sealed class SimulationRuntimeBootstrapStatus
    {
        public SimulationRuntimeBootstrapState State = SimulationRuntimeBootstrapState.Uninitialized;
        public string BackendName = string.Empty;
        public string ModelPathSource = string.Empty;
        public string ResolvedModelPath = string.Empty;
        public string TelemetryCachePath = string.Empty;
        public string HealthCheckResponse = string.Empty;
        public string HealthCheckRawJson = string.Empty;
        public string Error = string.Empty;

        public bool IsReady
        {
            get { return State == SimulationRuntimeBootstrapState.Ready; }
        }
    }

    public sealed class SimulationRuntimeBootstrapOptions
    {
        public string AppId = "gemma-hackathon";
        public string TelemetryCachePath = string.Empty;
        public string TelemetryVersion = string.Empty;
        public CactusLogLevel LogLevel = CactusLogLevel.Info;
        public string HealthCheckSystemPrompt = "Reply using plain text only.";
        public string HealthCheckPrompt = "Respond with exactly: BOOTSTRAP_READY";
        public string HealthCheckOptionsJson = SimulationConversationManagerOptions.DefaultCompletionOptionsJson;
        public int HealthCheckResponseBufferBytes = CactusNative.DefaultJsonBufferSize;
        public string CorpusDirectory = string.Empty;
        public bool CacheIndex = true;
        public ISimulationModelPathResolver ModelPathResolver;
    }

    public sealed class ConfiguredCactusModelPathResolver : ISimulationModelPathResolver
    {
        public string ExplicitModelPath = string.Empty;
        public string ConfigPath = "config/local.json";
        public string DirectModelPathProperty = "paths.desktopCactusModelPath";
        public string ModelsRootProperty = "paths.desktopModelsRoot";
        public string RelativeModelPath = "gemma-4-e2b";

        public SimulationModelPathResolution ResolveModelPath()
        {
            var repoRoot = ResolveRepoRoot();
            if (!string.IsNullOrWhiteSpace(ExplicitModelPath))
            {
                return ResolveCandidatePath(ExplicitModelPath, repoRoot, "serialized override");
            }

            JsonValue configRoot;
            string configError;
            if (TryLoadConfigRoot(repoRoot, ConfigPath, out configRoot, out configError))
            {
                var directModelPath = ReadConfigString(configRoot, DirectModelPathProperty);
                if (!string.IsNullOrWhiteSpace(directModelPath))
                {
                    return ResolveCandidatePath(
                        directModelPath,
                        repoRoot,
                        "config/local.json:" + DirectModelPathProperty);
                }

                var modelsRoot = ReadConfigString(configRoot, ModelsRootProperty);
                if (!string.IsNullOrWhiteSpace(modelsRoot))
                {
                    var combinedPath = string.IsNullOrWhiteSpace(RelativeModelPath)
                        ? modelsRoot
                        : Path.Combine(modelsRoot, RelativeModelPath);

                    return ResolveCandidatePath(
                        combinedPath,
                        repoRoot,
                        "config/local.json:" + ModelsRootProperty);
                }
            }
            else if (!string.IsNullOrWhiteSpace(configError))
            {
                return SimulationModelPathResolution.CreateFailure(configError);
            }

            if (string.IsNullOrWhiteSpace(repoRoot))
            {
                return SimulationModelPathResolution.CreateFailure(
                    "Repo root could not be resolved for local model lookup. Set an explicit model path on the scene manager.");
            }

            var fallbackPath = Path.Combine(repoRoot, ".local", "models", RelativeModelPath ?? string.Empty);
            return SimulationModelPathResolution.CreateSuccess(
                Path.GetFullPath(fallbackPath),
                "repo fallback");
        }

        private static SimulationModelPathResolution ResolveCandidatePath(string candidatePath, string repoRoot, string source)
        {
            if (string.IsNullOrWhiteSpace(candidatePath))
            {
                return SimulationModelPathResolution.CreateFailure("Resolved model path was empty.");
            }

            var basePath = string.IsNullOrWhiteSpace(repoRoot)
                ? Directory.GetCurrentDirectory()
                : repoRoot;

            var resolvedPath = Path.IsPathRooted(candidatePath)
                ? Path.GetFullPath(candidatePath)
                : Path.GetFullPath(Path.Combine(basePath, candidatePath));

            return SimulationModelPathResolution.CreateSuccess(resolvedPath, source);
        }

        private static bool TryLoadConfigRoot(string repoRoot, string configPathValue, out JsonValue root, out string error)
        {
            root = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(repoRoot))
            {
                return false;
            }

            var configPath = string.IsNullOrWhiteSpace(configPathValue)
                ? Path.Combine(repoRoot, "config", "local.json")
                : ResolveConfigPath(repoRoot, configPathValue);
            if (!File.Exists(configPath))
            {
                return false;
            }

            try
            {
                root = JsonDom.Parse(File.ReadAllText(configPath));
                if (root == null || root.Kind != JsonValueKind.Object)
                {
                    error = "config/local.json must contain a JSON object.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = "Failed to parse config/local.json: " + ex.Message;
                return false;
            }
        }

        private static string ResolveConfigPath(string repoRoot, string configPathValue)
        {
            if (string.IsNullOrWhiteSpace(configPathValue))
            {
                return Path.Combine(repoRoot, "config", "local.json");
            }

            return Path.IsPathRooted(configPathValue)
                ? Path.GetFullPath(configPathValue)
                : Path.GetFullPath(Path.Combine(repoRoot, configPathValue));
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
                if (current == null || current.Kind != JsonValueKind.Object)
                {
                    return string.Empty;
                }

                JsonValue next;
                if (!current.TryGetProperty(segments[i], out next))
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

        private static string ResolveRepoRoot()
        {
            var candidates = new List<string>();
            AddCandidate(candidates, Directory.GetCurrentDirectory());
            AddCandidate(candidates, AppDomain.CurrentDomain.BaseDirectory);

            if (!string.IsNullOrWhiteSpace(Application.dataPath))
            {
                AddCandidate(candidates, Application.dataPath);
                var dataDirectory = Path.GetDirectoryName(Application.dataPath);
                AddCandidate(candidates, dataDirectory);
            }

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                var current = string.IsNullOrWhiteSpace(candidate)
                    ? null
                    : new DirectoryInfo(candidate);

                while (current != null)
                {
                    if (LooksLikeRepoRoot(current.FullName))
                    {
                        return current.FullName;
                    }

                    current = current.Parent;
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

        private static bool LooksLikeRepoRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            return Directory.Exists(Path.Combine(path, "unity", "sim")) &&
                   File.Exists(Path.Combine(path, "config", "local.example.json"));
        }
    }

    public sealed class SimulationRuntimeBootstrapService : IDisposable
    {
        private readonly SimulationRuntimeBootstrapOptions _options;
        private readonly SimulationRuntimeBootstrapStatus _status = new SimulationRuntimeBootstrapStatus();
        private CactusModel _model;

        public SimulationRuntimeBootstrapService(SimulationRuntimeBootstrapOptions options = null)
        {
            _options = options ?? new SimulationRuntimeBootstrapOptions();
            _status.BackendName = "Local Cactus";
        }

        public SimulationRuntimeBootstrapStatus Status
        {
            get { return _status; }
        }

        public ISimulationCompletionModel CompletionModel
        {
            get { return _model; }
        }

        public bool Initialize()
        {
            if (_status.State == SimulationRuntimeBootstrapState.Ready && _model != null)
            {
                return true;
            }

            DisposeModel();

            _status.State = SimulationRuntimeBootstrapState.Initializing;
            _status.Error = string.Empty;
            _status.HealthCheckResponse = string.Empty;
            _status.HealthCheckRawJson = string.Empty;
            _status.ModelPathSource = string.Empty;
            _status.ResolvedModelPath = string.Empty;
            _status.TelemetryCachePath = string.Empty;

            try
            {
                ConfigureRuntime();

                var resolver = _options.ModelPathResolver;
                if (resolver == null)
                {
                    throw new InvalidOperationException("A model path resolver is required.");
                }

                var resolution = resolver.ResolveModelPath();
                if (resolution == null || !resolution.Success)
                {
                    throw new InvalidOperationException(
                        resolution == null || string.IsNullOrWhiteSpace(resolution.Error)
                            ? "Model path could not be resolved."
                            : resolution.Error);
                }

                _status.ModelPathSource = resolution.Source ?? string.Empty;
                _status.ResolvedModelPath = resolution.ModelPath ?? string.Empty;

                if (!Directory.Exists(_status.ResolvedModelPath) && !File.Exists(_status.ResolvedModelPath))
                {
                    throw new FileNotFoundException(
                        "Model path was resolved but does not exist: " + _status.ResolvedModelPath,
                        _status.ResolvedModelPath);
                }

                _model = new CactusModel(
                    _status.ResolvedModelPath,
                    string.IsNullOrWhiteSpace(_options.CorpusDirectory) ? null : _options.CorpusDirectory,
                    _options.CacheIndex);

                RunHealthCheck();
                _status.State = SimulationRuntimeBootstrapState.Ready;
                return true;
            }
            catch (Exception ex)
            {
                DisposeModel();
                _status.State = SimulationRuntimeBootstrapState.Error;
                _status.Error = ex.Message;
                return false;
            }
        }

        public void Reset()
        {
            DisposeModel();
            _status.State = SimulationRuntimeBootstrapState.Uninitialized;
            _status.Error = string.Empty;
            _status.HealthCheckResponse = string.Empty;
            _status.HealthCheckRawJson = string.Empty;
            _status.ModelPathSource = string.Empty;
            _status.ResolvedModelPath = string.Empty;
            _status.TelemetryCachePath = string.Empty;
        }

        public void Dispose()
        {
            DisposeModel();
        }

        private void ConfigureRuntime()
        {
            CactusRuntime.SetLogLevel(_options.LogLevel);

            if (!string.IsNullOrWhiteSpace(_options.AppId))
            {
                CactusRuntime.SetAppId(_options.AppId);
            }

            var telemetryCachePath = _options.TelemetryCachePath;
            if (string.IsNullOrWhiteSpace(telemetryCachePath))
            {
                telemetryCachePath = Path.Combine(
                    string.IsNullOrWhiteSpace(Application.persistentDataPath)
                        ? Application.temporaryCachePath
                        : Application.persistentDataPath,
                    "cactus-telemetry");
            }

            if (!string.IsNullOrWhiteSpace(telemetryCachePath))
            {
                Directory.CreateDirectory(telemetryCachePath);
                CactusRuntime.ConfigureTelemetry(telemetryCachePath, _options.TelemetryVersion);
                _status.TelemetryCachePath = telemetryCachePath;
            }
        }

        private void RunHealthCheck()
        {
            if (_model == null)
            {
                throw new InvalidOperationException("Model is not initialized.");
            }

            var messages = new List<ConversationMessage>(2);
            messages.Add(new ConversationMessage
            {
                Role = "system",
                Content = string.IsNullOrWhiteSpace(_options.HealthCheckSystemPrompt)
                    ? "Reply using plain text only."
                    : _options.HealthCheckSystemPrompt
            });
            messages.Add(new ConversationMessage
            {
                Role = "user",
                Content = string.IsNullOrWhiteSpace(_options.HealthCheckPrompt)
                    ? "Respond with exactly: BOOTSTRAP_READY"
                    : _options.HealthCheckPrompt
            });

            var rawJson = _model.CompleteJson(
                ConversationMessageJson.Serialize(messages),
                _options.HealthCheckOptionsJson,
                null,
                null,
                null,
                _options.HealthCheckResponseBufferBytes);

            _status.HealthCheckRawJson = rawJson ?? string.Empty;

            var response = CactusCompletionResponse.Parse(rawJson);
            if (!response.ParseSucceeded || !response.Success)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(response.Error)
                        ? "Runtime health check failed."
                        : response.Error);
            }

            if (string.IsNullOrWhiteSpace(response.Response))
            {
                throw new InvalidOperationException("Runtime health check completed but returned an empty response.");
            }

            _status.HealthCheckResponse = response.Response;
        }

        private void DisposeModel()
        {
            if (_model == null)
            {
                return;
            }

            _model.Dispose();
            _model = null;
        }
    }

    public sealed class UnavailableSimulationCompletionModel : ISimulationCompletionModel
    {
        private readonly string _error;

        public UnavailableSimulationCompletionModel(string error)
        {
            _error = string.IsNullOrWhiteSpace(error)
                ? "Completion model is unavailable."
                : error;
        }

        public void Reset()
        {
        }

        public string CompleteJson(
            string messagesJson,
            string optionsJson = null,
            string toolsJson = null,
            Action<string, uint> tokenCallback = null,
            byte[] pcm16Mono = null,
            int responseBufferBytes = CactusNative.DefaultJsonBufferSize)
        {
            throw new InvalidOperationException(_error);
        }
    }
}
