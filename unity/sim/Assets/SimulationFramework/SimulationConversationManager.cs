using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.Text;

namespace GemmaHackathon.SimulationFramework
{
    public sealed class SimulationConversationManager
    {
        private readonly ISimulationCompletionModel _model;
        private readonly ISimulationStateProvider _stateProvider;
        private readonly SimulationToolRegistry _toolRegistry;
        private readonly SimulationConversationManagerOptions _options;
        private readonly ISimulationToolExecutor _toolExecutor;
        private readonly List<ConversationMessage> _history = new List<ConversationMessage>();

        public SimulationConversationManager(
            ISimulationCompletionModel model,
            ISimulationStateProvider stateProvider,
            SimulationToolRegistry toolRegistry,
            SimulationConversationManagerOptions options = null)
        {
            if (model == null)
            {
                throw new ArgumentNullException("model");
            }

            if (stateProvider == null)
            {
                throw new ArgumentNullException("stateProvider");
            }

            _model = model;
            _stateProvider = stateProvider;
            _toolRegistry = toolRegistry ?? new SimulationToolRegistry();
            _options = options ?? new SimulationConversationManagerOptions();
            _toolExecutor = _options.ToolExecutor ?? new InlineSimulationToolExecutor();

            if (_options.MaxToolRoundTrips < 0)
            {
                throw new ArgumentOutOfRangeException("options", "MaxToolRoundTrips cannot be negative.");
            }

            if (_options.ResponseBufferBytes <= 0)
            {
                throw new ArgumentOutOfRangeException("options", "ResponseBufferBytes must be positive.");
            }
        }

        public IReadOnlyList<ConversationMessage> History
        {
            get { return _history.AsReadOnly(); }
        }

        public void ClearHistory()
        {
            _history.Clear();
            SafeResetModel();
        }

        public SimulationConversationTurnResult ProcessUserText(string userText)
        {
            if (string.IsNullOrWhiteSpace(userText))
            {
                throw new ArgumentException("User text is required.", "userText");
            }

            return ProcessTurn(new ConversationMessage
            {
                Role = "user",
                Content = userText
            });
        }

        public SimulationConversationTurnResult ProcessSimulationEvent(string eventDescription)
        {
            if (string.IsNullOrWhiteSpace(eventDescription))
            {
                throw new ArgumentException("Event description is required.", "eventDescription");
            }

            return ProcessTurn(new ConversationMessage
            {
                Role = "user",
                Content = (_options.EventMessagePrefix ?? string.Empty) + eventDescription
            });
        }

