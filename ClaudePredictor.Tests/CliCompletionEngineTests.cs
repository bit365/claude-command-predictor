using Xunit;

namespace ClaudePredictor.Tests;

public class CliCompletionEngineTests
{
    [Fact]
    public void Should_Be_Deterministic_For_Same_Input()
    {
        const string input = "claude --";

        var first = CliCompletionEngine.GetSuggestions(input, CancellationToken.None).ToArray();
        var second = CliCompletionEngine.GetSuggestions(input, CancellationToken.None).ToArray();

        Assert.Equal(first, second);
    }

    [Fact]
    public void Should_Not_Exceed_MaxSuggestionCount()
    {
        var suggestions = CliCompletionEngine.GetSuggestions("claude --", CancellationToken.None);

        Assert.True(suggestions.Count <= 24);
    }

    [Fact]
    public void Should_Handle_Fuzz_Inputs_Without_Throwing()
    {
        var random = new Random(20260424);
        var chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_=\"' /\\,";

        for (var i = 0; i < 200; i++)
        {
            var length = random.Next(0, 80);
            var buffer = new char[length];
            for (var j = 0; j < length; j++)
            {
                buffer[j] = chars[random.Next(chars.Length)];
            }

            var input = new string(buffer);
            var exception = Record.Exception(() => CliCompletionEngine.GetSuggestions(input, CancellationToken.None));
            Assert.Null(exception);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("git status")]
    [InlineData("claude \"")]
    [InlineData("claude '")]
    [InlineData("claude mcp --")]
    [InlineData("claude -- ")]
    public void Should_Not_Throw_On_Extreme_Or_Invalid_Inputs(string input)
    {
        var exception = Record.Exception(() => CliCompletionEngine.GetSuggestions(input, CancellationToken.None));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("claude ", "claude mcp")]
    [InlineData("claude mc", "claude mcp")]
    [InlineData("claude cp", "claude mcp")]
    [InlineData("claude udpate", "claude update")]
    [InlineData("claude mcp ", "claude mcp add")]
    [InlineData("claude mcp ", "claude mcp list")]
    [InlineData("claude auth ", "claude auth login")]
    [InlineData("claude auth ", "claude auth status")]
    public void Should_Narrow_Subcommands(string input, string expectedSuggestion)
    {
        var suggestions = CliCompletionEngine.GetSuggestions(input, CancellationToken.None);

        Assert.Contains(expectedSuggestion, suggestions, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Should_Prioritize_Subcommands_After_Root_Space()
    {
        var suggestions = CliCompletionEngine.GetSuggestions("claude ", CancellationToken.None);

        Assert.Contains("claude mcp", suggestions, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("claude --add-dir", suggestions, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Should_Not_Suggest_Root_Subcommands_After_Mode_Options()
    {
        var suggestions = CliCompletionEngine.GetSuggestions("claude -c -p ", CancellationToken.None);

        Assert.DoesNotContain("claude -c -p doctor", suggestions, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("claude -c -p mcp", suggestions, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Should_Not_Emit_Full_Option_List_After_Print_Mode_With_Trailing_Space()
    {
        var suggestions = CliCompletionEngine.GetSuggestions("claude -p ", CancellationToken.None);

        Assert.Empty(suggestions);
    }

    [Fact]
    public void Should_Still_Suggest_Long_Options_After_Print_Mode_When_Prefix_Typed()
    {
        var suggestions = CliCompletionEngine.GetSuggestions("claude -p --", CancellationToken.None);

        Assert.NotEmpty(suggestions);
        Assert.Contains("claude -p --add-dir", suggestions, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Should_Prioritize_Print_Mode_Options_When_Print_Mode_Is_Enabled()
    {
        var suggestions = CliCompletionEngine.GetSuggestions("claude -p --", CancellationToken.None).ToList();

        var outputFormatIndex = suggestions.FindIndex(static s => s.Equals("claude -p --output-format", StringComparison.OrdinalIgnoreCase));
        var addDirIndex = suggestions.FindIndex(static s => s.Equals("claude -p --add-dir", StringComparison.OrdinalIgnoreCase));

        Assert.True(outputFormatIndex >= 0);
        Assert.True(addDirIndex >= 0);
        Assert.True(outputFormatIndex < addDirIndex);
    }

    [Fact]
    public void Should_Suggest_Enum_Values_After_Space()
    {
        var suggestions = CliCompletionEngine.GetSuggestions("claude --output-format ", CancellationToken.None);

        Assert.Contains("claude --output-format text", suggestions, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("claude --output-format json", suggestions, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("claude --output-format stream-json", suggestions, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Should_Suggest_Inline_Assigned_Enum_Value()
    {
        var suggestions = CliCompletionEngine.GetSuggestions("claude --output-format=j", CancellationToken.None);

        Assert.Contains("claude --output-format=json", suggestions, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Should_Suggest_Short_Options_For_Dash_Prefix()
    {
        var suggestions = CliCompletionEngine.GetSuggestions("claude -", CancellationToken.None);

        Assert.Contains("claude -c", suggestions, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("claude --continue", suggestions, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Should_Return_Empty_For_Option_Terminator_Context()
    {
        var suggestions = CliCompletionEngine.GetSuggestions("claude -- ", CancellationToken.None);

        Assert.Empty(suggestions);
    }

    [Fact]
    public void Should_Suggest_Long_Options_For_DoubleDash_Prefix()
    {
        var suggestions = CliCompletionEngine.GetSuggestions("claude --", CancellationToken.None);

        Assert.Contains("claude --add-dir", suggestions, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Should_Support_Fuzzy_Long_Option_Matching()
    {
        var suggestions = CliCompletionEngine.GetSuggestions("claude --di", CancellationToken.None);

        Assert.Contains("claude --disable-slash-commands", suggestions, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("claude --add-dir", suggestions, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Should_Hide_Mutually_Exclusive_Option()
    {
        var suggestions = CliCompletionEngine.GetSuggestions("claude --chrome --n", CancellationToken.None);

        Assert.DoesNotContain("claude --no-chrome", suggestions, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Should_Not_Suggest_After_Leaf_Subcommand_Trailing_Space()
    {
        var suggestions = CliCompletionEngine.GetSuggestions("claude mcp remove ", CancellationToken.None);

        Assert.Empty(suggestions);
    }

    [Fact]
    public void Should_Not_Suggest_Options_When_Subcommand_Is_Required()
    {
        var suggestions = CliCompletionEngine.GetSuggestions("claude mcp --add-dir", CancellationToken.None);

        Assert.Empty(suggestions);
    }

    [Fact]
    public void Should_Not_Suggest_Options_After_Invalid_Positional_Dashes()
    {
        var suggestions = CliCompletionEngine.GetSuggestions("claude - - - -", CancellationToken.None);

        Assert.Empty(suggestions);
    }

    [Fact]
    public void Should_Suggest_Inline_Enum_On_Nested_Command()
    {
        var suggestions = CliCompletionEngine.GetSuggestions("claude mcp add --output-format=s", CancellationToken.None);

        Assert.Contains("claude mcp add --output-format=stream-json", suggestions, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Should_Suggest_Remaining_Comma_Separated_Enum_Values()
    {
        var suggestions = CliCompletionEngine.GetSuggestions("claude --setting-sources user,", CancellationToken.None);

        Assert.Contains("claude --setting-sources user,project", suggestions, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("claude --setting-sources user,local", suggestions, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("claude --setting-sources user,user", suggestions, StringComparer.OrdinalIgnoreCase);
    }
}
