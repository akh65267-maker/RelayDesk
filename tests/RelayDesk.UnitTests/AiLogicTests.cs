using RelayDesk.Modules.Ai.Application;
using RelayDesk.Modules.KnowledgeBase.Application;
using RelayDesk.SharedKernel.Ai;

namespace RelayDesk.UnitTests;

public class PiiRedactorTests
{
    [Theory]
    [InlineData("Write to maria.lopez@example.com please", "Write to [email] please")]
    [InlineData("Call me on +49 151 2345 6789 today", "Call me on [phone] today")]
    [InlineData("My number is (030) 123-45678", "My number is [phone]")]
    [InlineData("Card 4111 1111 1111 1111 was charged", "Card [card] was charged")]
    [InlineData("IBAN DE89 3704 0044 0532 0130 00 please refund", "IBAN [iban] please refund")]
    public void Redacts_personal_data(string input, string expected) =>
        Assert.Equal(expected, PiiRedactor.Redact(input));

    [Theory]
    [InlineData("My order #123456 hasn't arrived")]
    [InlineData("It happened between 2024 and 2026")]
    [InlineData("Invoice 2026-11-18 shows 21% VAT")]
    [InlineData("Tracking 1234567890123 (not a valid card number)")]
    public void Leaves_ordinary_numbers_alone(string input) =>
        Assert.Equal(input, PiiRedactor.Redact(input));
}

public class ChunkerTests
{
    [Fact]
    public void Splits_on_paragraphs_and_keeps_headings_with_their_text()
    {
        var md = "# Returns\n\nYou can return items within 30 days.\n\n# Shipping\n\nWe ship worldwide in 3-5 days.";

        var chunks = Chunker.Split(md);

        Assert.Equal(["Returns\n\nYou can return items within 30 days.", "Shipping\n\nWe ship worldwide in 3-5 days."], chunks);
    }

    [Fact]
    public void Merges_short_paragraphs_up_to_the_target_length()
    {
        var md = string.Join("\n\n", Enumerable.Range(1, 10).Select(i => $"Paragraph {i} " + new string('x', 150)));

        var chunks = Chunker.Split(md);

        Assert.True(chunks.Count is > 1 and < 10);
        Assert.All(chunks, c => Assert.True(c.Length <= Chunker.TargetLength + 200));
    }

    [Fact]
    public void Cuts_a_huge_paragraph_into_pieces_below_the_limit()
    {
        var md = string.Concat(Enumerable.Repeat("This is one long sentence about returns. ", 300));

        var chunks = Chunker.Split(md);

        Assert.True(chunks.Count > 5);
        Assert.All(chunks, c => Assert.True(c.Length <= Chunker.MaxLength));
    }
}

public class TriageParsingTests
{
    [Fact]
    public void Parses_valid_output_case_insensitively()
    {
        var t = AiAssistant.ParseTriage("""{"category":"Billing","priority":"high","sentiment":"NEGATIVE","reason":"Charged twice."}""");

        Assert.Equal(new TriageResult("billing", "High", "negative", "Charged twice."), t);
    }

    [Theory]
    [InlineData("""{"category":"refunds","priority":"P1","sentiment":"angry"}""")]
    [InlineData("{}")]
    [InlineData("")]
    public void Falls_back_to_safe_defaults_when_the_model_drifts(string json)
    {
        var t = AiAssistant.ParseTriage(json);

        Assert.Equal(("other", "Normal", "neutral"), (t.Category, t.Priority, t.Sentiment));
    }
}
