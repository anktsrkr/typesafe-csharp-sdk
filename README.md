# TypeSafe .NET SDK (TypeSafe.AI)

[![CI](https://github.com/anktsrkr/typesafe-csharp-sdk/actions/workflows/ci.yml/badge.svg)](https://github.com/anktsrkr/typesafe-csharp-sdk/actions/workflows/ci.yml)
[![Release & Publish](https://github.com/anktsrkr/typesafe-csharp-sdk/actions/workflows/release.yml/badge.svg)](https://github.com/anktsrkr/typesafe-csharp-sdk/actions/workflows/release.yml)
[![NuGet Version](https://img.shields.io/nuget/v/TypeSafe.AI.svg?style=flat&logo=nuget)](https://www.nuget.org/packages/TypeSafe.AI)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)

Unofficial, enterprise-ready **.NET SDK for TypeSafe AI** — providing high-throughput verification, deterministic question building, and evaluation for LLMs and AI agent workflows.

---

## ✨ Features

- **🚀 NativeAOT & Trimming Compatible**: Built with zero-reflection architecture and System.Text.Json source generation for blazing startup performance and minimal footprint.
- **🛡️ Type-Safe Verification**: Build structured questionnaires (Questions.Build) with support for numeric nouls, multiple choice, discriminators, and categorical classifications.
- **⚡ Enterprise Resilience**: Built-in HTTP resilience powered by Microsoft.Extensions.Http.Resilience (Polly v8) with automated retries, rate-limit backoff, and circuit breakers.
- **📊 Observability & OpenTelemetry**: Native ActivitySource and Meter instruments emitting execution durations, token costs, and structured diagnostics.
- **🧩 Idiomatic .NET**: Seamless integration with Microsoft.Extensions.DependencyInjection and IConfiguration.

---

## 📦 Installation

### Stable Releases (NuGet.org)

`ash
dotnet add package TypeSafe.AI
`

### Preview / Beta Builds (GitHub Packages)

Preview builds are published on every pull request from GitHub Packages. To consume beta packages:

1. Add a 
uget.config to your repository root (see [
uget.config.example](nuget.config.example)):

`xml
<?xml version=1.0 encoding=utf-8?>
<configuration>
  <packageSources>
    <add key=nuget.org value=https://api.nuget.org/v3/index.json />
    <add key=github value=https://nuget.pkg.github.com/anktsrkr/index.json />
  </packageSources>
  <packageSourceCredentials>
    <github>
      <add key=Username value=YOUR_GITHUB_USERNAME />
      <add key=ClearTextPassword value=%GITHUB_TOKEN% />
    </github>
  </packageSourceCredentials>
</configuration>
`

2. Install with the --prerelease flag:

`ash
dotnet add package TypeSafe.AI --prerelease
`

---

## 🚀 Quickstart

### 1. Register TypeSafe Client in Dependency Injection

`csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TypeSafe.AI;

var builder = Host.CreateApplicationBuilder(args);

// Registers ITypeSafeClient with resilient HTTP client
builder.Services.AddTypeSafeClient(options =>
{
    options.ApiKey = Environment.GetEnvironmentVariable(TYPESAFE_API_KEY);
});

var host = builder.Build();
var client = host.Services.GetRequiredService<ITypeSafeClient>();
`

Alternatively, configure options via ppsettings.json:

`json
{
  TypeSafe: {
    ApiKey: YOUR_API_KEY,
    BaseAddress: https://api.typesafe.ai,
    TimeoutSeconds: 30
  }
}
`

And bind directly:

`csharp
builder.Services.AddTypeSafeClient(builder.Configuration.GetSection(TypeSafe));
`

---

### 2. Evaluate Prompts with System-One

`csharp
// Build a verification questionnaire
var questions = Questions.Build(q => q
    .Noul(refund, Is the customer asking for money back?)
    .MultipleChoice(urgency, What is the priority level?, [Low, Medium, High]));

// Execute evaluation
SystemOneResponse response = await client.SystemOneAsync(
    I was charged twice for my subscription and need this reversed immediately!, 
    questions
);

// Access strongly-typed responses
double refundConfidence = response.Nouls[refund].Noul;
string urgency = response.MultipleChoice[urgency].Selection;

Console.WriteLine($Refund Confidence: {refundConfidence:P0});
Console.WriteLine($Urgency: {urgency});
`

---

## 📂 Repository Structure

`
project-typesafe-csharp-sdk/
├── .github/
│   └── workflows/
│       ├── ci.yml                 # Build, test, and pack on PR / main
│       └── release.yml            # Release-please automated versioning & publishing
├── src/
│   └── TypeSafe.AI/               # Core SDK library (TypeSafe.AI NuGet package)
├── samples/
│   └── TypeSafe.Sample.HelloJav/  # Getting started console sample
├── tests/
│   └── TypeSafe.AI.Tests/         # 115 unit & integration tests
├── Directory.Build.props          # Global compiler, AOT, and packaging settings
├── Directory.Packages.props       # Central Package Management (CPM)
├── global.json                    # Pinned .NET 10 SDK version
├── nuget.config.example           # Example config for GitHub Packages
└── TypeSafe.slnx                  # Solution file
`

---

## 🛠️ Building & Testing Locally

Requirements:
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (pinned via global.json)

`ash
# Clone the repository
git clone https://github.com/anktsrkr/typesafe-csharp-sdk.git
cd typesafe-csharp-sdk

# Restore dependencies
dotnet restore

# Build solution
dotnet build --configuration Release --no-restore

# Run tests
dotnet test --configuration Release --no-build

# Pack NuGet package
dotnet pack --configuration Release --no-build --output artifacts/packages

# Run sample
dotnet run --project samples/TypeSafe.Sample.HelloJav/TypeSafe.Sample.HelloJav.csproj
`

---

## 🔄 CI/CD & Automated Releases

This repository utilizes automated continuous integration and trunk-based releases:
- **Continuous Integration (ci.yml)**: Restores, builds in Release mode, runs all unit tests, and validates NuGet packing on every PR and commit.
- **Release Please (elease.yml)**: Tracks [Conventional Commits](https://www.conventionalcommits.org/) (eat:, ix:) to automatically maintain CHANGELOG.md and bump versions via automated Release PRs.
- **Beta Channel**: Preview packages are published to GitHub Packages upon opening/updating a Release PR.
- **General Availability (GA)**: Once the Release PR is merged into main, production .nupkg and .snupkg packages are pushed to [NuGet.org](https://www.nuget.org/packages/TypeSafe.AI).

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).
