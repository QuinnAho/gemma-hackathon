using System;

namespace GemmaHackathon.Backend.AssessmentCli
{
    internal sealed class CliOptions
    {
        public const string NarrativeModeNone = "none";
        public const string NarrativeModeTemplate = "template";
        public const string NarrativeModeDesktopGemma = "desktop-gemma";

        public bool UseLatest = true;
        public string SessionSelector = string.Empty;
        public string OutputDirectory = string.Empty;
        public string NarrativeMode = NarrativeModeNone;
        public bool ShowHelp;

        public static CliOptions Parse(string[] args)
        {
            var options = new CliOptions();
            if (args == null || args.Length == 0)
            {
                return options;
            }

            for (var i = 0; i < args.Length; i++)
            {
                var current = args[i] ?? string.Empty;
                switch (current)
                {
                    case "--help":
                    case "-h":
                    case "/?":
                        options.ShowHelp = true;
                        return options;

                    case "--latest":
                        options.UseLatest = true;
                        options.SessionSelector = string.Empty;
                        break;

                    case "--session":
                        if (i + 1 >= args.Length)
                        {
                            throw new ArgumentException("Missing value for --session.");
                        }

                        options.UseLatest = false;
                        options.SessionSelector = args[++i] ?? string.Empty;
                        break;

                    case "--output":
                        if (i + 1 >= args.Length)
                        {
                            throw new ArgumentException("Missing value for --output.");
                        }

                        options.OutputDirectory = args[++i] ?? string.Empty;
                        break;

                    case "--narrative":
                        if (i + 1 >= args.Length)
                        {
                            throw new ArgumentException("Missing value for --narrative.");
                        }

                        options.NarrativeMode = NormalizeNarrativeMode(args[++i]);
                        break;

                    default:
                        if (string.IsNullOrWhiteSpace(current))
                        {
                            break;
                        }

                        if (current.StartsWith("-", StringComparison.Ordinal))
                        {
                            throw new ArgumentException("Unsupported argument `" + current + "`.");
                        }

                        options.UseLatest = false;
                        options.SessionSelector = current;
                        break;
                }
            }

            return options;
        }

        private static string NormalizeNarrativeMode(string value)
        {
            var safeValue = string.IsNullOrWhiteSpace(value)
                ? NarrativeModeNone
                : value.Trim().ToLowerInvariant();

            if (string.Equals(safeValue, "desktop_gemma", StringComparison.Ordinal))
            {
                return NarrativeModeDesktopGemma;
            }

            if (string.Equals(safeValue, NarrativeModeNone, StringComparison.Ordinal) ||
                string.Equals(safeValue, NarrativeModeTemplate, StringComparison.Ordinal) ||
                string.Equals(safeValue, NarrativeModeDesktopGemma, StringComparison.Ordinal))
            {
                return safeValue;
            }

            throw new ArgumentException(
                "Unsupported value for --narrative. Expected `none`, `template`, or `desktop-gemma`.");
        }
    }
}
