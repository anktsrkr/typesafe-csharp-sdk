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
