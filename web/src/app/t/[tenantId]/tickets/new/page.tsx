import { redirect } from "next/navigation";
import { api, ApiError, TICKET_PRIORITIES, type TicketSummary } from "@/lib/api";
import { SubmitButton } from "@/components/SubmitButton";
import styles from "../../../../page.module.css";

export default async function NewTicket({
  params,
  searchParams,
}: {
  params: Promise<{ tenantId: string }>;
  searchParams: Promise<{ error?: string }>;
}) {
  const { tenantId } = await params;
  const { error } = await searchParams;

  async function create(formData: FormData) {
    "use server";
    let ticket: TicketSummary;
    try {
      ticket = await api<TicketSummary>("/api/tickets", {
        method: "POST",
        tenantId,
        body: JSON.stringify({
          subject: formData.get("subject"),
          body: formData.get("body"),
          requesterEmail: formData.get("requesterEmail"),
          requesterName: formData.get("requesterName") || null,
          priority: formData.get("priority"),
          tags: String(formData.get("tags") ?? "").split(","),
        }),
      });
    } catch (e) {
      const message = e instanceof ApiError ? e.detail : "Something went wrong.";
      redirect(`/t/${tenantId}/tickets/new?error=${encodeURIComponent(message)}`);
    }
    redirect(`/t/${tenantId}/tickets/${ticket.id}`);
  }

  return (
    <section className={styles.narrow}>
      <h2>New ticket</h2>
      <p className={styles.muted}>Log a request on a customer&apos;s behalf, e.g. from a phone call.</p>
      <form action={create} className={styles.form}>
        <label>
          Customer email
          <input name="requesterEmail" type="email" required maxLength={320} />
        </label>
        <label>
          Customer name <span className={styles.muted}>(optional)</span>
          <input name="requesterName" maxLength={200} />
        </label>
        <label>
          Subject
          <input name="subject" required maxLength={200} />
        </label>
        <label>
          What does the customer need?
          <textarea name="body" required rows={6} maxLength={20000} />
        </label>
        <label>
          Priority
          <select name="priority" defaultValue="Normal">
            {TICKET_PRIORITIES.map((p) => (
              <option key={p}>{p}</option>
            ))}
          </select>
        </label>
        <label>
          Tags <span className={styles.muted}>(comma-separated)</span>
          <input name="tags" placeholder="billing, login" />
        </label>
        {error && <p className={styles.error}>{error}</p>}
        <SubmitButton className={styles.primary} pendingText="Creating…">Create ticket</SubmitButton>
      </form>
    </section>
  );
}
