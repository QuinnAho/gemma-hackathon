using System;
using GemmaHackathon.SimulationFramework;

namespace GemmaHackathon.SimulationScenarios.SvrFire
{
    internal sealed class EscalateHazardTool : ISimulationToolHandler
    {
        private readonly SvrFireScenarioState _state;
        private readonly Func<float> _clock;

        public EscalateHazardTool(SvrFireScenarioState state, Func<float> clock)
        {
            _state = state;
            _clock = clock;
            Definition = new SimulationToolDefinition
            {
                Name = "escalate_hazard",
                Description = "Escalates the fire scenario and updates hazard or route status.",
                ParametersJson =
                    "{\"type\":\"object\",\"properties\":{\"hazard_state\":{\"type\":\"string\"},\"announcement\":{\"type\":\"string\"}},\"required\":[\"hazard_state\"]}"
            };
        }

        public SimulationToolDefinition Definition { get; private set; }

        public SimulationToolResult Execute(SimulationToolCall call)
        {
            var argumentsJson = call == null ? string.Empty : call.ArgumentsJson;
            var hazardState = SvrToolArgumentReader.ReadString(
                argumentsJson,
                "hazard_state",
                SvrFireScenarioValues.HazardAlarmAndSmokeExitA);
            var announcement = SvrToolArgumentReader.ReadString(
                argumentsJson,
                "announcement",
                "Fire alarm active.");
            _state.EscalateHazard(hazardState, announcement, GetElapsedSeconds());
            return new SimulationToolResult
            {
                Name = call == null ? string.Empty : call.Name,
                Content = "Hazard escalated to " + hazardState + ".",
                IsError = false
            };
        }

        private float GetElapsedSeconds()
        {
            return _clock == null ? 0f : _clock();
        }
    }

    internal sealed class PromptParticipantTool : ISimulationToolHandler
    {
        private readonly SvrFireScenarioState _state;
        private readonly Func<float> _clock;

        public PromptParticipantTool(SvrFireScenarioState state, Func<float> clock)
        {
            _state = state;
            _clock = clock;
            Definition = new SimulationToolDefinition
            {
                Name = "prompt_participant",
                Description = "Delivers a short in-scenario prompt to the participant.",
                ParametersJson =
                    "{\"type\":\"object\",\"properties\":{\"message\":{\"type\":\"string\"}},\"required\":[\"message\"]}"
            };
        }

        public SimulationToolDefinition Definition { get; private set; }

        public SimulationToolResult Execute(SimulationToolCall call)
        {
            var message = SvrToolArgumentReader.ReadString(
                call == null ? string.Empty : call.ArgumentsJson,
                "message",
                "Proceed to the clear exit.");
            _state.PromptParticipant(message, GetElapsedSeconds());
            return new SimulationToolResult
            {
                Name = call == null ? string.Empty : call.Name,
                Content = message,
                IsError = false
            };
        }

        private float GetElapsedSeconds()
        {
            return _clock == null ? 0f : _clock();
        }
    }

    internal sealed class ChangeEnvironmentCueTool : ISimulationToolHandler
    {
        private readonly SvrFireScenarioState _state;
        private readonly Func<float> _clock;

        public ChangeEnvironmentCueTool(SvrFireScenarioState state, Func<float> clock)
        {
            _state = state;
            _clock = clock;
            Definition = new SimulationToolDefinition
            {
                Name = "change_environment_cue",
                Description = "Adjusts route availability, coworker state, or visible environmental cues.",
                ParametersJson =
                    "{\"type\":\"object\",\"properties\":{\"route_id\":{\"type\":\"string\"},\"available\":{\"type\":\"boolean\"},\"coworker_state\":{\"type\":\"string\"},\"note\":{\"type\":\"string\"}},\"required\":[]}"
            };
        }

        public SimulationToolDefinition Definition { get; private set; }

        public SimulationToolResult Execute(SimulationToolCall call)
        {
            var argumentsJson = call == null ? string.Empty : call.ArgumentsJson;
            var routeId = SvrToolArgumentReader.ReadString(argumentsJson, "route_id", string.Empty);
            var available = SvrToolArgumentReader.ReadBoolean(argumentsJson, "available");
            var coworkerState = SvrToolArgumentReader.ReadString(argumentsJson, "coworker_state", string.Empty);
            var note = SvrToolArgumentReader.ReadString(argumentsJson, "note", "Environment cue updated.");

            _state.ChangeEnvironmentCue(routeId, available, coworkerState, note, GetElapsedSeconds());
            return new SimulationToolResult
            {
                Name = call == null ? string.Empty : call.Name,
                Content = note,
                IsError = false
            };
        }

