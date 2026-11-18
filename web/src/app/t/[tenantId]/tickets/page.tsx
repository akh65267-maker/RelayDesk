import Link from "next/link";
import { api, TICKET_STATUSES, type Member, type Page, type TicketSummary } from "@/lib/api";
import styles from "../../../page.module.css";

type Filters = { status?: string; assignee?: string; q?: string; page?: string };

export default async function Inbox({
  params,
  searchParams,
}: {
  params: Promise<{ tenantId: string }>;
  searchParams: Promise<Filters>;
}) {
  const { tenantId } = await params;
  const filters = await searchParams;
  const query = new URLSearchParams(
    Object.entries({ ...filters, pageSize: "25" }).filter(([, v]) => v) as [string, string][],
  );

  const [page, members] = await Promise.all([
    api<Page<TicketSummary>>(`/api/tickets?${query}`, { tenantId }),
    api<Member[]>("/api/tenancy/members", { tenantId }),
  ]);
  const emailOf = new Map(members.map((m) => [m.userId, m.email]));
  const current = page.page;
  const lastPage = Math.max(1, Math.ceil(page.total / page.pageSize));
  const pageLink = (p: number) => `?${new URLSearchParams({ ...filters, page: String(p) } as Record<string, string>)}`;

  return (
    <section className={styles.stack}>
      <div className={styles.toolbar}>
        <h2>Inbox</h2>
        <Link className={styles.primary} href={`/t/${tenantId}/tickets/new`}>
          New ticket
        </Link>
      </div>

      {/* Plain GET form: filters live in the URL, so views can be bookmarked and shared. */}
      <form className={styles.filters}>
        <select name="assignee" defaultValue={filters.assignee ?? ""} aria-label="Assignee">
          <option value="">Everyone</option>
          <option value="me">Assigned to me</option>
          <option value="unassigned">Unassigned</option>
        </select>
        <select name="status" defaultValue={filters.status ?? ""} aria-label="Status">
          <option value="">Any status</option>
          {TICKET_STATUSES.map((s) => (
            <option key={s}>{s}</option>
          ))}
        </select>
        <input name="q" defaultValue={filters.q ?? ""} placeholder="Search subject, email or #number" aria-label="Search" />
        <button className={styles.secondary}>Apply</button>
      </form>

      {page.items.length === 0 ? (
        <p className={styles.muted}>No tickets match these filters.</p>
      ) : (
        <ul className={styles.ticketList}>
          {page.items.map((t) => (
            <li key={t.id}>
              <Link href={`/t/${tenantId}/tickets/${t.id}`} className={styles.ticketRow}>
                <span className={styles.muted}>#{t.number}</span>
                <span className={styles.ticketSubject}>{t.subject}</span>
                <span className={`${styles.badge} ${styles[`status${t.status}`]}`}>{t.status}</span>
                <span className={`${styles.badge} ${styles[`priority${t.priority}`] ?? ""}`}>{t.priority}</span>
                <span className={`${styles.ticketMeta} ${styles.muted}`}>
                  <span>{t.assigneeUserId ? emailOf.get(t.assigneeUserId) ?? "Former member" : "Unassigned"}</span>
                  <span>{t.requesterEmail || "Chat visitor"}</span>
                  <time dateTime={t.updatedAt}>{new Date(t.updatedAt).toLocaleString()}</time>
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}

      {lastPage > 1 && (
        <nav className={styles.pager} aria-label="Pages">
          {current > 1 && <Link href={pageLink(current - 1)}>Previous</Link>}
          <span className={styles.muted}>
            Page {current} of {lastPage} · {page.total} tickets
          </span>
          {current < lastPage && <Link href={pageLink(current + 1)}>Next</Link>}
        </nav>
      )}
    </section>
  );
}
