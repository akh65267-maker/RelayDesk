import Link from "next/link";
import { redirect } from "next/navigation";
import { UseDraftButton } from "@/components/UseDraftButton";
import { SubmitButton } from "@/components/SubmitButton";
import { api, ApiError, type Source, type Suggestion, type TicketDetail, type Triage } from "@/lib/api";
import styles from "../../../../page.module.css";

/** Module-level so the inline server actions below can call it (they only capture variables, not nested helpers). */
async function post(tenantId: string, back: string, path: string, method: string, body: unknown) {
  try {
    await api(path, { method, tenantId, body: JSON.stringify(body) });
  } catch (e) {
    redirect(`${back}?error=${encodeURIComponent(e instanceof ApiError ? e.detail : "Something went wrong.")}`);
  }
  redirect(back);
}

/**
 * AI assistance for one ticket (ADR 0008). Everything here is a suggestion: triage is applied only when the
 * agent clicks, and drafts go into the reply box for editing. Results arrive live through Realtime.
 */
export async function AiPanel({ tenantId, ticket }: { tenantId: string; ticket: TicketDetail }) {
  const back = `/t/${tenantId}/tickets/${ticket.id}`;
  const suggestions = await api<Suggestion[]>(`/api/ai/tickets/${ticket.id}/suggestions`, { tenantId });
  const of = (kind: Suggestion["kind"]) => suggestions.find((s) => s.kind === kind);
  const triage = of("Triage");
  const reply = of("Reply");
  const summary = of("Summary");

  async function request(formData: FormData) {
    "use server";
    await post(tenantId, back, `/api/ai/tickets/${ticket.id}/suggestions`, "POST", { kind: formData.get("kind") });
  }

  async function applyTriage(formData: FormData) {
    "use server";
    const category = String(formData.get("category"));
    await post(tenantId, back, `/api/tickets/${ticket.id}`, "PATCH", {
      priority: formData.get("priority"),
      tags: Array.from(new Set([...ticket.tags, category])),
    });
  }

  const t = triage?.status === "Ready" ? (triage.data as Triage) : null;
  const triageApplied = t && ticket.priority === t.priority && ticket.tags.includes(t.category);

  return (
    <section className={styles.aiCard} aria-label="AI assistant">
      <h3>AI assistant</h3>

      {triage && <Status s={triage} label="Triage" />}
      {t && (
        <form action={applyTriage} className={styles.stack} style={{ gap: 6 }}>
          <p>
            <span className={styles.badge}>{t.category}</span> <span className={styles.badge}>{t.priority}</span>{" "}
            <span className={styles.badge}>{t.sentiment}</span>
          </p>
          {t.reason && <p className={styles.muted}>{t.reason}</p>}
          <input type="hidden" name="priority" value={t.priority} />
          <input type="hidden" name="category" value={t.category} />
          {!triageApplied && <SubmitButton className={styles.secondary}>Apply priority and tag</SubmitButton>}
        </form>
      )}

      <div className={styles.aiActions}>
        <form action={request}>
          <input type="hidden" name="kind" value="Reply" />
          <SubmitButton className={styles.secondary} pendingText="Asking…">
            {reply ? "New draft reply" : "Draft a reply"}
          </SubmitButton>
        </form>
        <form action={request}>
          <input type="hidden" name="kind" value="Summary" />
          <SubmitButton className={styles.secondary} pendingText="Asking…">
            Summarize
          </SubmitButton>
        </form>
      </div>

      {reply && <Status s={reply} label="Draft reply" />}
      {reply?.status === "Ready" && reply.content && (
        <>
          <p className={styles.messageBody}>{reply.content}</p>
          {Array.isArray(reply.data) && reply.data.length > 0 && (
            <ol className={styles.sources} aria-label="Sources">
              {(reply.data as Source[]).map((s) => (
                <li key={s.n} value={s.n}>
                  <Link href={`/t/${tenantId}/kb/${s.articleId}`}>{s.title}</Link>
                </li>
              ))}
            </ol>
          )}
          <UseDraftButton text={reply.content} />
        </>
      )}

      {summary && <Status s={summary} label="Summary" />}
      {summary?.status === "Ready" && summary.content && <p className={styles.messageBody}>{summary.content}</p>}
    </section>
  );
}

function Status({ s, label }: { s: Suggestion; label: string }) {
  const text =
    s.status === "Pending"
      ? "Working on it… (local models can take a minute)"
      : s.status === "Failed"
        ? `Couldn't finish: ${s.error ?? "unknown error"}. Try again.`
        : s.status === "QuotaExceeded"
          ? "This workspace has used its AI allowance for the month."
          : null;
  return (
    <p className={s.status === "Failed" ? styles.error : styles.muted} aria-live="polite">
      <strong>{label}</strong>
      {text ? `: ${text}` : s.model ? ` · ${s.model}` : ""}
    </p>
  );
}