        private SimulationConversationTurnResult ProcessTurn(ConversationMessage inputMessage)
        {
            var result = new SimulationConversationTurnResult();
            result.TurnId = SimulationRunLogging.CreateIdentifier("turn");
            result.InputMessage = CloneMessage(inputMessage);
            LogTurnRecord(result, "started");

            RecordTrace(result, SimulationConversationTraceKind.TurnInput, inputMessage.Content);

            var workingHistory = CloneMessages(_history);
            workingHistory.Add(CloneMessage(inputMessage));

            var toolRoundTrips = 0;
            var completionOptionsJson = _options.CompletionOptionsJson;

            try
            {
                while (true)
                {
                    var completionId = SimulationRunLogging.CreateIdentifier("completion");
                    var snapshot = CloneStateSnapshot(_stateProvider.CaptureState() ?? new SimulationStateSnapshot());
                    result.StateSnapshots.Add(snapshot);
                    RecordTrace(result, SimulationConversationTraceKind.StateSnapshot, snapshot.ToStructuredJson());
                    LogStateSnapshot(result.TurnId, completionId, "pre_completion", snapshot);
                    LogKpiSnapshot(result.TurnId, completionId, "pre_completion");

                    var requestMessages = BuildRequestMessages(snapshot, workingHistory);
                    var messagesJson = ConversationMessageJson.Serialize(requestMessages);
                    RecordTrace(result, SimulationConversationTraceKind.RequestMessagesJson, messagesJson);

                    if (_options.ResetModelBeforeEachCompletion)
                    {
                        SafeResetModel();
                    }

                    var rawJson = _model.CompleteJson(
                        messagesJson,
                        completionOptionsJson,
                        _toolRegistry.BuildToolsJson(),
                        null,
                        null,
                        _options.ResponseBufferBytes);

                    RecordTrace(result, SimulationConversationTraceKind.CompletionJson, rawJson);

                    var completion = CactusCompletionResponse.Parse(rawJson);
                    result.CompletionResponses.Add(completion);
                    result.CompletionCount = result.CompletionResponses.Count;
                    if (completion != null && completion.TotalTimeMs.HasValue)
                    {
                        result.TotalCompletionTimeMs += completion.TotalTimeMs.Value;
                    }
                    result.CloudHandoffRequested = result.CloudHandoffRequested || completion.CloudHandoff;
                    LogCompletionRecord(result.TurnId, completionId, completion);

                    if (!completion.ParseSucceeded || !completion.Success)
                    {
                        result.Success = false;
                        result.Error = string.IsNullOrWhiteSpace(completion.Error)
                            ? "Completion failed."
                            : completion.Error;
                        RecordTrace(result, SimulationConversationTraceKind.Error, result.Error);
                        LogErrorRecord(result.TurnId, "completion", result.Error, completion.RawJson);
                        LogTurnRecord(result, "failed");
                        return result;
                    }

                    if (!string.IsNullOrWhiteSpace(completion.Response))
                    {
                        workingHistory.Add(new ConversationMessage
                        {
                            Role = "assistant",
                            Content = completion.Response
                        });

                        result.AssistantResponses.Add(completion.Response);
                        result.FinalAssistantResponse = completion.Response;
                        RecordTrace(result, SimulationConversationTraceKind.AssistantResponse, completion.Response);
                    }

                    if (completion.FunctionCalls.Count == 0)
                    {
                        CommitHistory(workingHistory);
                        result.Success = true;
                        result.AppliedToHistory = true;
                        LogTurnRecord(result, "completed");
                        return result;
                    }

                    if (toolRoundTrips >= _options.MaxToolRoundTrips)
                    {
                        result.Success = false;
                        result.ReachedToolRoundTripLimit = true;
                        result.Error = "Tool round-trip limit reached.";
                        RecordTrace(result, SimulationConversationTraceKind.Error, result.Error);
                        CommitHistory(workingHistory);
                        result.AppliedToHistory = true;
                        LogErrorRecord(result.TurnId, "turn", result.Error, string.Empty);
                        LogTurnRecord(result, "failed");
                        return result;
                    }

                    for (var i = 0; i < completion.FunctionCalls.Count; i++)
                    {
                        var call = completion.FunctionCalls[i];
                        var toolCorrelationId = SimulationRunLogging.CreateIdentifier("tool");
                        RecordTrace(
                            result,
                            SimulationConversationTraceKind.FunctionCall,
                            call.Name + " " + (call.ArgumentsJson ?? "{}"));
                        LogToolCall(result.TurnId, toolCorrelationId, call);

                        SimulationToolResult toolResult;
                        var toolStopwatch = Stopwatch.StartNew();
                        try
                        {
                            toolResult = ExecuteToolCall(call);
                        }
                        catch (Exception ex)
                        {
                            toolResult = SimulationToolResult.CreateError(call.Name, ex.Message);
                        }

                        toolResult = toolResult ?? SimulationToolResult.CreateError(call.Name, "Tool execution returned no result.");
                        toolStopwatch.Stop();
                        result.ToolResults.Add(toolResult);
                        workingHistory.Add(toolResult.ToConversationMessage());
                        RecordTrace(result, SimulationConversationTraceKind.ToolResult, toolResult.ToToolMessageContentJson());
                        LogToolResult(result.TurnId, toolCorrelationId, toolResult, toolStopwatch.Elapsed.TotalMilliseconds);
                        LogPostToolState(result.TurnId, toolCorrelationId);
                    }

                    toolRoundTrips++;
                    completionOptionsJson = string.IsNullOrWhiteSpace(_options.FollowUpCompletionOptionsJson)
                        ? _options.CompletionOptionsJson
                        : _options.FollowUpCompletionOptionsJson;
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
                RecordTrace(result, SimulationConversationTraceKind.Error, ex.ToString());
                LogErrorRecord(result.TurnId, "turn_exception", result.Error, ex.ToString());
                LogTurnRecord(result, "failed");
                return result;
            }
        }

        private List<ConversationMessage> BuildRequestMessages(
            SimulationStateSnapshot snapshot,
            List<ConversationMessage> workingHistory)
        {
            var result = new List<ConversationMessage>(workingHistory.Count + 1);
            result.Add(new ConversationMessage
            {
                Role = "system",
                Content = BuildSystemMessage(snapshot)
            });

            for (var i = 0; i < workingHistory.Count; i++)
            {
                result.Add(CloneMessage(workingHistory[i]));
            }

            return result;
        }

        private string BuildSystemMessage(SimulationStateSnapshot snapshot)
        {
            var builder = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(_options.SystemPrompt))
            {
                builder.AppendLine(_options.SystemPrompt.Trim());
                builder.AppendLine();
            }

            builder.Append("Current simulation state JSON:");
            builder.AppendLine();
            builder.Append(snapshot == null ? "{}" : snapshot.ToStructuredJson());
            return builder.ToString();
        }

