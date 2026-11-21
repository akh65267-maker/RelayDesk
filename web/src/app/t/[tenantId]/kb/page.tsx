import Link from "next/link";
import { api, type ArticleSummary, type Tenant } from "@/lib/api";
import styles from "../../../page.module.css";

export default async function KnowledgeBase({ params }: { params: Promise<{ tenantId: string }> }) {
  const { tenantId } = await params;
  const [tenant, articles] = await Promise.all([
    api<Tenant>("/api/tenancy/tenant", { tenantId }),
    api<ArticleSummary[]>("/api/kb/articles", { tenantId }),
  ]);
  const canEdit = tenant.role !== "Agent";

  return (
    <section className={styles.stack}>
      <div className={styles.toolbar}>
        <h2>Knowledge base</h2>
        {canEdit && (
          <Link className={styles.primary} href={`/t/${tenantId}/kb/new`}>
            New article
          </Link>
        )}
      </div>
      <p className={styles.muted}>
        Published articles are what the AI uses to draft replies, and it cites them so you can check each answer.
      </p>
      {articles.length === 0 ? (
        <p className={styles.muted}>No articles yet{canEdit ? ". Start with your most common questions." : "."}</p>
      ) : (
        <ul className={styles.ticketList}>
          {articles.map((a) => (
            <li key={a.id}>
              <Link href={`/t/${tenantId}/kb/${a.id}`} className={styles.articleRow}>
                <span className={styles.ticketSubject}>{a.title}</span>
                <span className={`${styles.badge} ${a.status === "Published" ? styles.statusSolved : ""}`}>{a.status}</span>
                <time className={styles.muted} dateTime={a.updatedAt}>
                  {new Date(a.updatedAt).toLocaleString()}
                </time>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
