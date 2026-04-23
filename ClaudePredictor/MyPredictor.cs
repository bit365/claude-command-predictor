using System.Collections.Frozen;
using System.Management.Automation;
using System.Management.Automation.Subsystem;
using System.Management.Automation.Subsystem.Prediction;

namespace ClaudePredictor
{
    public class MyPredictor : ICommandPredictor
    {
        private const string ClaudeCommand = "claude";
        private const int MaxSuggestionCount = 24;
        internal const string PredictorGuid = "8f0750d6-aafb-4d6b-9d90-a17d33522dbe";
        private static readonly Guid PredictorId = new(PredictorGuid);
        private static readonly string[] PluginSubcommands = ["disable", "enable", "install", "list", "marketplace", "uninstall", "update", "validate"];

        private static readonly string[] RootCommands =
        [
            "agents", "auto-mode", "auth", "mcp", "plugin", "plugins", "setup-token", "doctor", "update", "upgrade", "install", "remote-control"
        ];

        private static readonly string[] RootOptions =
        [
            "--add-dir", "--agent", "--agents", "--append-system-prompt", "--append-system-prompt-file", "--bare",
            "--channels", "--chrome", "--continue", "-c", "--dangerously-load-development-channels", "--dangerously-skip-permissions",
            "--debug", "--debug-file", "--disable-slash-commands", "--disallowedTools", "--effort", "--exclude-dynamic-system-prompt-sections",
            "--fallback-model", "--fork-session", "--from-pr", "--ide", "--init", "--init-only", "--include-hook-events",
            "--include-partial-messages", "--input-format", "--json-schema", "--maintenance", "--max-budget-usd", "--max-turns",
            "--mcp-config", "--model", "--name", "-n", "--no-chrome", "--no-session-persistence", "--output-format", "--permission-mode",
            "--permission-prompt-tool", "--plugin-dir", "--print", "-p", "--remote", "--remote-control", "--rc", "--replay-user-messages",
            "--resume", "-r", "--session-id", "--setting-sources", "--settings", "--strict-mcp-config", "--system-prompt",
            "--system-prompt-file", "--teleport", "--teammate-mode", "--tmux", "--tools", "--verbose", "--version", "-v", "--worktree", "-w"
        ];

        private static readonly Dictionary<string, string[]> OptionValues = new(StringComparer.OrdinalIgnoreCase)
        {
            ["--output-format"] = ["text", "json", "stream-json"],
            ["--input-format"] = ["text", "stream-json"],
            ["--permission-mode"] = ["default", "acceptEdits", "plan", "auto", "dontAsk", "bypassPermissions"],
            ["--model"] = ["sonnet", "opus", "haiku"],
            ["--setting-sources"] = ["user", "project", "local"],
            ["--effort"] = ["low", "medium", "high", "xhigh", "max"],
            ["--teammate-mode"] = ["auto", "in-process", "tmux"]
        };

        private static readonly FrozenSet<string> FilePathOptions =
        new[]
        {
            "--settings", "--mcp-config", "--debug-file", "--append-system-prompt-file", "--system-prompt-file", "--json-schema"
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        private static readonly FrozenSet<string> DirectoryPathOptions =
        new[]
        {
            "--add-dir", "--plugin-dir", "--worktree"
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, string[]> Subcommands = new(StringComparer.OrdinalIgnoreCase)
        {
            ["auth"] = ["login", "logout", "status"],
            ["auto-mode"] = ["defaults", "config", "critique"],
            ["mcp"] = ["add", "add-from-claude-desktop", "add-json", "get", "list", "remove", "reset-project-choices", "serve"],
            ["plugin"] = PluginSubcommands,
            ["plugins"] = PluginSubcommands
        };

        public Guid Id => PredictorId;

        public string Name => "ClaudePredictor";

        public string Description => "Predictive IntelliSense for Claude Code CLI.";

        public SuggestionPackage GetSuggestion(PredictionClient client, PredictionContext context, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return NoSuggestion();
            }

            var input = context.InputAst?.Extent?.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(input))
            {
                return NoSuggestion();
            }

            var trimmed = input.TrimStart();
            var leadingWhitespace = input[..(input.Length - trimmed.Length)];
            var tokens = Tokenize(trimmed);
            if (tokens.Count == 0 || !string.Equals(tokens[0], ClaudeCommand, StringComparison.OrdinalIgnoreCase))
            {
                return NoSuggestion();
            }

            var currentToken = GetCurrentToken(trimmed, tokens);
            var suggestions = BuildSuggestions(trimmed, tokens, currentToken, cancellationToken);

            if (suggestions.Count == 0)
            {
                return NoSuggestion();
            }

            var predictiveSuggestions = suggestions
                .Select(s => new PredictiveSuggestion(leadingWhitespace + s))
                .ToList();

            return new SuggestionPackage(predictiveSuggestions);
        }

        private static SuggestionPackage NoSuggestion() => default!;

        public bool CanAcceptFeedback(PredictionClient client, PredictorFeedbackKind feedback) => false;

        public void OnSuggestionDisplayed(PredictionClient client, uint session, int countOrIndex)
        {
        }