        private void CommitHistory(List<ConversationMessage> workingHistory)
        {
            _history.Clear();
            for (var i = 0; i < workingHistory.Count; i++)
            {
                _history.Add(CloneMessage(workingHistory[i]));
            }
        }

        private void SafeResetModel()
        {
            try
            {
                _model.Reset();
            }
            catch
            {
            }
        }

        private void RecordTrace(
            SimulationConversationTurnResult result,
            SimulationConversationTraceKind kind,
            string content)
        {
            var entry = SimulationConversationTraceEntry.Create(kind, content);
            result.TraceEntries.Add(entry);
            LogTraceEntry(result.TurnId, entry);

            var sink = _options.TraceSink;
            if (sink != null)
            {
                try
                {
                    sink(entry);
                }
                catch
                {
                }
            }
        }

        private void LogTurnRecord(SimulationConversationTurnResult result, string kind)
        {
            var logger = _options.RunLogger;
            if (logger == null || result == null)
            {
                return;
            }

            var payload = new SimulationRunTurnRecord
            {
                InputRole = result.InputMessage == null ? string.Empty : result.InputMessage.Role,
                InputContent = result.InputMessage == null ? string.Empty : result.InputMessage.Content,
                Success = result.Success,
                AppliedToHistory = result.AppliedToHistory,
                CloudHandoffRequested = result.CloudHandoffRequested,
                ReachedToolRoundTripLimit = result.ReachedToolRoundTripLimit,
                FinalAssistantResponse = result.FinalAssistantResponse,
                Error = result.Error,
                CompletionCount = result.CompletionCount,
                TotalCompletionTimeMs = result.TotalCompletionTimeMs,
                AssistantResponseCount = result.AssistantResponses == null ? 0 : result.AssistantResponses.Count,
                ToolResultCount = result.ToolResults == null ? 0 : result.ToolResults.Count
            };

            LogRunEvent(result.TurnId, string.Empty, "turn", kind, payload.ToJson(logger.Verbosity));
        }

        private void LogStateSnapshot(
            string turnId,
            string correlationId,
            string kind,
            SimulationStateSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            LogRunEvent(turnId, correlationId, "state", kind, snapshot.ToStructuredJson());
        }

        private void LogKpiSnapshot(string turnId, string correlationId, string kind)
        {
            var kpiSnapshot = CaptureKpiSnapshot();
            if (kpiSnapshot == null || !kpiSnapshot.HasEntries)
            {
                return;
            }

            LogRunEvent(turnId, correlationId, "kpi", kind, kpiSnapshot.ToStructuredJson());
        }

