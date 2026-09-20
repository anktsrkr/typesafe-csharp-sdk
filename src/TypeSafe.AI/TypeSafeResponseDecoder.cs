using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TypeSafe.AI;

/// <summary>The wire shape of a system-one success response, decoded by the source-generated
/// serializer. Answers stay as raw elements so known kinds can be dispatched through the
/// polymorphic contract and unrecognized kinds can be skipped without failing.</summary>
/// <remarks>Implementation detail of <see cref="TypeSafeResponseDecoder"/>; public only so the
/// generated serializer context can expose its metadata.</remarks>
public sealed record SystemOneResponseWire
{
    [JsonPropertyName("model")] public required string Model { get; init; }

    [JsonPropertyName("answers")] public required IReadOnlyDictionary<string, JsonElement> Answers { get; init; }

    [JsonPropertyName("usage")] public required TypeSafeUsage Usage { get; init; }
}

/// <summary>
/// Decodes and validates a system-one success response. Malformed payloads raise
/// <see cref="TypeSafeProtocolException"/>; unknown additive fields and unknown answer kinds are permitted.
/// </summary>
/// <remarks>Public so consumers with custom transports can share the client's response validation.</remarks>
public static class TypeSafeResponseDecoder
{
    /// <summary>Decodes a system-one response body. <paramref name="requestId"/> carries the
    /// x-typesafe-request-id response header value, if any.</summary>
    public static SystemOneResponse DecodeSystemOne(string json, string? requestId = null)
    {
        SystemOneResponseWire wire;
        try
        {
            wire = JsonSerializer.Deserialize(json, TypeSafeJsonContext.Default.SystemOneResponseWire)
                ?? throw new TypeSafeProtocolException("The TypeSafe response is empty.");
        }
        catch (JsonException exception)
        {
            throw new TypeSafeProtocolException("The TypeSafe response is not valid JSON.", exception);
        }

        var response = new SystemOneResponse
        {
            Model = wire.Model,
            Answers = DecodeAnswers(wire.Answers),
            Usage = wire.Usage,
            RequestId = requestId
        };
        TypeSafeAnswerValidator.Validate(response);
        return response;
    }

    private static IReadOnlyDictionary<string, TypeSafeAnswer> DecodeAnswers(IReadOnlyDictionary<string, JsonElement> answers)
    {
        var decoded = new Dictionary<string, TypeSafeAnswer>(answers.Count);
        foreach (var (id, element) in answers)
            decoded[id] = DecodeAnswer(id, element);
        return new ReadOnlyDictionary<string, TypeSafeAnswer>(decoded);
    }

    private static TypeSafeAnswer DecodeAnswer(string id, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new TypeSafeProtocolException($"The answer '{id}' must be a JSON object.");
        if (!element.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
            throw new TypeSafeProtocolException($"The answer '{id}' is missing its string 'type' discriminator.");

        var kind = type.GetString()!;
        if (kind is not ("noul" or "choice" or "score"))
            return new UnknownAnswer { Type = kind, Raw = element.Clone() };

        try
        {
            return element.Deserialize(TypeSafeJsonContext.Default.TypeSafeAnswer)!;
        }
        catch (JsonException exception)
        {
            throw new TypeSafeProtocolException($"The answer '{id}' is malformed.", exception);
        }
    }
}
