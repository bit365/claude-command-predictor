using System.Collections.Frozen;

namespace ClaudePredictor;

internal static class CliSpec
{
    public const string RootCommandName = "claude";

    public static readonly CommandSpec RootCommand = BuildRootCommand();

    public static readonly OptionSpec[] GlobalOptions =
    [
        OptionSpec.Flag("--allow-dangerously-skip-permissions"),
        OptionSpec.Flag("--bare"),
        OptionSpec.Flag("--chrome").WithMutualExclusions("--no-chrome"),
        OptionSpec.Flag("--continue", "-c"),
        OptionSpec.Flag("--dangerously-skip-permissions"),
        OptionSpec.Flag("--disable-slash-commands"),
        OptionSpec.Flag("--exclude-dynamic-system-prompt-sections"),
        OptionSpec.Flag("--fork-session"),
        OptionSpec.Flag("--ide"),
        OptionSpec.Flag("--init"),
        OptionSpec.Flag("--init-only"),
        OptionSpec.Flag("--include-hook-events"),
        OptionSpec.Flag("--include-partial-messages"),
        OptionSpec.Flag("--maintenance"),
        OptionSpec.Flag("--no-chrome").WithMutualExclusions("--chrome"),
        OptionSpec.Flag("--no-session-persistence"),
        OptionSpec.Flag("--print", "-p"),
        OptionSpec.Flag("--replay-user-messages"),
        OptionSpec.Flag("--strict-mcp-config"),
        OptionSpec.Flag("--teleport"),
        OptionSpec.Flag("--verbose"),
        OptionSpec.Flag("--version", "-v"),

        OptionSpec.FreeText("--agent"),
        OptionSpec.FreeText("--agents"),
        OptionSpec.FreeText("--append-system-prompt"),
        OptionSpec.FilePath("--append-system-prompt-file"),
        OptionSpec.MultiFreeText("--allowedTools"),
        OptionSpec.MultiFreeText("--betas"),
        OptionSpec.MultiFreeText("--channels"),
        OptionSpec.MultiFreeText("--dangerously-load-development-channels"),
        OptionSpec.FreeText("--debug"),
        OptionSpec.FilePath("--debug-file"),
        OptionSpec.MultiFreeText("--disallowedTools"),
        OptionSpec.Enum("--effort", ["low", "medium", "high", "xhigh", "max"]),
        OptionSpec.FreeText("--fallback-model"),
        OptionSpec.FreeText("--from-pr"),
        OptionSpec.Enum("--input-format", ["text", "stream-json"]),
        OptionSpec.FilePath("--json-schema"),
        OptionSpec.FreeText("--max-budget-usd"),
        OptionSpec.FreeText("--max-turns"),
        OptionSpec.MultiFilePath("--mcp-config"),
        OptionSpec.Enum("--model", ["sonnet", "opus", "haiku"]),
        OptionSpec.FreeText("--name", "-n"),
        OptionSpec.Enum("--output-format", ["text", "json", "stream-json"]),
        OptionSpec.Enum("--permission-mode", ["default", "acceptEdits", "plan", "auto", "dontAsk", "bypassPermissions"]),
        OptionSpec.FreeText("--permission-prompt-tool"),
        OptionSpec.MultiDirectoryPath("--plugin-dir"),
        OptionSpec.FreeText("--remote"),
        OptionSpec.FreeText("--remote-control", "--rc"),
        OptionSpec.FreeText("--remote-control-session-name-prefix"),
        OptionSpec.FreeText("--resume", "-r"),
        OptionSpec.FreeText("--session-id"),
        OptionSpec.CommaSeparatedEnum("--setting-sources", ["user", "project", "local"]),
        OptionSpec.FilePath("--settings"),
        OptionSpec.FreeText("--system-prompt").WithMutualExclusions("--system-prompt-file"),
        OptionSpec.FilePath("--system-prompt-file").WithMutualExclusions("--system-prompt"),
        OptionSpec.Enum("--teammate-mode", ["auto", "in-process", "tmux"]),
        OptionSpec.Enum("--tmux", ["classic"]),
        OptionSpec.FreeText("--tools"),
        OptionSpec.DirectoryPath("--worktree", "-w"),
        OptionSpec.MultiDirectoryPath("--add-dir")
    ];

    public static readonly FrozenDictionary<string, OptionSpec> GlobalOptionsByName = GlobalOptions
        .SelectMany(static option => option.Names.Select(name => (Name: name, Option: option)))
        .ToFrozenDictionary(static item => item.Name, static item => item.Option, StringComparer.OrdinalIgnoreCase);

