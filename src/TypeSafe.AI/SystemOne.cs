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
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        return new SystemOneRequest { State = state, Model = model, Questions = SnapshotQuestions(questions) };
    }

    internal static IReadOnlyDictionary<string, TypeSafeQuestion> SnapshotQuestions(IReadOnlyDictionary<string, TypeSafeQuestion> questions)
    {
        ArgumentNullException.ThrowIfNull(questions);
        if (questions.Count == 0)
            throw new ArgumentException("At least one question is required.", nameof(questions));

        var snapshot = new Dictionary<string, TypeSafeQuestion>(questions.Count);
        foreach (var (id, question) in questions)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            if (question is null || question.Instructions is null)
                throw new ArgumentException($"The question '{id}' requires non-null instructions.", nameof(questions));
            snapshot[id] = question switch
            {
                null => throw new ArgumentException($"The question '{id}' is null.", nameof(questions)),
                Noul => question,
                Choice { Criteria: var criteria } => SnapshotChoice(id, question.Instructions, criteria),
                Score { Criteria: var levels } => SnapshotScore(id, question.Instructions, levels),
                _ => throw new ArgumentException($"The question '{id}' has an unsupported type {question.GetType().Name}.", nameof(questions))
            };
        }
        return new ReadOnlyDictionary<string, TypeSafeQuestion>(snapshot);
    }

    private static TypeSafeQuestion SnapshotChoice(string id, TypeSafeContent instructions,
        IReadOnlyDictionary<string, TypeSafeContent?> criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        if (criteria.Count is < 1 or > 255)
            throw new ArgumentException($"The choice question '{id}' requires between 1 and 255 options.");
        foreach (var label in criteria.Keys)
            ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return new Choice
        {
            Instructions = instructions,
            Criteria = new ReadOnlyDictionary<string, TypeSafeContent?>(new Dictionary<string, TypeSafeContent?>(criteria))
        };
    }

    private static TypeSafeQuestion SnapshotScore(string id, TypeSafeContent instructions,
        IReadOnlyList<TypeSafeContent> criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        if (criteria.Count is < 2 or > 10)
            throw new ArgumentException($"The score question '{id}' requires between 2 and 10 rubric levels.");
        for (var index = 0; index < criteria.Count; index++)
        {
            if (criteria[index] is null)
                throw new ArgumentException($"The score question '{id}' has a null rubric level at index {index}.");
        }
        return new Score { Instructions = instructions, Criteria = Array.AsReadOnly(criteria.ToArray()) };
    }
}

/// <summary>An immutable snapshot of the provider's answers. IDs and semantic values are returned as received.</summary>
public sealed class SystemOneResponse
{
    public string Model { get; }
    public IReadOnlyDictionary<string, TypeSafeAnswer> Answers { get; }
    public TypeSafeUsage Usage { get; }
    public string? RequestId { get; }
    public IReadOnlyDictionary<string, NoulAnswer> Nouls { get; }
    public IReadOnlyDictionary<string, ChoiceAnswer> Choices { get; }
    public IReadOnlyDictionary<string, ScoreAnswer> Scores { get; }

    public SystemOneResponse(string model, IReadOnlyDictionary<string, TypeSafeAnswer> answers,
        TypeSafeUsage usage, string? requestId = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(usage);
        Model = model;
        Usage = usage;
        RequestId = requestId;
        var all = new Dictionary<string, TypeSafeAnswer>(answers.Count);
        var nouls = new Dictionary<string, NoulAnswer>();
        var choices = new Dictionary<string, ChoiceAnswer>();
        var scores = new Dictionary<string, ScoreAnswer>();
        foreach (var (id, answer) in answers)
        {
            ArgumentNullException.ThrowIfNull(answer);
            var copy = answer switch
            {
                ChoiceAnswer choice => Snapshot(choice),
                ScoreAnswer score => Snapshot(score),
                UnknownAnswer unknown => unknown with { Raw = unknown.Raw.Clone() },
                _ => answer
            };
            all.Add(id, copy);
            switch (copy)
            {
                case NoulAnswer noul: nouls.Add(id, noul); break;
                case ChoiceAnswer choice: choices.Add(id, choice); break;
                case ScoreAnswer score: scores.Add(id, score); break;
            }
        }
        Answers = new ReadOnlyDictionary<string, TypeSafeAnswer>(all);
        Nouls = new ReadOnlyDictionary<string, NoulAnswer>(nouls);
        Choices = new ReadOnlyDictionary<string, ChoiceAnswer>(choices);
        Scores = new ReadOnlyDictionary<string, ScoreAnswer>(scores);
    }