        private float GetElapsedSeconds()
        {
            return _clock == null ? 0f : _clock();
        }
    }

    internal sealed class AnnotateContextTool : ISimulationToolHandler
    {
        private readonly SvrFireScenarioState _state;
        private readonly Func<float> _clock;

        public AnnotateContextTool(SvrFireScenarioState state, Func<float> clock)
        {
            _state = state;
            _clock = clock;
            Definition = new SimulationToolDefinition
            {
                Name = "annotate_context",
                Description = "Stores a non-scoring AI note for later review.",
                ParametersJson =
                    "{\"type\":\"object\",\"properties\":{\"note\":{\"type\":\"string\"}},\"required\":[\"note\"]}"
            };
        }

        public SimulationToolDefinition Definition { get; private set; }

        public SimulationToolResult Execute(SimulationToolCall call)
        {
            var note = SvrToolArgumentReader.ReadString(
                call == null ? string.Empty : call.ArgumentsJson,
                "note",
                "AI annotation recorded.");
            _state.AnnotateContext(note, GetElapsedSeconds());
            return new SimulationToolResult
            {
                Name = call == null ? string.Empty : call.Name,
                Content = note,
                IsError = false
            };
        }

        private float GetElapsedSeconds()
        {
            return _clock == null ? 0f : _clock();
        }
    }

    internal sealed class TransitionPhaseTool : ISimulationToolHandler
    {
        private readonly SvrFireScenarioState _state;
        private readonly Func<float> _clock;

        public TransitionPhaseTool(SvrFireScenarioState state, Func<float> clock)
        {
            _state = state;
            _clock = clock;
            Definition = new SimulationToolDefinition
            {
                Name = "transition_phase",
                Description = "Moves the scenario to another explicit phase.",
                ParametersJson =
                    "{\"type\":\"object\",\"properties\":{\"phase\":{\"type\":\"string\"}},\"required\":[\"phase\"]}"
            };
        }

        public SimulationToolDefinition Definition { get; private set; }

        public SimulationToolResult Execute(SimulationToolCall call)
        {
            var phase = SvrToolArgumentReader.ReadString(
                call == null ? string.Empty : call.ArgumentsJson,
                "phase",
                SvrFireScenarioValues.PhaseEvacuation);
            _state.TransitionPhase(phase, GetElapsedSeconds());
            return new SimulationToolResult
            {
                Name = call == null ? string.Empty : call.Name,
                Content = "Scenario phase changed to " + phase + ".",
                IsError = false
            };
        }

        private float GetElapsedSeconds()
        {
            return _clock == null ? 0f : _clock();
        }
    }

    internal sealed class RequestEndScenarioTool : ISimulationToolHandler
    {
        private readonly SvrFireScenarioState _state;
        private readonly Func<float> _clock;

        public RequestEndScenarioTool(SvrFireScenarioState state, Func<float> clock)
        {
            _state = state;
            _clock = clock;
            Definition = new SimulationToolDefinition
            {
                Name = "request_end_scenario",
                Description = "Requests that the deterministic scenario controller conclude the session if terminal conditions are met.",
                ParametersJson =
                    "{\"type\":\"object\",\"properties\":{\"reason\":{\"type\":\"string\"}},\"required\":[\"reason\"]}"
            };
        }

        public SimulationToolDefinition Definition { get; private set; }

        public SimulationToolResult Execute(SimulationToolCall call)
        {
            var reason = SvrToolArgumentReader.ReadString(
                call == null ? string.Empty : call.ArgumentsJson,
                "reason",
                "AI requested scenario completion.");
            _state.RequestEndScenario(reason, GetElapsedSeconds());
            return new SimulationToolResult
            {
                Name = call == null ? string.Empty : call.Name,
                Content = reason,
                IsError = false
            };
        }

        private float GetElapsedSeconds()
        {
            return _clock == null ? 0f : _clock();
        }
    }
}