        private void LogPostToolState(string turnId, string correlationId)
        {
            var snapshot = CloneStateSnapshot(_stateProvider.CaptureState() ?? new SimulationStateSnapshot());
            LogStateSnapshot(turnId, correlationId, "post_tool_execution", snapshot);
            LogKpiSnapshot(turnId, correlationId, "post_tool_execution");
        }

        private void LogCompletionRecord(
            string turnId,
            string correlationId,
            CactusCompletionResponse completion)
        {
            if (completion == null)
            {
                return;
            }

            var logger = _options.RunLogger;
            if (logger == null)
            {
                return;
            }

            var payload = new SimulationRunCompletionRecord
            {
                Success = completion.Success,
                ParseSucceeded = completion.ParseSucceeded,
                CloudHandoff = completion.CloudHandoff,
                Error = completion.Error,
                Response = completion.Response,
                FunctionCallCount = completion.FunctionCalls == null ? 0 : completion.FunctionCalls.Count,
                Confidence = completion.Confidence,
                TimeToFirstTokenMs = completion.TimeToFirstTokenMs,
                TotalTimeMs = completion.TotalTimeMs,
                PrefillTokensPerSecond = completion.PrefillTokensPerSecond,
                DecodeTokensPerSecond = completion.DecodeTokensPerSecond,
                RamUsageMb = completion.RamUsageMb,
                PrefillTokens = completion.PrefillTokens,
                DecodeTokens = completion.DecodeTokens,
                TotalTokens = completion.TotalTokens,
                RawJson = completion.RawJson
            };

            LogRunEvent(
                turnId,
                correlationId,
                "completion",
                "result",
                payload.ToJson(logger.Verbosity));
        }

        private void LogToolCall(string turnId, string correlationId, SimulationToolCall call)
        {
            if (call == null)
            {
                return;
            }

            var payload = new SimulationRunToolRecord
            {
                Name = call.Name,
                ArgumentsJson = call.ArgumentsJson,
                Content = string.Empty,
                IsError = false
            };

            var logger = _options.RunLogger;
            LogRunEvent(
                turnId,
                correlationId,
                "tool",
                "call",
                payload.ToJson(logger == null ? SimulationRunLogVerbosity.Compact : logger.Verbosity));
        }

        private void LogToolResult(string turnId, string correlationId, SimulationToolResult result, double durationMs)
        {
            if (result == null)
            {
                return;
            }

            var payload = new SimulationRunToolRecord
            {
                Name = result.Name,
                ArgumentsJson = "{}",
                Content = result.Content,
                IsError = result.IsError,
                DurationMs = durationMs
            };

            var logger = _options.RunLogger;
            LogRunEvent(
                turnId,
                correlationId,
                "tool",
                "result",
                payload.ToJson(logger == null ? SimulationRunLogVerbosity.Compact : logger.Verbosity));
        }

        private void LogTraceEntry(string turnId, SimulationConversationTraceEntry entry)
        {
            var logger = _options.RunLogger;
            if (logger == null || entry == null)
            {
                return;
            }

            if (logger.Verbosity != SimulationRunLogVerbosity.Verbose)
            {
                if (entry.Kind != SimulationConversationTraceKind.Error)
                {
                    return;
                }
            }

            var payload = new SimulationRunTraceRecord
            {
                TraceKind = entry.Kind.ToString(),
                Content = entry.Content
            };

            LogRunEvent(turnId, string.Empty, "trace", entry.Kind.ToString(), payload.ToJson());
        }

        private void LogErrorRecord(string turnId, string source, string message, string details)
        {
            var payload = new SimulationRunErrorRecord
            {
                Source = source ?? string.Empty,
                Message = message ?? string.Empty,
                Details = details ?? string.Empty
            };

            LogRunEvent(turnId, string.Empty, "error", source ?? string.Empty, payload.ToJson());
        }