        public void OnSuggestionAccepted(PredictionClient client, uint session, string acceptedSuggestion)
        {
        }

        public void OnCommandLineAccepted(PredictionClient client, IReadOnlyList<string> history)
        {
        }

        public void OnCommandLineExecuted(PredictionClient client, string commandLine, bool success)
        {
        }

        private static List<string> BuildSuggestions(string input, List<string> tokens, string currentToken, CancellationToken cancellationToken)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (HasOptionTerminator(tokens))
            {
                return [];
            }

            if (tokens.Count <= 1)
            {
                AddCandidates(input, currentToken, RootCommands.Concat(RootOptions), result, cancellationToken);
                return LimitSuggestions(result);
            }

            if (TryAddOptionValueSuggestions(input, tokens, currentToken, result, cancellationToken))
            {
                return LimitSuggestions(result);
            }

            var primaryCommand = tokens[1];
            if (Subcommands.TryGetValue(primaryCommand, out var subCandidates))
            {
                AddCandidates(input, currentToken, subCandidates, result, cancellationToken);
            }

            AddCandidates(input, currentToken, RootOptions, result, cancellationToken);
            return LimitSuggestions(result);
        }

        private static bool TryAddOptionValueSuggestions(
            string input,
            List<string> tokens,
            string currentToken,
            HashSet<string> result,
            CancellationToken cancellationToken)
        {
            if (tokens.Count < 2)
            {
                return false;
            }

            var hasTrailingWhitespace = HasTrailingWhitespace(input);

            if (!hasTrailingWhitespace && TryAddEqualsStyleValueSuggestions(input, currentToken, result, cancellationToken))
            {
                return true;
            }

            var optionToken = tokens[^1];
            if (!hasTrailingWhitespace)
            {
                optionToken = tokens[^2];
            }

            if (!OptionValues.TryGetValue(optionToken, out var values))
            {
                var directoriesOnly = DirectoryPathOptions.Contains(optionToken);
                if (!directoriesOnly && !FilePathOptions.Contains(optionToken))
                {
                    return false;
                }

                var pathPrefix = hasTrailingWhitespace ? string.Empty : currentToken;
                var pathCandidates = GetPathCandidates(pathPrefix, directoriesOnly);
                AddCandidates(input, currentToken, pathCandidates, result, cancellationToken);
                return true;
            }

            AddCandidates(input, currentToken, values, result, cancellationToken);
            return true;
        }

        private static bool TryAddEqualsStyleValueSuggestions(
            string input,
            string currentToken,
            HashSet<string> result,
            CancellationToken cancellationToken)
        {
            var equalIndex = currentToken.IndexOf('=');
            if (equalIndex <= 0)
            {
                return false;
            }

            var optionToken = currentToken[..equalIndex];
            var valuePrefix = currentToken[(equalIndex + 1)..];

            if (OptionValues.TryGetValue(optionToken, out var optionValues))
            {
                AddAssignedValueCandidates(input, currentToken, optionToken, valuePrefix, optionValues, result, cancellationToken);
                return true;
            }

            var directoriesOnly = DirectoryPathOptions.Contains(optionToken);
            if (!directoriesOnly && !FilePathOptions.Contains(optionToken))
            {
                return false;
            }

            var pathCandidates = GetPathCandidates(valuePrefix, directoriesOnly);
            AddAssignedValueCandidates(input, currentToken, optionToken, valuePrefix, pathCandidates, result, cancellationToken);
            return true;
        }

        private static List<string> LimitSuggestions(IEnumerable<string> suggestions) =>
            [.. suggestions.Take(MaxSuggestionCount)];

