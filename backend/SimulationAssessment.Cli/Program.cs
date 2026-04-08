using System;
using GemmaHackathon.SimulationFramework;
using GemmaHackathon.SimulationScenarios.SvrFire;

namespace GemmaHackathon.Backend.AssessmentCli
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                var options = CliOptions.Parse(args);
                if (options.ShowHelp)
                {
                    WriteUsage();
                    return 0;
                }

                var sessionLog = SimulationRunLogReader.Load(options);
                var replay = SvrFireAssessmentReplay.Replay(sessionLog.SessionRecord, sessionLog.AuditEvents);
                replay.SourceSessionDirectory = sessionLog.SessionDirectory ?? string.Empty;
                var narrative = ComposeNarrativeIfRequested(options, sessionLog, replay);

                var exportDirectory = AssessmentExportWriter.Write(sessionLog, replay, narrative, options.OutputDirectory);
                Console.WriteLine("Exported SVR assessment to:");
                Console.WriteLine(exportDirectory);
                Console.WriteLine(
                    "Score " +
                    replay.Assessment.Result.TotalPoints +
                    "/" +
                    replay.Assessment.Result.MaxPoints +
                    " (" +
                    (replay.Assessment.Result.Band ?? string.Empty) +
                    ")");
                if (narrative != null && narrative.Success)
                {
                    Console.WriteLine(
                        "Narrative " +
                        (narrative.Provider ?? string.Empty) +
                        (narrative.UsedFallback ? " (fallback)" : string.Empty));
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("SVR assessment export failed: " + ex.Message);
                return 1;
            }
        }

        private static void WriteUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  dotnet run --project backend/SimulationAssessment.Cli -- --latest");
            Console.WriteLine("  dotnet run --project backend/SimulationAssessment.Cli -- --session <session-dir-or-id>");
            Console.WriteLine("  dotnet run --project backend/SimulationAssessment.Cli -- --session <session-dir-or-id> --output <export-dir>");
            Console.WriteLine("  dotnet run --project backend/SimulationAssessment.Cli -- --latest --narrative template|desktop-gemma");
        }

        private static AssessmentNarrative ComposeNarrativeIfRequested(
            CliOptions options,
            SimulationRunSessionLog sessionLog,
            SvrFireAssessmentReplayResult replay)
        {
            var composer = AssessmentNarrativeComposerFactory.Create(options);
            if (composer == null)
            {
                return null;
            }

            return composer.Compose(new AssessmentNarrativeComposeRequest
            {
                SessionRecord = sessionLog == null ? new AuditSessionRecord() : (sessionLog.SessionRecord ?? new AuditSessionRecord()),
                Assessment = replay == null ? new AssessmentArtifacts() : (replay.Assessment ?? new AssessmentArtifacts()),
                Timeline = replay == null ? new System.Collections.Generic.List<AuditEvent>() : (replay.Timeline ?? new System.Collections.Generic.List<AuditEvent>())
            });
        }
    }
}
