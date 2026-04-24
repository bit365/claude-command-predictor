using System.Collections.Frozen;

namespace ClaudePredictor;

internal static class CliParser
{
    public static ParseContext Parse(IReadOnlyList<CliToken> tokens, bool hasTrailingWhitespace)
    {
        var context = CreateDefaultContext();

        var parseCount = hasTrailingWhitespace ? tokens.Count : Math.Max(0, tokens.Count - 1);
        if (parseCount == 0)
        {
            return context;
        }

        if (!string.Equals(tokens[0].Value, CliSpec.RootCommandName, StringComparison.OrdinalIgnoreCase))
        {
            return context with { IsClaudeCommand = false };
        }

        var commandPath = new List<CommandSpec> { CliSpec.RootCommand };
        var consumedOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentCommand = CliSpec.RootCommand;
        var pendingOption = (OptionSpec?)null;
        var optionTerminated = false;
        var hasPositionalArgument = false;

        for (var i = 1; i < parseCount; i++)
        {
            var token = tokens[i].Value;

            if (optionTerminated)
            {
                hasPositionalArgument = true;
                continue;
            }

            if (pendingOption is not null)
            {
                if (pendingOption.ValueArity == OptionValueArity.Multi && IsOptionLike(token))
                {
                    pendingOption = null;
                }
                else
                {
                    pendingOption = pendingOption.ValueArity == OptionValueArity.Multi ? pendingOption : null;
                    continue;
                }
            }

            if (token == "--")
            {
                optionTerminated = true;
                pendingOption = null;
                continue;
            }

            var availableOptions = GetAvailableOptions(commandPath);
            if (TryResolveOptionToken(token, availableOptions, out var option, out var hasInlineValue))
            {
                consumedOptions.Add(option.Names[0]);

                if (option.ValueArity == OptionValueArity.None)
                {
                    continue;
                }

                if (!hasInlineValue)
                {
                    pendingOption = option;
                    continue;
                }

                pendingOption = option.ValueArity == OptionValueArity.Multi ? option : null;
                continue;
            }

            if (currentCommand.Subcommands.TryGetValue(token, out var childCommand))
            {
                currentCommand = childCommand;
                commandPath.Add(childCommand);
                continue;
            }

            hasPositionalArgument = true;
        }

        var expectingSubcommand = !hasPositionalArgument && pendingOption is null && !optionTerminated && currentCommand.Subcommands.Count > 0;

        return new(
            currentCommand,
            [.. commandPath],
            pendingOption,
            optionTerminated,
            expectingSubcommand,
            consumedOptions.ToFrozenSet(StringComparer.OrdinalIgnoreCase),
            hasPositionalArgument);
    }

    public static FrozenDictionary<string, OptionSpec> GetAvailableOptions(IReadOnlyList<CommandSpec> commandPath)
    {
        var map = new Dictionary<string, OptionSpec>(StringComparer.OrdinalIgnoreCase);

        foreach (var command in commandPath)
        {
            foreach (var option in command.OptionsByName)
            {
                map[option.Key] = option.Value;
            }

            if (!command.InheritParentOptions)
            {
                map.Clear();
                foreach (var option in command.OptionsByName)
                {
                    map[option.Key] = option.Value;
                }
            }
        }

        foreach (var option in CliSpec.GlobalOptionsByName)
        {
            map[option.Key] = option.Value;
        }

        return map.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    public static bool TryResolveOptionToken(
        string token,
        IReadOnlyDictionary<string, OptionSpec> optionsByName,
        out OptionSpec option,
        out bool hasInlineValue)
    {
        hasInlineValue = false;
        var equalsIndex = token.IndexOf('=');
        if (equalsIndex > 0)
        {
            var optionName = token[..equalsIndex];
            if (optionsByName.TryGetValue(optionName, out option!))
            {
                hasInlineValue = true;
                return true;
            }

            option = null!;
            return false;
        }

        return optionsByName.TryGetValue(token, out option!);
    }

    public static bool IsOptionLike(string token) =>
        !string.IsNullOrEmpty(token) && token.StartsWith('-');

    private static ParseContext CreateDefaultContext() =>
        new(
            CliSpec.RootCommand,
            Array.Empty<CommandSpec>(),
            null,
            false,
            CliSpec.RootCommand.Subcommands.Count > 0,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase).ToFrozenSet(StringComparer.OrdinalIgnoreCase),
            false,
            true);

}

internal sealed record ParseContext(
    CommandSpec Command,
    IReadOnlyList<CommandSpec> CommandPath,
    OptionSpec? PendingOption,
    bool OptionTerminated,
    bool ExpectingSubcommand,
    FrozenSet<string> ConsumedOptions,
    bool HasPositionalArgument,
    bool IsClaudeCommand = true);
