using System.Text.Json.Serialization;

namespace TypeSafe.AI;

/// <summary>An answer to a single question, identified by its "type" discriminator.</summary>
/// <remarks>Known kinds deserialize through the source-generated polymorphic contract;
/// unrecognized kinds never reach this hierarchy — the response decoder wraps them in
/// <see cref="UnknownAnswer"/>, matching the official SDKs' forward compatibility.</remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(NoulAnswer), "noul")]
[JsonDerivedType(typeof(ChoiceAnswer), "choice")]
[JsonDerivedType(typeof(ScoreAnswer), "score")]
public abstract record TypeSafeAnswer;

/// <summary>A yes/no answer: the probability of a yes answer from 0 to 1. Carries no separate confidence.</summary>
public sealed record NoulAnswer : TypeSafeAnswer
{
    public required double Noul { get; init; }
}

/// <summary>A multiple-choice answer with the top option, the full distribution, and confidence from 0 to 1.</summary>
public sealed record ChoiceAnswer : TypeSafeAnswer
{
    public required string Choice { get; init; }
    public required IReadOnlyDictionary<string, double> Probabilities { get; init; }
    public required double Confidence { get; init; }
}

/// <summary>A rubric answer: a probability-weighted score that may fall between levels, plus legend and distribution.</summary>
public sealed record ScoreAnswer : TypeSafeAnswer
{
    public required double Score { get; init; }
    public required IReadOnlyDictionary<string, string> Legend { get; init; }
    public required IReadOnlyDictionary<string, double> Probabilities { get; init; }
    public required double Confidence { get; init; }
}

/// <summary>An answer whose "type" is not recognized by this SDK version; excluded from grouped result maps.</summary>
public sealed record UnknownAnswer : TypeSafeAnswer
{
    /// <summary>The unrecognized discriminator value.</summary>
    public required string Type { get; init; }

    /// <summary>The original answer object, retained for diagnostics.</summary>
    public required System.Text.Json.JsonElement Raw { get; init; }
}

/// <summary>Validates decoded answers against the documented contract. Structural presence and JSON
/// types are enforced by the source-generated serializer's required members; this pass covers the
/// semantic rules: finite probabilities in [0, 1], distributions summing to 1 within tolerance,
/// and option/legend key consistency.</summary>
internal static class TypeSafeAnswerValidator
{
    /// <summary>Tolerance for probability distributions documented to sum to 1 but rounded in transit.</summary>
    internal const double ProbabilitySumTolerance = 0.01;

    public static void Validate(SystemOneResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.Model))
            throw new TypeSafeProtocolException("The TypeSafe response requires a nonempty 'model'.");
        if (response.Usage.InputTokens is < 0 || response.Usage.OutputTokens is < 0)
            throw new TypeSafeProtocolException("The TypeSafe usage token counts must be nonnegative.");
        foreach (var (id, answer) in response.Answers)
        {
            switch (answer)
            {
                case NoulAnswer noul:
                    Probability(id, "noul", noul.Noul);
                    break;
                case ChoiceAnswer choice:
                    Probability(id, "confidence", choice.Confidence);
                    Distribution(id, choice.Probabilities);
                    if (!choice.Probabilities.ContainsKey(choice.Choice))
                        throw new TypeSafeProtocolException($"The answer '{id}' choice '{choice.Choice}' is not a key of its probability distribution.");
                    break;
                case ScoreAnswer score:
                    Probability(id, "confidence", score.Confidence);
                    Distribution(id, score.Probabilities);
                    ValidateScore(id, score);
                    break;
            }
        }
    }

    private static void Probability(string id, string field, double value)
    {
        if (!double.IsFinite(value) || value is < 0.0 or > 1.0)
            throw new TypeSafeProtocolException($"The answer '{id}' field '{field}' must be a finite probability between 0 and 1.");
    }

    private static void Distribution(string id, IReadOnlyDictionary<string, double> probabilities)
    {
        if (probabilities.Count == 0)
            throw new TypeSafeProtocolException($"The answer '{id}' requires a nonempty 'probabilities' object.");
        double sum = 0;
        foreach (var (key, value) in probabilities)
        {
            if (!double.IsFinite(value) || value is < 0.0 or > 1.0)
                throw new TypeSafeProtocolException($"The answer '{id}' probability for '{key}' must be a finite value between 0 and 1.");
            sum += value;
        }
        if (Math.Abs(sum - 1.0) > ProbabilitySumTolerance)
            throw new TypeSafeProtocolException($"The answer '{id}' probabilities sum to {sum}, outside the tolerance of 1 ± {ProbabilitySumTolerance}.");
    }

    private static void ValidateScore(string id, ScoreAnswer score)
    {
        if (score.Legend.Count == 0)
            throw new TypeSafeProtocolException($"The score answer '{id}' requires a nonempty 'legend'.");
        var highestLevel = -1;
        foreach (var level in score.Probabilities.Keys)
        {
            if (!int.TryParse(level, out var parsed) || parsed < 0)
                throw new TypeSafeProtocolException($"The score answer '{id}' probability key '{level}' must be a nonnegative integer rubric level.");
            if (!score.Legend.ContainsKey(level))
                throw new TypeSafeProtocolException($"The score answer '{id}' probability key '{level}' has no legend entry.");
            highestLevel = Math.Max(highestLevel, parsed);
        }
        if (!double.IsFinite(score.Score) || score.Score < 0.0 || score.Score > highestLevel)
            throw new TypeSafeProtocolException($"The score answer '{id}' score {score.Score} falls outside its rubric levels [0, {highestLevel}].");
    }
}
