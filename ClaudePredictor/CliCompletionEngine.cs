using System.Collections.Frozen;

namespace ClaudePredictor;

internal static class CliCompletionEngine
{
    private const int MaxSuggestionCount = 24;
    private static readonly FrozenSet<string> PrintModePriorityOptions =
        new[]
        {
            "--output-format",
            "--input-format",
            "--max-turns",
            "--max-budget-usd",
            "--include-partial-messages",
            "--include-hook-events",
            "--replay-user-messages",
            "--no-session-persistence",
            "--json-schema",
            "--permission-prompt-tool"
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> GetSuggestions(string input, CancellationToken cancellationToken)
    {
        var tokens = CliTokenizer.Tokenize(input);
        if (tokens.Count == 0)
        {
            return [];
        }

        var hasTrailingWhitespace = HasTrailingWhitespace(input);
        var currentToken = hasTrailingWhitespace ? string.Empty : tokens[^1].Value;
        var context = CliParser.Parse(tokens, hasTrailingWhitespace);
        if (!context.IsClaudeCommand)
        {
            return [];
        }

        var availableOptions = CliParser.GetAvailableOptions(context.CommandPath);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (context.OptionTerminated)
        {
            return [];
        }

        if (TryHandlePendingOption(input, currentToken, availableOptions, context, result, cancellationToken, out var pendingSuggestions))
        {
            return pendingSuggestions;
        }

        if (TryHandleSubcommandPhase(input, currentToken, context, result, cancellationToken, out var subcommandSuggestions))
        {
            return subcommandSuggestions;
        }

        if (TryHandleInlineAssignedValuePhase(input, currentToken, hasTrailingWhitespace, availableOptions, result, cancellationToken, out var inlineAssignedSuggestions))
        {
            return inlineAssignedSuggestions;
        }

        if (context.HasPositionalArgument)
        {
            return [];
        }

        if (TryHandleOptionPrefixPhase(input, currentToken, availableOptions, context, result, cancellationToken, out var optionPrefixSuggestions))
        {
            return optionPrefixSuggestions;
        }

        if (TryHandleIdlePhase(input, currentToken, context, availableOptions, result, cancellationToken, out var idleSuggestions))
        {
            return idleSuggestions;
        }

        return [];
    }

    private static bool TryHandlePendingOption(
        string input,
        string currentToken,
        FrozenDictionary<string, OptionSpec> availableOptions,
        ParseContext context,
        HashSet<string> result,
        CancellationToken cancellationToken,
        out IReadOnlyList<string> suggestions)
    {
        if (context.PendingOption is not { } pendingOption)
        {
            suggestions = [];
            return false;
        }

        if (pendingOption.ValueArity == OptionValueArity.Multi && CliParser.IsOptionLike(currentToken))
        {
            AddOptionCandidates(input, currentToken, availableOptions, context.ConsumedOptions, result, cancellationToken);
        }
        else
        {
            AddValueCandidates(input, currentToken, pendingOption, null, null, result, cancellationToken);
        }

        suggestions = Limit(result);
        return true;
    }

    private static bool TryHandleSubcommandPhase(
        string input,
        string currentToken,
        ParseContext context,
        HashSet<string> result,
        CancellationToken cancellationToken,
        out IReadOnlyList<string> suggestions)
    {
        var allowSubcommandSuggestions = context.ExpectingSubcommand && context.ConsumedOptions.Count == 0;
        if (!allowSubcommandSuggestions)
        {
            suggestions = [];
            return false;
        }

        if (!CliParser.IsOptionLike(currentToken))
        {
            AddSubcommandCandidates(input, currentToken, context.Command.Subcommands.Keys, result, cancellationToken);
            if (result.Count == 0 && !string.IsNullOrEmpty(currentToken))
            {
                AddSubsequenceSubcommandCandidates(input, currentToken, context.Command.Subcommands.Keys, result, cancellationToken);
            }

            if (result.Count == 0 && !string.IsNullOrEmpty(currentToken))
            {
                AddClosestSubcommandCandidate(input, currentToken, context.Command.Subcommands.Keys, result);
            }

            if (result.Count > 0 || context.CommandPath.Count > 1)
            {
                suggestions = Limit(result);
                return true;
            }
        }

        if (context.CommandPath.Count > 1)
        {
            suggestions = [];
            return true;
        }

        suggestions = [];
        return false;
    }

    private static bool TryHandleInlineAssignedValuePhase(
        string input,
        string currentToken,
        bool hasTrailingWhitespace,
        FrozenDictionary<string, OptionSpec> availableOptions,
        HashSet<string> result,
        CancellationToken cancellationToken,
        out IReadOnlyList<string> suggestions)
    {
        if (hasTrailingWhitespace || !TryAddInlineAssignedValueCandidates(input, currentToken, availableOptions, result, cancellationToken))
        {
            suggestions = [];
            return false;
        }

        suggestions = Limit(result);
        return true;
    }

    private static bool TryHandleOptionPrefixPhase(
        string input,
        string currentToken,
        FrozenDictionary<string, OptionSpec> availableOptions,
        ParseContext context,
        HashSet<string> result,
        CancellationToken cancellationToken,
        out IReadOnlyList<string> suggestions)
    {
        if (!CliParser.IsOptionLike(currentToken))
        {
            suggestions = [];
            return false;
        }

        AddOptionCandidates(input, currentToken, availableOptions, context.ConsumedOptions, result, cancellationToken);
        suggestions = Limit(result);
        return true;
    }

    private static bool TryHandleIdlePhase(
        string input,
        string currentToken,
        ParseContext context,
        FrozenDictionary<string, OptionSpec> availableOptions,
        HashSet<string> result,
        CancellationToken cancellationToken,
        out IReadOnlyList<string> suggestions)
    {
        if (context.Command.Subcommands.Count == 0 && string.IsNullOrEmpty(currentToken))
        {
            suggestions = [];
            return true;
        }

        if (!string.IsNullOrEmpty(currentToken))
        {
            suggestions = [];
            return false;
        }

        if (context.ConsumedOptions.Count > 0)
        {
            suggestions = [];
            return true;
        }

        AddOptionCandidates(input, currentToken, availableOptions, context.ConsumedOptions, result, cancellationToken);
        suggestions = Limit(result);
        return true;
    }

    private static void AddSubcommandCandidates(
        string input,
        string currentToken,
        IEnumerable<string> subcommands,
        HashSet<string> result,
        CancellationToken cancellationToken) =>
        AddCandidates(input, currentToken, subcommands, quoteIfNeeded: false, result, cancellationToken);

    private static void AddSubsequenceSubcommandCandidates(
        string input,
        string currentToken,
        IEnumerable<string> subcommands,
        HashSet<string> result,
        CancellationToken cancellationToken)
    {
        if (currentToken.Length < 2)
        {
            return;
        }

        var baseInput = input[..^currentToken.Length];
        var ranked = subcommands
            .Select(candidate => (Candidate: candidate, Score: GetSubsequenceScore(currentToken, candidate)))
            .Where(static item => item.Score >= 0)
            .OrderBy(static item => item.Score)
            .ThenBy(static item => item.Candidate.Length)
            .ThenBy(static item => item.Candidate, StringComparer.OrdinalIgnoreCase);

        foreach (var (candidate, _) in ranked)
        {
            if (cancellationToken.IsCancellationRequested || result.Count >= MaxSuggestionCount)
            {
                return;
            }

            result.Add(baseInput + candidate);
        }
    }

    private static int GetSubsequenceScore(string pattern, string candidate)
    {
        var p = 0;
        var score = 0;

        for (var i = 0; i < candidate.Length && p < pattern.Length; i++)
        {
            if (char.ToLowerInvariant(candidate[i]) == char.ToLowerInvariant(pattern[p]))
            {
                p++;
            }
            else
            {
                score++;
            }
        }

        return p == pattern.Length ? score : -1;
    }

    private static void AddClosestSubcommandCandidate(
        string input,
        string currentToken,
        IEnumerable<string> subcommands,
        HashSet<string> result)
    {
        var normalized = currentToken.Trim();
        if (normalized.Length == 0)
        {
            return;
        }

        var (Candidate, Distance) = subcommands
            .Select(candidate => (Candidate: candidate, Distance: ComputeLevenshteinDistance(normalized, candidate)))
            .OrderBy(static item => item.Distance)
            .ThenBy(static item => item.Candidate, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (Candidate is null || Distance > 2)
        {
            return;
        }

        var baseInput = input[..^currentToken.Length];
        result.Add(baseInput + Candidate);
    }

    private static int ComputeLevenshteinDistance(string source, string target)
    {
        if (source.Length == 0)
        {
            return target.Length;
        }

        if (target.Length == 0)
        {
            return source.Length;
        }

        var previous = new int[target.Length + 1];
        var current = new int[target.Length + 1];

        for (var j = 0; j <= target.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= source.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= target.Length; j++)
            {
                var cost = char.ToLowerInvariant(source[i - 1]) == char.ToLowerInvariant(target[j - 1]) ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[target.Length];
    }

    private static bool TryAddInlineAssignedValueCandidates(
        string input,
        string currentToken,
        FrozenDictionary<string, OptionSpec> availableOptions,
        HashSet<string> result,
        CancellationToken cancellationToken)
    {
        var equalIndex = currentToken.IndexOf('=');
        if (equalIndex <= 0)
        {
            return false;
        }

        var optionName = currentToken[..equalIndex];
        if (!availableOptions.TryGetValue(optionName, out var option) || option.ValueArity == OptionValueArity.None)
        {
            return false;
        }

        var valuePrefix = currentToken[(equalIndex + 1)..];
        AddValueCandidates(input, valuePrefix, option, optionName, currentToken, result, cancellationToken);
        return true;
    }

    private static void AddOptionCandidates(
        string input,
        string currentToken,
        FrozenDictionary<string, OptionSpec> availableOptions,
        IReadOnlySet<string> consumedOptions,
        HashSet<string> result,
        CancellationToken cancellationToken)
    {
        var candidatePool = currentToken.StartsWith("--", StringComparison.Ordinal)
            ? availableOptions.Keys.Where(static name => name.StartsWith("--", StringComparison.Ordinal))
            : currentToken.StartsWith('-')
                ? availableOptions.Keys.Where(static name => name.StartsWith('-') && !name.StartsWith("--", StringComparison.Ordinal))
                : availableOptions.Keys.Where(static name => name.StartsWith("--", StringComparison.Ordinal));

        var filtered = candidatePool
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => IsOptionAvailable(name, availableOptions, consumedOptions))
            .OrderBy(name => GetOptionPriority(name, consumedOptions))
            .ThenBy(static name => name, StringComparer.OrdinalIgnoreCase);

        AddCandidates(input, currentToken, filtered, false, result, cancellationToken);

        if (!string.IsNullOrEmpty(currentToken) && result.Count < MaxSuggestionCount)
        {
            AddFuzzyOptionCandidates(input, currentToken, filtered, result, cancellationToken);
        }
    }

    private static void AddFuzzyOptionCandidates(
        string input,
        string currentToken,
        IEnumerable<string> candidates,
        HashSet<string> result,
        CancellationToken cancellationToken)
    {
        var pattern = currentToken.TrimStart('-');
        if (pattern.Length < 2)
        {
            return;
        }

        var baseInput = input[..^currentToken.Length];
        var ranked = candidates
            .Select(candidate => (Candidate: candidate, Score: GetOptionFuzzyScore(pattern, candidate.TrimStart('-'))))
            .Where(static item => item.Score >= 0)
            .OrderBy(static item => item.Score)
            .ThenBy(static item => item.Candidate.Length)
            .ThenBy(static item => item.Candidate, StringComparer.OrdinalIgnoreCase);

        foreach (var (candidate, _) in ranked)
        {
            if (cancellationToken.IsCancellationRequested || result.Count >= MaxSuggestionCount)
            {
                return;
            }

            result.Add(baseInput + candidate);
        }
    }

    private static int GetOptionFuzzyScore(string pattern, string candidate)
    {
        if (candidate.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var containsIndex = candidate.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (containsIndex >= 0)
        {
            return 10 + containsIndex;
        }

        var subsequenceScore = GetSubsequenceScore(pattern, candidate);
        if (subsequenceScore >= 0)
        {
            return 100 + subsequenceScore;
        }

        var distance = ComputeLevenshteinDistance(pattern, candidate);
        return distance <= 2 ? 200 + distance : -1;
    }

    private static bool IsOptionAvailable(string optionName, FrozenDictionary<string, OptionSpec> availableOptions, IReadOnlySet<string> consumedOptions)
    {
        if (!availableOptions.TryGetValue(optionName, out var option))
        {
            return false;
        }

        var primaryName = option.Names[0];
        if (!option.Repeatable && consumedOptions.Contains(primaryName))
        {
            return false;
        }

        return option.MutuallyExclusiveWith.All(exclusive => !consumedOptions.Contains(exclusive));
    }

    private static int GetOptionPriority(string optionName, IReadOnlySet<string> consumedOptions)
    {
        if (consumedOptions.Contains("--print") && PrintModePriorityOptions.Contains(optionName))
        {
            return 0;
        }

        return 1;
    }

    private static void AddValueCandidates(
        string input,
        string valuePrefix,
        OptionSpec option,
        string? assignedOption,
        string? assignedToken,
        HashSet<string> result,
        CancellationToken cancellationToken)
    {
        IEnumerable<string> values = option.ValueKind switch
        {
            OptionValueKind.Enum => GetEnumCandidates(option, valuePrefix),
            OptionValueKind.DirectoryPath => GetPathCandidates(valuePrefix, true),
            OptionValueKind.FilePath => GetPathCandidates(valuePrefix, false),
            _ => []
        };

        if (assignedOption is null)
        {
            AddCandidates(input, valuePrefix, values, true, result, cancellationToken);
            return;
        }

        AddAssignedCandidates(input, valuePrefix, assignedOption, assignedToken ?? string.Empty, values, result, cancellationToken);
    }

    private static IEnumerable<string> GetEnumCandidates(OptionSpec option, string prefix)
    {
        if (!option.IsCommaSeparatedEnum)
        {
            return option.EnumValues.Where(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        var segments = prefix.Split(',', StringSplitOptions.None);
        var selectedValues = segments.Length > 1 ? segments[..^1] : [];
        var currentSegment = segments[^1].Trim();
        var selectedSet = selectedValues
            .Select(static value => value.Trim())
            .Where(static value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var basePrefix = selectedValues.Length > 0 ? string.Join(',', selectedValues) + "," : string.Empty;

        return option.EnumValues
            .Where(value => !selectedSet.Contains(value))
            .Where(value => value.StartsWith(currentSegment, StringComparison.OrdinalIgnoreCase))
            .Select(value => basePrefix + value);
    }

    private static void AddCandidates(
        string input,
        string currentToken,
        IEnumerable<string> candidates,
        bool quoteIfNeeded,
        HashSet<string> result,
        CancellationToken cancellationToken)
    {
        var trailingWhitespace = HasTrailingWhitespace(input);

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

            var finalCandidate = quoteIfNeeded ? QuoteIfNeeded(candidate) : candidate;

            if (trailingWhitespace)
            {
                result.Add(input + finalCandidate);
            }
            else
            {
                var prefix = currentToken.Length == 0 ? input : input[..^currentToken.Length];
                result.Add(prefix + finalCandidate);
            }

            if (result.Count >= MaxSuggestionCount)
            {
                return;
            }
        }
    }

    private static void AddAssignedCandidates(
        string input,
        string valuePrefix,
        string optionName,
        string assignedToken,
        IEnumerable<string> candidates,
        HashSet<string> result,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(assignedToken) || !input.EndsWith(assignedToken, StringComparison.Ordinal))
        {
            return;
        }

        var inputPrefix = input[..^assignedToken.Length];

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

            result.Add($"{inputPrefix}{optionName}={QuoteIfNeeded(candidate)}");

            if (result.Count >= MaxSuggestionCount)
            {
                return;
            }
        }
    }

    private static IReadOnlyList<string> Limit(HashSet<string> suggestions) => [.. suggestions.Take(MaxSuggestionCount)];

    private static bool HasTrailingWhitespace(string input) =>
        !string.IsNullOrEmpty(input) && char.IsWhiteSpace(input[^1]);

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

            var baseDirectory = string.IsNullOrEmpty(parentPart)
                ? Environment.CurrentDirectory
                : Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, parentPart));

            if (!Directory.Exists(baseDirectory))
            {
                return [];
            }

            var list = new List<string>(MaxSuggestionCount);
            foreach (var path in Directory.EnumerateDirectories(baseDirectory))
            {
                var name = Path.GetFileName(path);
                if (!name.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var candidate = $"{parentPart}{name}{Path.DirectorySeparatorChar}";
                list.Add(quoteChar == '\0' ? candidate : QuoteWith(candidate, quoteChar));
                if (list.Count >= MaxSuggestionCount)
                {
                    return list;
                }
            }

            if (directoriesOnly)
            {
                return list;
            }

            foreach (var path in Directory.EnumerateFiles(baseDirectory))
            {
                var name = Path.GetFileName(path);
                if (!name.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var candidate = $"{parentPart}{name}";
                list.Add(quoteChar == '\0' ? candidate : QuoteWith(candidate, quoteChar));
                if (list.Count >= MaxSuggestionCount)
                {
                    return list;
                }
            }

            return list;
        }
        catch
        {
            return [];
        }
    }

    private static string QuoteWith(string value, char quoteChar) =>
        quoteChar == '\''
            ? $"'{value.Replace("'", "''")}'"
            : $"\"{value.Replace("\"", "\"\"")}\"";

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
}
