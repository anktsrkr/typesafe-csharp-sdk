using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace TypeSafe.AI;

/// <summary>Token usage reported for a request; each count is null when the API did not report it.</summary>
public sealed record TypeSafeUsage
{
    [JsonPropertyName("input_tokens")] public int? InputTokens { get; init; }

    [JsonPropertyName("output_tokens")] public int? OutputTokens { get; init; }
}

/// <summary>The system-one request body. Use <see cref="Create"/> to validate and snapshot inputs.</summary>
public sealed record SystemOneRequest
{
    /// <summary>The content to evaluate: a string, object, or array.</summary>
    public required TypeSafeContent State { get; init; }

    /// <summary>The model that answers the questions, such as "jev-latest".</summary>
    public required string Model { get; init; }

    /// <summary>Questions keyed by caller-chosen ids; the ids are not sent to the model.</summary>
    public required IReadOnlyDictionary<string, TypeSafeQuestion> Questions { get; init; }

    /// <summary>Validates the inputs against the documented contract and returns a defensive
    /// snapshot: question collections are copied and content is immutable, so later caller
    /// mutations cannot change what is sent.</summary>
    public static SystemOneRequest Create(TypeSafeContent state,
        IReadOnlyDictionary<string, TypeSafeQuestion> questions, string model)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(questions);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (questions.Count == 0)
            throw new ArgumentException("At least one question is required.", nameof(questions));

        var snapshot = new Dictionary<string, TypeSafeQuestion>(questions.Count);
        foreach (var (id, question) in questions)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            snapshot[id] = question switch
            {
                null => throw new ArgumentException($"The question '{id}' is null.", nameof(questions)),
                Noul => question,
                Choice { Criteria: var criteria } => SnapshotChoice(id, question.Instructions, criteria),
                Score { Criteria: var levels } => SnapshotScore(id, question.Instructions, levels),
                _ => throw new ArgumentException($"The question '{id}' has an unsupported type {question.GetType().Name}.", nameof(questions))
            };
        }
        return new SystemOneRequest
        {
            State = state,
            Model = model,
            Questions = new ReadOnlyDictionary<string, TypeSafeQuestion>(snapshot)
        };
    }

    private static TypeSafeQuestion SnapshotChoice(string id, TypeSafeContent? instructions,
        IReadOnlyDictionary<string, TypeSafeContent?> criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        if (criteria.Count == 0)
            throw new ArgumentException($"The choice question '{id}' requires at least one option.");
        return new Choice
        {
            Instructions = instructions,
            Criteria = new ReadOnlyDictionary<string, TypeSafeContent?>(new Dictionary<string, TypeSafeContent?>(criteria))
        };
    }

    private static TypeSafeQuestion SnapshotScore(string id, TypeSafeContent? instructions,
        IReadOnlyList<TypeSafeContent> criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        if (criteria.Count < 2)
            throw new ArgumentException($"The score question '{id}' requires at least two rubric levels.");
        for (var index = 0; index < criteria.Count; index++)
        {
            if (criteria[index] is null)
                throw new ArgumentException($"The score question '{id}' has a null rubric level at index {index}.");
        }
        return new Score { Instructions = instructions, Criteria = Array.AsReadOnly(criteria.ToArray()) };
    }
}

/// <summary>A system-one reply: answers keyed by question id plus model and usage metadata.</summary>
public sealed record SystemOneResponse
{
    /// <summary>The model that answered.</summary>
    public required string Model { get; init; }

    /// <summary>All answers keyed by question id, including unrecognized answer kinds.</summary>
    public required IReadOnlyDictionary<string, TypeSafeAnswer> Answers { get; init; }

    /// <summary>Token usage; counts are null when the API did not report them.</summary>
    public required TypeSafeUsage Usage { get; init; }

    /// <summary>The x-typesafe-request-id response header value, attached by the client.</summary>
    public string? RequestId { get; init; }

    /// <summary>Yes/no answers keyed by question id; unrecognized kinds are excluded.</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, NoulAnswer> Nouls => Group<NoulAnswer>();

    /// <summary>Choice answers keyed by question id; unrecognized kinds are excluded.</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, ChoiceAnswer> Choices => Group<ChoiceAnswer>();

    /// <summary>Score answers keyed by question id; unrecognized kinds are excluded.</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, ScoreAnswer> Scores => Group<ScoreAnswer>();

    private IReadOnlyDictionary<string, T> Group<T>() where T : TypeSafeAnswer
    {
        var grouped = new Dictionary<string, T>();
        foreach (var (id, answer) in Answers)
        {
            if (answer is T typed)
                grouped[id] = typed;
        }
        return new ReadOnlyDictionary<string, T>(grouped);
    }
}

/// <summary>The general-purpose TypeSafe System One client, mirroring the official SDKs.</summary>
public interface ITypeSafeClient
{
    /// <summary>Evaluates the state against the questions in one upstream request.</summary>
    /// <param name="state">The content to evaluate: a string, object, or array.</param>
    /// <param name="questions">Questions keyed by caller-chosen ids; must be nonempty.</param>
    /// <param name="model">Per-call model override; null uses the configured default.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    Task<SystemOneResponse> SystemOneAsync(TypeSafeContent state,
        IReadOnlyDictionary<string, TypeSafeQuestion> questions, string? model = null,
        CancellationToken cancellationToken = default);
}
