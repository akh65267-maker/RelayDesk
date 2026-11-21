import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import {
  api,
  ApiError,
  TICKET_PRIORITIES,
  TICKET_STATUSES,
  type Member,
  type TicketDetail,
} from "@/lib/api";
import { TicketLive, TypingTextarea } from "@/components/live/LiveWorkspace";
import { AiPanel } from "./AiPanel";
import { SubmitButton } from "@/components/SubmitButton";
import styles from "../../../../page.module.css";

/**
 * Shared by this page's server actions. Module-level on purpose: an inline server action only
 * captures the variables it closes over, so a helper nested in the component isn't available to it.
 */
async function mutate(tenantId: string, back: string, path: string, method: string, body: unknown) {
  try {
    await api(path, { method, tenantId, body: JSON.stringify(body) });
  } catch (e) {
    const message = e instanceof ApiError ? e.detail : "Something went wrong.";
    redirect(`${back}?error=${encodeURIComponent(message)}`);
  }
  redirect(back);
}

export default async function TicketPage({
  params,
  searchParams,
}: {
  params: Promise<{ tenantId: string; ticketId: string }>;
  searchParams: Promise<{ error?: string }>;
}) {
  const { tenantId, ticketId } = await params;
  const { error } = await searchParams;
  const self = `/t/${tenantId}/tickets/${ticketId}`;

  let ticket: TicketDetail;
  try {
    ticket = await api<TicketDetail>(`/api/tickets/${ticketId}`, { tenantId });
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) notFound();
    throw e;
  }
  const members = await api<Member[]>("/api/tenancy/members", { tenantId });
  const emailOf = new Map(members.map((m) => [m.userId, m.email]));
  const emails = Object.fromEntries(emailOf);

  async function send(formData: FormData) {
    "use server";
    await mutate(tenantId, self, `/api/tickets/${ticketId}/messages`, "POST", {
      body: formData.get("body"),
      internal: formData.get("internal") === "on",
    });
  }

  async function update(formData: FormData) {
    "use server";
    const assignee = String(formData.get("assignee") ?? "");
    await mutate(tenantId, self, `/api/tickets/${ticketId}`, "PATCH", {
      status: formData.get("status"),
      priority: formData.get("priority"),
      ...(assignee ? { assigneeUserId: assignee } : { unassign: true }),
      tags: String(formData.get("tags") ?? "").split(","),
    });
  }

  return (
    <div className={styles.ticketLayout}>
      <section className={styles.stack}>
        <Link href={`/t/${tenantId}/tickets`}>← Inbox</Link>
        <h2>
          <span className={styles.muted}>#{ticket.number}</span> {ticket.subject}
        </h2>
        <p className={styles.muted}>
          From {[ticket.requesterName, ticket.requesterEmail && `<${ticket.requesterEmail}>`].filter(Boolean).join(" ") || "an anonymous chat visitor"}
        </p>

        <ol className={styles.thread}>
          {ticket.messages.map((m) => (
            <li key={m.id} className={m.isInternal ? styles.internal : m.authorType === "Customer" ? styles.customer : styles.agent}>
              <div className={styles.messageMeta}>
                <strong>
                  {m.authorType === "Customer"
                    ? ticket.requesterName || ticket.requesterEmail || "Visitor"
                    : emailOf.get(m.authorUserId ?? "") ?? "Former member"}
                </strong>
                {m.isInternal && <span className={styles.badge}>Internal note</span>}
                <time className={styles.muted} dateTime={m.createdAt}>
                  {new Date(m.createdAt).toLocaleString()}
                </time>
              </div>
              <p className={styles.messageBody}>{m.body}</p>
            </li>
          ))}
        </ol>

        <TicketLive ticketId={ticketId} emails={emails} />

        <form action={send} className={styles.form}>
          <label>
            Reply
            <TypingTextarea ticketId={ticketId} name="body" required rows={4} maxLength={20000} placeholder="Write a reply or an internal note" />
          </label>
          <label className={styles.checkbox}>
            <input type="checkbox" name="internal" /> Internal note (only visible to your team)
          </label>
          <SubmitButton className={styles.primary} pendingText="Sending…">Send</SubmitButton>
        </form>
        {error && <p className={styles.error}>{error}</p>}
      </section>

      <aside className={styles.panel}>
        <AiPanel tenantId={tenantId} ticket={ticket} />
        <form action={update} className={styles.form}>
          <label>
            Status
            <select name="status" defaultValue={ticket.status}>
              {TICKET_STATUSES.map((s) => (
                <option key={s}>{s}</option>
              ))}
            </select>
          </label>
          <label>
            Priority
            <select name="priority" defaultValue={ticket.priority}>
              {TICKET_PRIORITIES.map((p) => (
                <option key={p}>{p}</option>
              ))}
            </select>
          </label>
          <label>
            Assignee
            <select name="assignee" defaultValue={ticket.assigneeUserId ?? ""}>
              <option value="">Unassigned</option>
              {members.map((m) => (
                <option key={m.userId} value={m.userId}>
                  {m.email}
                </option>
              ))}
            </select>
          </label>
          <label>
            Tags
            <input name="tags" defaultValue={ticket.tags.join(", ")} placeholder="billing, login" />
          </label>
          <SubmitButton className={styles.secondary}>Save changes</SubmitButton>
        </form>
        <p className={styles.muted}>Created {new Date(ticket.createdAt).toLocaleString()}</p>
      </aside>
    </div>
  );
}
