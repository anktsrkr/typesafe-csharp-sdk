using System.Text.Json;
using System.Text.Json.Nodes;
using TypeSafe.AI;
using Xunit;

namespace TypeSafe.AI.Tests;

public class AdvancedStructureTests
{
    [Fact]
    public void StructuredInstructions_ObjectAndArrayShapes_SerializeCorrectly()
    {
        var instructionsObject = new JsonObject
        {
            ["document_type"] = "invoice",
            ["required_fields"] = new JsonArray { "invoice_id", "total_amount", "vendor_tax_id" },
            ["strict_mode"] = true
        };

        var questions = Questions.Build(q => q
            .Noul("invoice_valid", instructionsObject,
                whenTrue: "Document satisfies all invoice schema constraints",
                whenFalse: "Document misses required fields or has invalid format")
            .Choice("currency", new JsonArray { "Check primary currency symbol", "Look for ISO 4217 code" },
                "USD", "EUR", "GBP", "OTHER")
            .Score("compliance_grade", instructionsObject,
                "Non-compliant", "Partially compliant", "Fully compliant"));

        var request = SystemOneRequest.Create("Invoice text content...", questions, "jev-latest");
        var json = JsonSerializer.Serialize(request, TypeSafeJsonContext.Default.SystemOneRequest);

        Assert.Contains("\"document_type\":\"invoice\"", json);
        Assert.Contains("\"strict_mode\":true", json);
        Assert.Contains("\"invoice_valid\"", json);
        Assert.Contains("\"currency\"", json);
        Assert.Contains("\"compliance_grade\"", json);
    }

    [Fact]
    public void StructuredChoiceCriteria_TaxonomySubtreesAndRubrics_SerializeCorrectly()
    {
        var technicalSupportRubric = new JsonObject
        {
            ["summary"] = "Technical issues requiring engineering or infrastructure support",
            ["includes"] = new JsonArray { "API 500 errors", "Database connection timeouts", "SSL certificate expiry" },
            ["excludes"] = new JsonArray { "Password resets", "Billing invoices" }
        };

        var accountBillingRubric = new JsonObject
        {
            ["summary"] = "Subscription, billing, payment, and account access issues",
            ["includes"] = new JsonArray { "Credit card declines", "Refund requests", "Plan upgrades" },
            ["excludes"] = new JsonArray { "Bug reports", "API crashes" }
        };

        var questions = Questions.Build(q => q
            .Choice("department_routing", "Route incoming customer ticket to the correct department.", opts => opts
                .Option("engineering", technicalSupportRubric)
                .Option("billing", accountBillingRubric)
                .Option("general", "General inquiries that do not require specialized routing.")));

        var request = SystemOneRequest.Create(
            new JsonObject { ["ticket_id"] = 1234, ["body"] = "Our API calls to /v1/checkout are returning 500 Internal Error" },
            questions,
            "jev-latest");

        var json = JsonSerializer.Serialize(request, TypeSafeJsonContext.Default.SystemOneRequest);

        Assert.Contains("\"summary\":\"Technical issues requiring engineering or infrastructure support\"", json);
        Assert.Contains("\"API 500 errors\"", json);
        Assert.Contains("\"Credit card declines\"", json);
    }

    [Fact]
    public void StructuredScoreCriteria_MultiFactorRubricObjects_SerializeCorrectly()
    {
        var level0 = new JsonObject
        {
            ["tier"] = 0,
            ["summary"] = "Trivial change or documentation fix",
            ["signals"] = new JsonArray { "Markdown documentation updates only", "Fixes simple typos", "No logic modified" }
        };

        var level1 = new JsonObject
        {
            ["tier"] = 1,
            ["summary"] = "Isolated bug fix or minor enhancement",
            ["signals"] = new JsonArray { "Touches single service or component", "Under 100 LOC changed", "Unit tests added" }
        };

        var level2 = new JsonObject
        {
            ["tier"] = 2,
            ["summary"] = "Major architectural refactoring or security-critical change",
            ["signals"] = new JsonArray { "Alters public API contracts", "Changes authentication or encryption", "Over 500 LOC changed" }
        };

        var questions = Questions.Build(q => q
            .Score("pull_request_risk", "Score the review risk and review rigor needed for this pull request.",
                level0, level1, level2));

        var request = SystemOneRequest.Create(
            new JsonObject
            {
                ["pr_number"] = 402,
                ["title"] = "Refactor token validation pipeline",
                ["diff_stats"] = new JsonObject { ["additions"] = 620, ["deletions"] = 140 }
            },
            questions,
            "jev-latest");

        var json = JsonSerializer.Serialize(request, TypeSafeJsonContext.Default.SystemOneRequest);

        Assert.Contains("\"tier\":0", json);
        Assert.Contains("\"tier\":1", json);
        Assert.Contains("\"tier\":2", json);
        Assert.Contains("\"Markdown documentation updates only\"", json);
        Assert.Contains("\"Alters public API contracts\"", json);
    }

    [Fact]
    public void StructuredNoulCriteria_SecurityPhishingDefinitionAndExamples_SerializeCorrectly()
    {
        var phishingCriteriaTrue = new JsonObject
        {
            ["definition"] = "Credential harvesting or deceptive communication impersonating internal IT or executive leadership",
            ["indicators"] = new JsonArray
            {
                "Requests immediate password reset via external link",
                "Urgent threat of account termination",
                "Mismatched reply-to email headers"
            },
            ["counter_indicators"] = new JsonArray
            {
                "Signed company newsletter with known internal domain"
            }
        };

        var phishingCriteriaFalse = new JsonObject
        {
            ["definition"] = "Legitimate business communication, routine system notification, or harmless vendor correspondence"
        };

        var questions = Questions.Build(q => q
            .Noul("is_phishing", "Determine whether this email poses an active credential phishing or social engineering threat.",
                phishingCriteriaTrue, phishingCriteriaFalse));

        var request = SystemOneRequest.Create(
            new JsonObject
            {
                ["subject"] = "URGENT: Verify your Microsoft 365 credentials now to prevent suspension",
                ["sender"] = "security-alert@microsoft-support-verify.com",
                ["body"] = "Click here within 2 hours or your account will be locked forever."
            },
            questions,
            "jev-latest");

        var json = JsonSerializer.Serialize(request, TypeSafeJsonContext.Default.SystemOneRequest);

        Assert.Contains("\"definition\":\"Credential harvesting", json);
        Assert.Contains("\"Requests immediate password reset via external link\"", json);
        Assert.Contains("\"is_phishing\"", json);
    }

    [Fact]
    public void StructuredState_ObjectAndArrayInSystemOneRequest_PreservesStructure()
    {
        var stateObj = new JsonObject
        {
            ["session_id"] = "sess-8821",
            ["user_role"] = "admin",
            ["recent_events"] = new JsonArray
            {
                new JsonObject { ["action"] = "login", ["ip"] = "192.168.1.1" },
                new JsonObject { ["action"] = "sudo_access", ["target"] = "prod-db" }
            }
        };

        var questions = Questions.Build(q => q.Noul("suspicious_activity", "Is the event sequence suspicious?"));
        var request = SystemOneRequest.Create(stateObj, questions, "jev-latest");

        var json = JsonSerializer.Serialize(request, TypeSafeJsonContext.Default.SystemOneRequest);
        Assert.Contains("\"session_id\":\"sess-8821\"", json);
        Assert.Contains("\"sudo_access\"", json);
    }
}
