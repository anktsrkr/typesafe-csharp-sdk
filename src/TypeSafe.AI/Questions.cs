using System.Text.Json.Serialization;

namespace TypeSafe.AI;

/// <summary>Base record for the three question primitives; serialized with a "type" discriminator.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Noul), "noul")]
[JsonDerivedType(typeof(Choice), "choice")]
[JsonDerivedType(typeof(Score), "score")]
public abstract record TypeSafeQuestion
{
    /// <summary>Required text, object, or array describing the question.</summary>
    public required TypeSafeContent Instructions { get; init; }
}

/// <summary>A yes/no question: the answer approaches 1 for yes and 0 for no, near 0.5 is uncertainty.</summary>
public sealed record Noul : TypeSafeQuestion
{
    /// <summary>Optional descriptions of the yes and no outcomes.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NoulCriteria? Criteria { get; init; }
}

/// <summary>Optional descriptions of the yes and no outcomes; null leaves an outcome undescribed.</summary>
public sealed record NoulCriteria
{
    [JsonPropertyName("true")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TypeSafeContent? WhenTrue { get; init; }

    [JsonPropertyName("false")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TypeSafeContent? WhenFalse { get; init; }
}

/// <summary>A question that selects between named alternatives described by <see cref="Criteria"/>.</summary>
public sealed record Choice : TypeSafeQuestion
{
    /// <summary>Required, nonempty labels mapped to a description, or null for an undescribed label.</summary>
    public required IReadOnlyDictionary<string, TypeSafeContent?> Criteria { get; init; }
}

/// <summary>A question that assigns a score using the ordered rubric in <see cref="Criteria"/>.</summary>
public sealed record Score : TypeSafeQuestion
{
    /// <summary>Required, ordered rubric levels starting at zero; the wire contract requires at least two.</summary>
    public required IReadOnlyList<TypeSafeContent> Criteria { get; init; }
}
