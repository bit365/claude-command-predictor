using System.Management.Automation;
using System.Management.Automation.Subsystem;
using System.Management.Automation.Subsystem.Prediction;

namespace ClaudePredictor;

public sealed class MyPredictor : ICommandPredictor
{
    private static readonly Guid PredictorId = new(PredictorGuid);
    internal const string PredictorGuid = "8f0750d6-aafb-4d6b-9d90-a17d33522dbe";

    public Guid Id => PredictorId;

    public string Name => "ClaudePredictor";

    public string Description => "Predictive IntelliSense for Claude Code CLI.";

    public SuggestionPackage GetSuggestion(PredictionClient client, PredictionContext context, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return default!;
        }

        var input = context.InputAst?.Extent?.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return default!;
        }

        var trimmed = input.TrimStart();
        var leadingWhitespace = input[..(input.Length - trimmed.Length)];
        var suggestions = CliCompletionEngine.GetSuggestions(trimmed, cancellationToken);
        if (suggestions.Count == 0)
        {
            return default!;
        }

        var predictiveSuggestions = suggestions
            .Select(suggestion => new PredictiveSuggestion(leadingWhitespace + suggestion))
            .ToList();

        return new SuggestionPackage(predictiveSuggestions);
    }

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