    private static ChoiceAnswer Snapshot(ChoiceAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer.Choice);
        return answer with { Probabilities = Copy(answer.Probabilities) };
    }

    private static ScoreAnswer Snapshot(ScoreAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer.Legend);
        foreach (var value in answer.Legend.Values) ArgumentNullException.ThrowIfNull(value);
        return answer with { Legend = Copy(answer.Legend), Probabilities = Copy(answer.Probabilities) };
    }

    private static IReadOnlyDictionary<string, T> Copy<T>(IReadOnlyDictionary<string, T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new ReadOnlyDictionary<string, T>(new Dictionary<string, T>(values));
    }
}

/// <summary>
/// The general-purpose TypeSafe System One client, mirroring the official SDKs.
/// When resolved via dependency injection, transport lifetimes are managed automatically
/// by <see cref="IHttpClientFactory"/> (do not dispose).
/// For standalone usage outside DI, instantiate <see cref="TypeSafeClient"/> directly
/// which implements <see cref="IDisposable"/>.
/// </summary>
public interface ITypeSafeClient
{
    /// <summary>Evaluates a pre-built, immutable System One request in one upstream call.</summary>
    /// <param name="request">The validated, immutable System One request.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The provider's answers, usage statistics, and request metadata.</returns>
    /// <exception cref="TypeSafeAuthenticationException">Authentication failed (401).</exception>
    /// <exception cref="TypeSafeValidationException">The request was rejected (422).</exception>
    /// <exception cref="TypeSafeRateLimitException">Rate limit exceeded (429).</exception>
    /// <exception cref="TypeSafeOverloadedException">Service temporarily overloaded (529).</exception>
    /// <exception cref="TypeSafeTimeoutException">Per-attempt or total timeout budget exceeded.</exception>
    /// <exception cref="TypeSafeConnectionException">Network transport or read failure.</exception>
    /// <exception cref="TypeSafeProtocolException">Invalid response format from the server.</exception>
    Task<SystemOneResponse> SystemOneAsync(SystemOneRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Evaluates the state against the questions in one upstream request.</summary>
    /// <param name="state">The content to evaluate: a string, object, or array.</param>
    /// <param name="questions">Questions keyed by caller-chosen ids; must be nonempty.</param>
    /// <param name="model">Per-call model override; null uses the configured default.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The provider's answers, usage statistics, and request metadata.</returns>
    /// <exception cref="TypeSafeAuthenticationException">Authentication failed (401).</exception>
    /// <exception cref="TypeSafeValidationException">The request was rejected (422).</exception>
    /// <exception cref="TypeSafeRateLimitException">Rate limit exceeded (429).</exception>
    /// <exception cref="TypeSafeOverloadedException">Service temporarily overloaded (529).</exception>
    /// <exception cref="TypeSafeTimeoutException">Per-attempt or total timeout budget exceeded.</exception>
    /// <exception cref="TypeSafeConnectionException">Network transport or read failure.</exception>
    /// <exception cref="TypeSafeProtocolException">Invalid response format from the server.</exception>
    Task<SystemOneResponse> SystemOneAsync(TypeSafeContent state,
        IReadOnlyDictionary<string, TypeSafeQuestion> questions, string? model = null,
        CancellationToken cancellationToken = default);
}
