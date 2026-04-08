namespace GemmaHackathon.SimulationScenarios.SvrFire
{
    internal static class SvrFireScenarioValues
    {
        public const string ScenarioId = "svr-office-fire-evacuation";
        public const string SimulationId = "svr";
        public const string DefaultVariantId = "office_fire_v1";
        public const string DefaultRubricVersion = "svr.fire.rubric.v1";
        public const string DefaultScoringVersion = "svr.fire.score.v1";
        public const string DefaultParticipantAlias = "participant-anonymous";

        public const string SessionStateReady = "ready";
        public const string SessionStateRunning = "running";
        public const string SessionStateComplete = "complete";

        public const string PhaseNormal = "normal";
        public const string PhaseAlarm = "alarm";
        public const string PhaseEvacuation = "evacuation";
        public const string PhaseComplete = "complete";

        public const string LocationWorkstation = "workstation";
        public const string LocationExitA = "exit_a";
        public const string LocationExitB = "exit_b";
        public const string LocationSafe = "safe";
        public const string LocationHazard = "hazard_zone";

        public const string HazardNone = "none";
        public const string HazardAlarmOnly = "alarm_only";
        public const string HazardAlarmAndSmokeExitA = "alarm_and_smoke_exit_a";
        public const string HazardAlarmAndSmokeExitB = "alarm_and_smoke_exit_b";

        public const string CoworkerNearby = "nearby";
        public const string CoworkerNeedsHelp = "needs_help";
        public const string CoworkerAssisted = "assisted";
        public const string CoworkerLeftBehind = "left_behind";

        public const string ActionAcknowledgeAlarm = "acknowledge_alarm";
        public const string ActionMoveExitA = "move_exit_a";
        public const string ActionMoveExitB = "move_exit_b";
        public const string ActionHelpCoworker = "help_coworker";
        public const string ActionAbandonScenario = "abandon_scenario";

        public const string CriticalIgnoredAlarm = "ignored_alarm_60s";
        public const string CriticalWrongExit = "wrong_exit_into_fire";
    }
}
