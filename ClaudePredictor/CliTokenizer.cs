namespace ClaudePredictor;

internal static class CliTokenizer
{
    public static IReadOnlyList<CliToken> Tokenize(string input)
    {
        var tokens = new List<CliToken>();
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
                        tokens.Add(new(input[start..i], start, i, IsCurrent: false, IsQuoted: IsQuoted(input[start])));
                        start = -1;
                    }

                    continue;
                }

                if (start < 0)
                {
                    start = i;
                }

                if (ch is '\'' or '"')
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
            tokens.Add(new(input[start..], start, input.Length, IsCurrent: true, IsQuoted: IsQuoted(input[start])));
        }

        return tokens;
    }

    private static bool IsQuoted(char ch) => ch is '\'' or '"';
}

internal readonly record struct CliToken(string Value, int Start, int End, bool IsCurrent, bool IsQuoted);
