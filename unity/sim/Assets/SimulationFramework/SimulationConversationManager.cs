using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GemmaHackathon.SimulationFramework
{
    public sealed class SimulationConversationManager
    {
        private readonly ISimulationCompletionModel _model;
        private readonly ISimulationStateProvider _stateProvider;
        private readonly SimulationToolRegistry _toolRegistry;
        private readonly SimulationConversationManagerOptions _options;
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
            result.InputMessage = CloneMessage(inputMessage);

            RecordTrace(result, SimulationConversationTraceKind.TurnInput, inputMessage.Content);

            var workingHistory = CloneMessages(_history);
            workingHistory.Add(CloneMessage(inputMessage));

            var toolRoundTrips = 0;
            var completionOptionsJson = _options.CompletionOptionsJson;

            try
            {
                while (true)
                {
                    var snapshot = CloneStateSnapshot(_stateProvider.CaptureState() ?? new SimulationStateSnapshot());
                    result.StateSnapshots.Add(snapshot);
                    RecordTrace(result, SimulationConversationTraceKind.StateSnapshot, snapshot.ToStructuredJson());

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
                    result.CloudHandoffRequested = result.CloudHandoffRequested || completion.CloudHandoff;

                    if (!completion.ParseSucceeded || !completion.Success)
                    {
                        result.Success = false;
                        result.Error = string.IsNullOrWhiteSpace(completion.Error)
                            ? "Completion failed."
                            : completion.Error;
                        RecordTrace(result, SimulationConversationTraceKind.Error, result.Error);
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
                        return result;
                    }

                    for (var i = 0; i < completion.FunctionCalls.Count; i++)
                    {
                        var call = completion.FunctionCalls[i];
                        RecordTrace(
                            result,
                            SimulationConversationTraceKind.FunctionCall,
                            call.Name + " " + (call.ArgumentsJson ?? "{}"));

                        SimulationToolResult toolResult;
                        try
                        {
                            if (!_toolRegistry.TryExecute(call, out toolResult))
                            {
                                toolResult = toolResult ?? SimulationToolResult.CreateError(call.Name, "Tool execution failed.");
                            }
                        }
                        catch (Exception ex)
                        {
                            toolResult = SimulationToolResult.CreateError(call.Name, ex.Message);
                        }

                        toolResult = toolResult ?? SimulationToolResult.CreateError(call.Name, "Tool execution returned no result.");
                        result.ToolResults.Add(toolResult);
                        workingHistory.Add(toolResult.ToConversationMessage());
                        RecordTrace(result, SimulationConversationTraceKind.ToolResult, toolResult.ToToolMessageContentJson());
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