        private static void AddCandidates(
            string input,
            string currentToken,
            IEnumerable<string> candidates,
            HashSet<string> result,
            CancellationToken cancellationToken)
        {
            var hasTrailingWhitespace = HasTrailingWhitespace(input);

            foreach (var candidate in candidates)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (!string.IsNullOrEmpty(currentToken) &&
                    !candidate.StartsWith(currentToken, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (hasTrailingWhitespace)
                {
                    result.Add(input + QuoteIfNeeded(candidate));
                }
                else
                {
                    var replacementLength = currentToken.Length;
                    var baseInput = replacementLength > 0 ? input[..^replacementLength] : input;
                    result.Add(baseInput + QuoteIfNeeded(candidate));
                }
            }
        }

        private static void AddAssignedValueCandidates(
            string input,
            string currentToken,
            string optionToken,
            string valuePrefix,
            IEnumerable<string> candidates,
            HashSet<string> result,
            CancellationToken cancellationToken)
        {
            foreach (var candidate in candidates)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (!string.IsNullOrEmpty(valuePrefix) &&
                    !candidate.StartsWith(valuePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var baseInput = input[..^currentToken.Length];
                result.Add($"{baseInput}{optionToken}={QuoteIfNeeded(candidate)}");
            }
        }

        private static List<string> GetPathCandidates(string valuePrefix, bool directoriesOnly)
        {
            try
            {
                var prefix = valuePrefix.Trim();
                var quoteChar = '\0';
                if (prefix.Length > 0 && (prefix[0] == '\'' || prefix[0] == '"'))
                {
                    var startsWithQuote = prefix[0];
                    var hasMatchingCloseQuote = prefix.Length > 1 && prefix[^1] == startsWithQuote;
                    if (!hasMatchingCloseQuote)
                    {
                        quoteChar = startsWithQuote;
                        prefix = prefix[1..];
                    }
                }

                var separatorIndex = prefix.LastIndexOfAny(['/', '\\']);
                var parentPart = separatorIndex >= 0 ? prefix[..(separatorIndex + 1)] : string.Empty;
                var namePrefix = separatorIndex >= 0 ? prefix[(separatorIndex + 1)..] : prefix;

                var currentDirectory = Environment.CurrentDirectory;
                var parentDirectory = string.IsNullOrEmpty(parentPart)
                    ? currentDirectory
                    : Path.GetFullPath(Path.Combine(currentDirectory, parentPart));

                if (!Directory.Exists(parentDirectory))
                {
                    return [];
                }

                var entries = new List<string>(MaxSuggestionCount);

                foreach (var path in Directory.EnumerateDirectories(parentDirectory))
                {
                    var fileName = Path.GetFileName(path);
                    if (!fileName.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var candidate = $"{parentPart}{fileName}{Path.DirectorySeparatorChar}";
                    entries.Add(quoteChar == '\0' ? candidate : QuoteWith(candidate, quoteChar));
                    if (entries.Count >= MaxSuggestionCount)
                    {
                        return entries;
                    }
                }

                if (!directoriesOnly)
                {
                    foreach (var path in Directory.EnumerateFiles(parentDirectory))
                    {
                        var fileName = Path.GetFileName(path);
                        if (!fileName.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var candidate = $"{parentPart}{fileName}";
                        entries.Add(quoteChar == '\0' ? candidate : QuoteWith(candidate, quoteChar));
                        if (entries.Count >= MaxSuggestionCount)
                        {
                            return entries;
                        }
                    }
                }
                return entries;
            }
            catch
            {
                return [];
            }
        }

        private static string QuoteWith(string value, char quoteChar)
        {
            if (quoteChar == '\'')
            {
                return $"'{value.Replace("'", "''")}'";
            }

            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        private static string QuoteIfNeeded(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            if ((value[0] == '\'' && value[^1] == '\'') || (value[0] == '"' && value[^1] == '"'))
            {
                return value;
            }

            return value.Any(char.IsWhiteSpace)
                ? $"'{value.Replace("'", "''")}'"
                : value;
        }

        private static string GetCurrentToken(string input, List<string> tokens)
        {
            if (string.IsNullOrEmpty(input) || HasTrailingWhitespace(input))
            {
                return string.Empty;
            }

            return tokens.Count == 0 ? string.Empty : tokens[^1];
        }

        private static bool HasOptionTerminator(List<string> tokens)
        {
            for (var i = 1; i < tokens.Count; i++)
            {
                if (tokens[i] == "--")
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasTrailingWhitespace(string input) =>
            !string.IsNullOrEmpty(input) && char.IsWhiteSpace(input[^1]);

        private static List<string> Tokenize(string input)
        {
            var tokens = new List<string>();
            var start = -1;
            var quoteChar = '\0';

            for (var i = 0; i < input.Length; i++)
            {
                var ch = input[i];

                if (quoteChar == '\0')
                {
                    if (char.IsWhiteSpace(ch))
                    {
                        if (start >= 0)
                        {
                            tokens.Add(input[start..i]);
                            start = -1;
                        }

                        continue;
                    }

                    if (start < 0)
                    {
                        start = i;
                    }

                    if (ch == '\'' || ch == '"')
                    {
                        quoteChar = ch;
                    }

                    continue;
                }

                if (ch == quoteChar)
                {
                    quoteChar = '\0';
                }
            }

            if (start >= 0)
            {
                tokens.Add(input[start..]);
            }

            return tokens;
        }
    }

    public sealed class Init : IModuleAssemblyInitializer, IModuleAssemblyCleanup
    {
        private static readonly MyPredictor Predictor = new();
        private static int _isRegistered;

        public void OnImport()
        {
            if (Interlocked.CompareExchange(ref _isRegistered, 1, 0) != 0)
            {
                return;
            }

            try
            {
                SubsystemManager.RegisterSubsystem(SubsystemKind.CommandPredictor, Predictor);
            }
            catch (Exception ex) when (IsAlreadyRegistered(ex))
            {
                Interlocked.Exchange(ref _isRegistered, 0);
            }
        }

        private static bool IsAlreadyRegistered(Exception ex) =>
            ex is ArgumentException or InvalidOperationException &&
            ex.Message.Contains("already registered", StringComparison.OrdinalIgnoreCase);

        public void OnRemove(PSModuleInfo psModuleInfo)
        {
            if (Interlocked.CompareExchange(ref _isRegistered, 0, 1) != 1)
            {
                return;
            }

            SubsystemManager.UnregisterSubsystem(SubsystemKind.CommandPredictor, new Guid(MyPredictor.PredictorGuid));
        }
    }
}
