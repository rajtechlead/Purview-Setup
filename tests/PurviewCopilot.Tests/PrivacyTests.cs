using PurviewCopilot.Domain;
using PurviewCopilot.Domain.Privacy;
using Xunit;

namespace PurviewCopilot.Tests;

public class PrivacyTests
{
    [Fact]
    public void Detect_FindsEmailAndTenantIdentifier()
    {
        var matches = SensitiveDataDetector.Detect(
            "Contact alex@contoso.com about tenant 8b1a9953-1a15-4c2f-9a41-5f0b2c7d9e10.");

        Assert.Contains(matches, m => m.RuleId == "EMAIL" && m.Category == SensitiveCategory.Pii);
        Assert.Contains(matches, m => m.RuleId == "TENANT_GUID" && m.Category == SensitiveCategory.TenantIdentifier);
    }

    [Fact]
    public void Detect_ValidatesCreditCardsWithLuhn()
    {
        var valid = SensitiveDataDetector.Detect("Card 4111 1111 1111 1111 expires soon.");
        var invalid = SensitiveDataDetector.Detect("Number 4111 1111 1111 1112 is not a card.");

        Assert.Contains(valid, m => m.RuleId == "CREDIT_CARD");
        Assert.DoesNotContain(invalid, m => m.RuleId == "CREDIT_CARD");
    }

    [Fact]
    public void Detect_FindsSecretsAndCredentials()
    {
        var text = "Authorization: Bearer abcdefghijklmnopqrstuvwxyz012345 and AccountKey=abc123def456ghi789";
        var matches = SensitiveDataDetector.Detect(text);

        Assert.Contains(matches, m => m.Category == SensitiveCategory.Secret);
        Assert.Contains(matches, m => m.Category == SensitiveCategory.Credential);
    }

    [Fact]
    public void Detect_DoesNotDuplicateOverlappingMatches()
    {
        var matches = SensitiveDataDetector.Detect("admin@contoso.onmicrosoft.com");

        for (var i = 1; i < matches.Count; i++)
        {
            var previous = matches[i - 1];
            Assert.True(matches[i].Start >= previous.Start + previous.Length);
        }
    }

    [Fact]
    public void Tokenize_IsStableForRepeatedValues()
    {
        var service = new RedactionService(AiRedactionMode.Tokenize);
        var result = service.Apply("first alex@contoso.com then alex@contoso.com again");

        Assert.Equal(2, result.Matches.Count);
        Assert.Single(result.TokenMap);
        Assert.DoesNotContain("alex@contoso.com", result.Text);
        Assert.False(result.Blocked);
    }

    [Fact]
    public void Tokenize_CanBeRestoredLocally()
    {
        var service = new RedactionService(AiRedactionMode.Tokenize);
        var original = "Owner bob@fabrikam.com holds card 4111111111111111.";
        var result = service.Apply(original);
        var restored = RedactionService.Restore(result.Text, result.TokenMap);

        Assert.Equal(original, restored);
    }

    [Fact]
    public void Redact_RemovesValueEntirely()
    {
        var service = new RedactionService(AiRedactionMode.Redact);
        var result = service.Apply("Email jane.doe@contoso.com now");

        Assert.DoesNotContain("jane.doe@contoso.com", result.Text);
        Assert.Contains("[REDACTED:PII]", result.Text);
    }

    [Fact]
    public void Mask_KeepsOnlyLastFourCharacters()
    {
        var service = new RedactionService(AiRedactionMode.Mask);
        var result = service.Apply("token 1234567890");

        Assert.Contains("******7890", result.Text);
    }

    [Fact]
    public void Block_StopsPromptsContainingSecrets()
    {
        var service = new RedactionService(AiRedactionMode.Block);
        var result = service.Apply("use eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U to call the API");

        Assert.True(result.Blocked);
    }

    [Fact]
    public void Block_AllowsCleanText()
    {
        var service = new RedactionService(AiRedactionMode.Block);
        var result = service.Apply("Which DLP policies should be enforced first?");

        Assert.False(result.Blocked);
        Assert.False(result.ContainsSensitiveData);
    }

    [Fact]
    public void ScrubForLog_RemovesSecrets()
    {
        var scrubbed = RedactionService.ScrubForLog("password=SuperSecret123; next line");

        Assert.DoesNotContain("SuperSecret123", scrubbed);
        Assert.Contains("[REDACTED:SECRET]", scrubbed);
    }
}