    public static readonly string[] GlobalLongOptions =
    [
        .. GlobalOptions
            .SelectMany(static option => option.Names)
            .Where(static name => name.StartsWith("--", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
    ];

    public static readonly string[] GlobalShortOptions =
    [
        .. GlobalOptions
            .SelectMany(static option => option.Names)
            .Where(static name => name.StartsWith('-') && !name.StartsWith("--", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
    ];

    private static CommandSpec BuildRootCommand()
    {
        var pluginSubcommands = new[] { "disable", "enable", "install", "list", "marketplace", "uninstall", "update", "validate" };

        var authLogin = CommandSpec.Create("login", [], [OptionSpec.Flag("--console"), OptionSpec.Flag("--sso"), OptionSpec.FreeText("--email")]);
        var authStatus = CommandSpec.Create("status", [], [OptionSpec.Flag("--text")]);
        var auth = CommandSpec.Create("auth", ["login", "logout", "status"], authLogin, authStatus);

        return CommandSpec.Create(
            RootCommandName,
            ["agents", "doctor", "install", "setup-token", "update", "upgrade"],
            auth,
            CommandSpec.Create("auto-mode", ["defaults", "config", "critique"]),
            CommandSpec.Create("mcp", ["add", "add-from-claude-desktop", "add-json", "get", "list", "remove", "reset-project-choices", "serve"]),
            CommandSpec.Create("plugin", pluginSubcommands),
            CommandSpec.Create("plugins", pluginSubcommands),
            CommandSpec.Create("remote-control", []));
    }
}

internal sealed record CommandSpec(
    string Name,
    FrozenDictionary<string, CommandSpec> Subcommands,
    FrozenDictionary<string, OptionSpec> OptionsByName,
    string[] LongOptionNames,
    string[] ShortOptionNames,
    bool InheritParentOptions,
    bool AcceptsPositionalArguments)
{
    public static CommandSpec Create(
        string name,
        IEnumerable<string> leafSubcommands,
        params CommandSpec[] nestedSubcommands) =>
        Create(name, leafSubcommands, [], nestedSubcommands);

    public static CommandSpec Create(
        string name,
        IEnumerable<string> leafSubcommands,
        IEnumerable<OptionSpec>? options,
        params CommandSpec[] nestedSubcommands) =>
        Create(name, leafSubcommands, options, true, true, nestedSubcommands);

    public static CommandSpec Create(
        string name,
        IEnumerable<string> leafSubcommands,
        IEnumerable<OptionSpec>? options,
        bool inheritParentOptions = true,
        bool acceptsPositionalArguments = true,
        params CommandSpec[] nestedSubcommands)
    {
        var subcommandMap = leafSubcommands.ToDictionary(
            sub => sub,
            sub => Create(sub, Array.Empty<string>(), Array.Empty<OptionSpec>()),
            StringComparer.OrdinalIgnoreCase);
        foreach (var nestedSubcommand in nestedSubcommands)
        {
            subcommandMap[nestedSubcommand.Name] = nestedSubcommand;
        }

        var optionArray = options?.ToArray() ?? [];

        return new(
            name,
            subcommandMap.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            optionArray
                .SelectMany(static option => option.Names.Select(name => (Name: name, Option: option)))
                .ToFrozenDictionary(static item => item.Name, static item => item.Option, StringComparer.OrdinalIgnoreCase),
            [
                .. optionArray.SelectMany(static option => option.Names)
                    .Where(static name => name.StartsWith("--", StringComparison.Ordinal))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            ],
            [
                .. optionArray.SelectMany(static option => option.Names)
                    .Where(static name => name.StartsWith('-') && !name.StartsWith("--", StringComparison.Ordinal))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            ],
            inheritParentOptions,
            acceptsPositionalArguments);
    }
}

internal enum OptionValueKind
{
    None,
    Enum,
    FilePath,
    DirectoryPath,
    FreeText
}

internal enum OptionValueArity
{
    None,
    Single,
    Multi
}

internal sealed record OptionSpec(
    string[] Names,
    OptionValueKind ValueKind,
    OptionValueArity ValueArity,
    string[] EnumValues,
    bool IsCommaSeparatedEnum,
    bool Repeatable,
    string[] MutuallyExclusiveWith)
{
    public static OptionSpec Flag(params string[] names) => new(names, OptionValueKind.None, OptionValueArity.None, [], false, false, []);

    public static OptionSpec FreeText(params string[] names) => new(names, OptionValueKind.FreeText, OptionValueArity.Single, [], false, false, []);

    public static OptionSpec MultiFreeText(params string[] names) => new(names, OptionValueKind.FreeText, OptionValueArity.Multi, [], false, true, []);

    public static OptionSpec Enum(string name, string[] values) => new([name], OptionValueKind.Enum, OptionValueArity.Single, values, false, false, []);

    public static OptionSpec CommaSeparatedEnum(string name, string[] values) => new([name], OptionValueKind.Enum, OptionValueArity.Single, values, true, false, []);

    public static OptionSpec FilePath(params string[] names) => new(names, OptionValueKind.FilePath, OptionValueArity.Single, [], false, false, []);

    public static OptionSpec MultiFilePath(params string[] names) => new(names, OptionValueKind.FilePath, OptionValueArity.Multi, [], false, true, []);

    public static OptionSpec DirectoryPath(params string[] names) => new(names, OptionValueKind.DirectoryPath, OptionValueArity.Single, [], false, false, []);

    public static OptionSpec MultiDirectoryPath(params string[] names) => new(names, OptionValueKind.DirectoryPath, OptionValueArity.Multi, [], false, true, []);

    public OptionSpec WithMutualExclusions(params string[] optionNames) =>
        this with { MutuallyExclusiveWith = optionNames ?? [] };
}
