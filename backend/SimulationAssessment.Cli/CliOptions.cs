using System;

namespace GemmaHackathon.Backend.AssessmentCli
{
    internal sealed class CliOptions
    {
        public bool UseLatest = true;
        public string SessionSelector = string.Empty;
        public string OutputDirectory = string.Empty;
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
    }
}
