# TypeSafe.AI

Unofficial .NET SDK for the TypeSafe System One API, mirroring the official Python and JavaScript SDKs. Send state and typed questions to Jev, TypeSafe's System One model, and get structured answers your code can act on directly: yes/no probabilities, choices with full distributions, and rubric scores — each call also returning confidence and token usage.

The library provides fluent question builders, a resilient HTTP client, and AOT-friendly JSON handling. See the [TypeSafe documentation](https://docs.typesafe.ai/) for the full API contract.

## Installation

```bash
dotnet add package TypeSafe.AI
```

## Features

- **Fluent Question Builders**: Build the three question primitives — `Noul` (yes/no), `Choice`, and `Score` — with strong typing and eager validation.
- **Resilience Pipelines**: Retry with exponential backoff and jitter, per-attempt and total timeout budgets via Microsoft.Extensions.Http.Resilience.
- **Dependency Injection**: `services.AddTypeSafeClient(...)` registers the typed client through the options pattern with `ValidateOnStart`.
- **Forward Compatibility**: Unrecognized answer kinds decode to `UnknownAnswer` instead of failing.
- **High Performance**: Native AOT friendly; all serialization goes through System.Text.Json source generation.

## Quick Start

```csharp
using Microsoft.Extensions.DependencyInjection;
using TypeSafe.AI;

var services = new ServiceCollection();

services.AddTypeSafeClient(options =>
{
    options.ApiKey = "your-api-key"; // or set the TYPESAFE_API_KEY environment variable
});

var client = services.BuildServiceProvider().GetRequiredService<ITypeSafeClient>();

var questions = Questions.Build(q => q
    .Noul("factual", "Is this response factual and helpful?")
    .Score("quality", "Rate the overall quality.", "poor", "fair", "good", "excellent"));

SystemOneResponse response = await client.SystemOneAsync("The state to evaluate...", questions);

double isFactual = response.Nouls["factual"].Noul;   // probability of yes, 0 to 1
ScoreAnswer quality = response.Scores["quality"];
Console.WriteLine($"Factual: {isFactual:P0}, Quality: {quality.Score:0.##} (confidence {quality.Confidence:P0})");
```

Without dependency injection, construct the client directly; it owns its handler chain and resilience pipeline:

```csharp
using TypeSafe.AI;

using var client = new TypeSafeClient(new TypeSafeClientOptions
{
    ApiKey = "your-api-key",
});
```

## Building Questions

Questions are keyed by caller-chosen ids; the ids are not sent to the model. `Questions.Build` takes a chained lambda, and every method returns the builder for fluency. Duplicate or blank ids throw immediately.

```csharp
var questions = Questions.Build(q => q
    .Noul("is_refund", "Is the customer requesting a refund?",
        whenTrue: "Money is being returned to the customer.",
        whenFalse: "No money changes hands.")
    .Choice("department", "Which department should handle this ticket?", opts => opts
        .Option("billing", "Invoices, subscriptions, payment failures.")
        .Option("technical", "Bugs, crashes, server errors.")
        .Option("sales", "Licensing, pricing, upgrades."))
    .Score("urgency", "How urgent is this ticket?", "can wait", "this week", "today"));
```

- **`Noul`** — a yes/no question. Optional `whenTrue`/`whenFalse` strings describe the outcomes; answers carry a single probability (`NoulAnswer.Noul`, 0 to 1, with values near 0.5 signaling uncertainty) and no separate confidence.
- **`Choice`** — select among labeled options, described through a `ChoiceOptions` lambda or plain labels. Answers carry the top `Choice`, the full `Probabilities` distribution, and `Confidence`.
- **`Score`** — assign a score against an ordered rubric of at least two levels, via plain level names or a `ScoreLevels` lambda. Answers carry a probability-weighted `Score` (which may fall between levels), the `Legend`, the `Probabilities` distribution, and `Confidence`.

Questions are evaluated in parallel and in isolation: adding more questions barely affects latency, and no answer depends on another. Batch everything the code might need into a single call — including speculative questions whose answers you may ignore — instead of chaining round trips ([speculative fan-out](https://docs.typesafe.ai/patterns/fan-out)). Keep each question atomic and let code compose the answers ([intent routing](https://docs.typesafe.ai/patterns/intent-routing)).

## Content and State

`SystemOneAsync` takes the state as a `TypeSafeContent`: a JSON string, object, or array. Plain strings convert implicitly, so a `string` can be passed directly. For structured state, build content from a `JsonNode` or from a strongly-typed value serialized through your own source-generated `JsonSerializerContext` (anonymous objects are unsupported because they require runtime reflection):

```csharp
TypeSafeContent text = "plain text state";
TypeSafeContent fromNode = TypeSafeContent.FromJson(JsonNode.Parse("""{"ticket":"TS-1042"}""")!);
TypeSafeContent fromTyped = TypeSafeContent.FromObject(ticket, MyAppJsonContext.Default.Ticket);
```

## Calling System One

`ITypeSafeClient.SystemOneAsync(state, questions, model = null, cancellationToken = default)` sends one request to `v1/systemone` and returns a `SystemOneResponse`:

- `Model` — the model that answered.
- `Answers` — every answer keyed by question id, including unrecognized kinds as `UnknownAnswer`.
- `Nouls`, `Choices`, `Scores` — the same answers grouped by type, with unrecognized kinds excluded.
- `Usage` — `InputTokens` and `OutputTokens`, null when the API did not report them.
- `RequestId` — the `x-typesafe-request-id` response header.

Pass `model` to override the configured default for a single call:

```csharp
var response = await client.SystemOneAsync(state, questions, model: "jev-latest", cancellationToken);
```

## Configuration

`AddTypeSafeClient` returns the `IHttpClientBuilder`, so hosts can customize the handler chain afterwards. Configuration precedence, lowest to highest:

1. Library defaults (model `jev-latest`, base URL `https://api.typesafe.ai/`).
2. Environment variables: `TYPESAFE_API_KEY`, `TYPESAFE_BASE_URL`, `TYPESAFE_DEFAULT_MODEL`.
3. The host configuration's `TypeSafe` section, when a configuration is passed.
4. The registration callback.

```csharp
services.AddTypeSafeClient(configuration, options =>
{
    // Final overrides, applied after environment and configuration sources.
});
```

The `TypeSafe` configuration section maps option names directly, including the `Retry` subsection:

```json
{
  "TypeSafe": {
    "ApiKey": "<secret>",
    "BaseUrl": "https://api.typesafe.ai/",
    "DefaultModel": "jev-latest",
    "AllowInsecureHttp": false,
    "MaxResponseBytes": 4194304,
    "Retry": {
      "MaxRetries": 2,
      "BackoffInitial": "00:00:00.500",
      "BackoffMax": "00:00:05",
      "UseJitter": true,
      "RespectRetryAfter": true,
      "PerAttemptTimeout": "00:00:10",
      "TotalTimeoutBudget": "00:00:30"
    }
  }
}
```

An empty string for `PerAttemptTimeout` or `TotalTimeoutBudget` disables that strategy. All options are validated at host startup and on first resolution; the base URL must be an absolute `https://` address ending in `/` (or explicit `AllowInsecureHttp` for local development). The typed client is transient — resolve `ITypeSafeClient` through DI rather than capturing it in an application singleton.

## Error Handling

All failures surface as `TypeSafeException` subclasses, so a single catch covers the library:

| Exception | Cause |
| --- | --- |
| `TypeSafeAuthenticationException` | HTTP 401 |
| `TypeSafeValidationException` | HTTP 422 |
| `TypeSafeRateLimitException` | HTTP 429 |
| `TypeSafeOverloadedException` | HTTP 529 |
| `TypeSafeException` with `StatusCode` | Any other unexpected status |
| `TypeSafeConnectionException` | The request failed before a response was received |
| `TypeSafeTimeoutException` | The per-attempt or total timeout budget elapsed |
| `TypeSafeProtocolException` | The response violated the contract (for example, oversized body or invalid answers) |

```csharp
try
{
    var response = await client.SystemOneAsync(state, questions, cancellationToken: cancellationToken);
}
catch (TypeSafeRateLimitException)
{
    // Back off and retry later; generated backoff and Retry-After are already honored inside the pipeline.
}
catch (TypeSafeException ex)
{
    Console.WriteLine($"TypeSafe call failed: {ex.Message} (status: {ex.StatusCode?.ToString() ?? "n/a"})");
}
```

## Observability

The library emits diagnostic activities through the `TypeSafe.AI` `ActivitySource`, tagged with the model, token usage, and request id. Tag values are metadata only — question text, state, credentials, and provider error bodies are never recorded.

## License

MIT
