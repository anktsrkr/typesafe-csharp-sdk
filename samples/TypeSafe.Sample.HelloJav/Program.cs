using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TypeSafe.AI;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddTypeSafeClient(x =>
{
    x.ApiKey = "xxx";
});
using var host = builder.Build();
await host.StartAsync();

var client = host.Services.GetRequiredService<ITypeSafeClient>();
var questions = Questions.Build(q => q
    .Noul("refund", "Is the customer asking for money back?")
    .Choice("team", "Which team should handle this?", "billing", "support")
    .Score("urgency", "How urgent is this?", "low", "medium", "high"));

var response = await client.SystemOneAsync(
    "I was charged twice for my subscription.", questions);
Console.WriteLine($"Refund probability: {response.Nouls["refund"].Noul:P0}");
Console.WriteLine($"Team: {response.Choices["team"].Choice}");
Console.WriteLine($"Urgency: {response.Scores["urgency"].Score}");
await host.StopAsync();