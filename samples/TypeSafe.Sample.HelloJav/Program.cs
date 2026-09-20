using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TypeSafe.AI;

var builder = Host.CreateApplicationBuilder(args);

// Configure TypeSafeClient: reads TYPESAFE_API_KEY from environment variable or app configuration
builder.Services.AddTypeSafeClient(options =>
{
    options.ApiKey = Environment.GetEnvironmentVariable("TYPESAFE_API_KEY")
                     ?? builder.Configuration["TypeSafe:ApiKey"]
                     ?? builder.Configuration["TYPESAFE_API_KEY"]
                     ?? "your-typesafe-api-key";
});

var host = builder.Build();

var typeSafeClient = host.Services.GetRequiredService<ITypeSafeClient>();

var questions = Questions.Build(q => q
    .Noul("refund", "Is the customer asking for money back?"));

SystemOneResponse response = await typeSafeClient.SystemOneAsync("I was charged twice for my subscription.", questions);

double refundRequested = response.Nouls["refund"].Noul;
Console.WriteLine($"Refund requested score: {refundRequested}");
