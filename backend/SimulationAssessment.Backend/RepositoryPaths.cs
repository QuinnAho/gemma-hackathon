using System;
using System.IO;

namespace GemmaHackathon.Backend.AssessmentCli
{
    internal static class RepositoryPaths
    {
        public static string FindRepositoryRoot()
        {
            var current = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "TO-DO.md")) &&
                    Directory.Exists(Path.Combine(current.FullName, "unity")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not resolve the repository root from the current working directory.");
        }

        public static string GetRunLogRoot()
        {
            return Path.Combine(FindRepositoryRoot(), ".local", "logs", "simulation-runs");
        }

        public static string GetDefaultExportRoot()
        {
            return Path.Combine(FindRepositoryRoot(), ".local", "exports", "svr");
        }
    }
}