        private void LogRunEvent(
            string turnId,
            string correlationId,
            string family,
            string kind,
            string payloadJson)
        {
            var logger = _options.RunLogger;
            if (logger == null)
            {
                return;
            }

            try
            {
                logger.LogEvent(new SimulationRunLogEvent
                {
                    TurnId = turnId ?? string.Empty,
                    CorrelationId = correlationId ?? string.Empty,
                    Family = family ?? string.Empty,
                    Kind = kind ?? string.Empty,
                    PayloadJson = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson
                });
            }
            catch
            {
            }
        }

        private SimulationKpiSnapshot CaptureKpiSnapshot()
        {
            var provider = _stateProvider as ISimulationKpiProvider;
            if (provider == null)
            {
                return null;
            }

            try
            {
                var snapshot = provider.CaptureKpis();
                return snapshot == null ? null : snapshot.Clone();
            }
            catch
            {
                return null;
            }
        }

        private SimulationToolResult ExecuteToolCall(SimulationToolCall call)
        {
            var result = _toolExecutor.Execute(_toolRegistry, call);
            if (result == null)
            {
                return SimulationToolResult.CreateError(
                    call == null ? string.Empty : call.Name,
                    "Tool execution returned no result.");
            }

            return result;
        }

        private static List<ConversationMessage> CloneMessages(List<ConversationMessage> messages)
        {
            var result = new List<ConversationMessage>(messages == null ? 0 : messages.Count);
            if (messages == null)
            {
                return result;
            }

            for (var i = 0; i < messages.Count; i++)
            {
                result.Add(CloneMessage(messages[i]));
            }

            return result;
        }

        private static ConversationMessage CloneMessage(ConversationMessage message)
        {
            var value = message ?? new ConversationMessage();
            return new ConversationMessage
            {
                Role = value.Role ?? string.Empty,
                Content = value.Content ?? string.Empty,
                Images = value.Images == null ? Array.Empty<string>() : (string[])value.Images.Clone(),
                Audio = value.Audio == null ? Array.Empty<string>() : (string[])value.Audio.Clone()
            };
        }

        private static SimulationStateSnapshot CloneStateSnapshot(SimulationStateSnapshot snapshot)
        {
            var value = snapshot ?? new SimulationStateSnapshot();
            var clone = new SimulationStateSnapshot();
            clone.SimulationId = value.SimulationId ?? string.Empty;
            clone.ScenarioId = value.ScenarioId ?? string.Empty;
            clone.ElapsedSeconds = value.ElapsedSeconds;

            if (value.Entries != null)
            {
                for (var i = 0; i < value.Entries.Count; i++)
                {
                    var entry = value.Entries[i] ?? new SimulationStateEntry();
                    clone.Entries.Add(new SimulationStateEntry
                    {
                        Category = entry.Category ?? string.Empty,
                        Key = entry.Key ?? string.Empty,
                        ValueJson = string.IsNullOrWhiteSpace(entry.ValueJson) ? "null" : entry.ValueJson
                    });
                }
            }

            if (value.Checklist != null)
            {
                for (var i = 0; i < value.Checklist.Count; i++)
                {
                    var item = value.Checklist[i] ?? new SimulationChecklistItem();
                    clone.Checklist.Add(new SimulationChecklistItem
                    {
                        Id = item.Id ?? string.Empty,
                        Label = item.Label ?? string.Empty,
                        Completed = item.Completed,
                        Notes = item.Notes ?? string.Empty
                    });
                }
            }

            if (value.RecentActions != null)
            {
                for (var i = 0; i < value.RecentActions.Count; i++)
                {
                    var action = value.RecentActions[i] ?? new SimulationActionRecord();
                    clone.RecentActions.Add(new SimulationActionRecord
                    {
                        Actor = action.Actor ?? string.Empty,
                        Verb = action.Verb ?? string.Empty,
                        Details = action.Details ?? string.Empty,
                        OccurredAtSeconds = action.OccurredAtSeconds
                    });
                }
            }

            return clone;
        }
    }
}
