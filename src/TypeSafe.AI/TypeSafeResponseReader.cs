using System.Text.Json;
using System.Text.Json.Serialization;

namespace TypeSafe.AI;

internal sealed record SystemOneResponseWire
{
    public required string Model { get; init; }
    public required IReadOnlyDictionary<string, JsonElement> Answers { get; init; }
    public required TypeSafeUsage Usage { get; init; }
}

internal static class TypeSafeResponseReader
{
    public static async Task<SystemOneResponse> ReadAsync(Stream stream, string? requestId, CancellationToken token)
    {
        try
        {
            var wire = await JsonSerializer.DeserializeAsync(stream,
                TypeSafeJsonContext.Default.SystemOneResponseWire, token).ConfigureAwait(false)
                ?? throw new JsonException("The response must be an object.");
            var answers = new Dictionary<string, TypeSafeAnswer>(wire.Answers.Count);
            foreach (var (id, element) in wire.Answers)
            {
                if (element.ValueKind != JsonValueKind.Object ||
                    !element.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
                    throw new JsonException("An answer requires a string type discriminator.");
                var kind = type.GetString()!;
                answers.Add(id, kind is "noul" or "choice" or "score"
                    ? element.Deserialize(TypeSafeJsonContext.Default.TypeSafeAnswer)!
                    : new UnknownAnswer { Type = kind, Raw = element.Clone() });
            }
            return new SystemOneResponse(wire.Model, answers, wire.Usage, requestId);
        }
        catch (JsonException exception)
        {
            throw new TypeSafeProtocolException("The TypeSafe response has invalid JSON or an invalid structure.", exception);
        }
        catch (ArgumentException exception)
        {
            // Collection element nullability is not enforced by System.Text.Json.
            throw new TypeSafeProtocolException("The TypeSafe response contains an invalid required value.", exception);
        }
    }
}
