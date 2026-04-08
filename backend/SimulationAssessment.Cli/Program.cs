using System;
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

                var exportDirectory = AssessmentExportWriter.Write(sessionLog, replay, options.OutputDirectory);
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
        }
    }
}
