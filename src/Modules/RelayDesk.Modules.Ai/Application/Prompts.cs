using System.Text;
using RelayDesk.SharedKernel.Ai;

namespace RelayDesk.Modules.Ai.Application;

/// <summary>
/// All prompts in one place. Changes here are covered by the eval suite (tests/RelayDesk.Evals), which CI
/// runs when this file changes. Every piece of ticket text passes through <see cref="PiiRedactor"/> first.
/// </summary>
internal static class Prompts
{
    public const string TriageSystem = """
        You triage customer-support tickets. Reply with JSON only, exactly this shape:
        {"category":"billing|technical|account|shipping|other","priority":"Low|Normal|High|Urgent","sentiment":"positive|neutral|negative","reason":"one short sentence"}
        Priority guide: Urgent = service down, data loss, security or legal risk; High = customer blocked or very upset;
        Normal = usual questions and problems; Low = feedback or nice-to-have.
        """;

    public const string ReplySystem = """
        You draft replies for a customer-support agent, who will review and edit them before sending.
        Use ONLY the numbered knowledge-base sources provided.
        Every sentence that uses a source MUST end with that source's number in square brackets, so the agent can check it.
        Example: "Refunds take 5 to 10 business days to appear [2]. Gift cards can't be refunded [2]."
        If the sources don't answer the question, say briefly that you'll check with the team, and don't guess:
        never invent prices, policies, dates or links. Be friendly, concise and specific, in the customer's language.
        Personal details in the conversation are hidden as [email], [phone], [card] or [iban]. Never write these
        or any other placeholder such as [Name] in your reply. Write only the reply text.
        """;

    public const string SummarySystem = """
        Summarise this support ticket for a teammate taking it over. Use 3 to 5 short bullet points:
        what the customer wants, what has been tried or promised, and what is still open.
        Internal notes are visible to the team, so you may use them. Write only the bullets.
        """;

    public static string Conversation(TicketForAi ticket, bool includeInternal)
    {
        var sb = new StringBuilder();
        sb.Append("Ticket #").Append(ticket.Number).Append(": ").AppendLine(PiiRedactor.Redact(ticket.Subject));
        foreach (var m in ticket.Messages.Where(m => includeInternal || !m.IsInternal))
        {
            var who = m.IsInternal ? "Internal note" : m.AuthorType == "Customer" ? "Customer" : "Agent";
            sb.Append("\n[").Append(who).Append("]\n").AppendLine(PiiRedactor.Redact(m.Body));
        }
        return sb.ToString();
    }

    public static string Sources(IReadOnlyList<KnowledgeHit> hits)
    {
        if (hits.Count == 0) return "(no knowledge-base sources found)";
        var sb = new StringBuilder();
        for (var i = 0; i < hits.Count; i++)
            sb.Append('[').Append(i + 1).Append("] ").AppendLine(hits[i].Title).AppendLine(hits[i].Content).AppendLine();
        return sb.ToString();
    }

    /// <summary>What to search the knowledge base for: the subject plus the customer's latest message.</summary>
    public static string RetrievalQuery(TicketForAi ticket)
    {
        var last = ticket.Messages.LastOrDefault(m => m.AuthorType == "Customer" && !m.IsInternal)?.Body ?? "";
        return PiiRedactor.Redact($"{ticket.Subject}\n{last}");
    }
}
