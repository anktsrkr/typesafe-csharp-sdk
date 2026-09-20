using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TypeSafe.AI;

var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["TypeSafe:ApiKey"] = "local-smoke-key",
    ["TypeSafe:DefaultModel"] = "smoke-model",
    ["TypeSafe:Retry:MaxRetries"] = "0",
    ["TypeSafe:Retry:PerAttemptTimeout"] = "",
    ["TypeSafe:Retry:TotalTimeoutBudget"] = "00:00:05"
}).Build();
var services = new ServiceCollection();
services.AddTypeSafeClient(configuration).ConfigurePrimaryHttpMessageHandler(() => new FixtureHandler());
using var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<ITypeSafeClient>();
var questions = Questions.Build(q => q
    .Noul("refund", new JsonObject { ["question"] = "Refund requested?", ["context"] = new JsonArray("billing") })
    .Choice("team", "Which team?", "billing", "support")
    .Score("urgency", "How urgent?", s => s.Level(new JsonObject { ["label"] = "low" }).Level("high")));
var state = TypeSafeContent.FromObject(new Ticket("Charged twice", 2), SmokeJsonContext.Default.Ticket);
var result = await client.SystemOneAsync(state, questions);
if (result.Model != "smoke-model" || result.RequestId != "req-smoke" || result.Usage.InputTokens != 3 ||
    result.Nouls["refund"].Noul != 0.9 || result.Choices["team"].Choice != "billing" || result.Scores["urgency"].Score != 0.8 ||
    result.Answers["future"] is not UnknownAnswer unknown || unknown.Raw.GetProperty("data").GetInt32() != 42 ||
    !ReferenceEquals(result.Choices, result.Choices))
    throw new InvalidOperationException("Response smoke verification failed.");
Console.WriteLine("TypeSafe NativeAOT smoke passed: DI, configuration, structured content, all answers, unknown kinds.");

internal sealed record Ticket(string Text, int Attempts);
[JsonSerializable(typeof(Ticket))]
internal partial class SmokeJsonContext : JsonSerializerContext;

internal sealed class FixtureHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
        if (request.Headers.Authorization?.ToString() != "Bearer local-smoke-key" ||
            body.RootElement.GetProperty("model").GetString() != "smoke-model" ||
            body.RootElement.GetProperty("state").GetProperty("Attempts").GetInt32() != 2 ||
            body.RootElement.GetProperty("questions").GetProperty("refund").GetProperty("instructions").ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Request smoke verification failed.");
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"model":"smoke-model","answers":{
                  "refund":{"type":"noul","noul":0.9},
                  "team":{"type":"choice","choice":"billing","probabilities":{"billing":0.8,"support":0.2},"confidence":0.8},
                  "urgency":{"type":"score","score":0.8,"legend":{"0":"low","1":"high"},"probabilities":{"0":0.2,"1":0.8},"confidence":0.8},
                  "future":{"type":"future-kind","data":42}
                },"usage":{"input_tokens":3,"output_tokens":2},"additional":"ignored"}
                """)
        };
        response.Headers.Add("x-typesafe-request-id", "req-smoke");
        return response;
    }
}
