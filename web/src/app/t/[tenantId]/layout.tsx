import Link from "next/link";
import { api, type Tenant } from "@/lib/api";
import styles from "../../page.module.css";

export default async function WorkspaceLayout({
  children,
  params,
}: {
  children: React.ReactNode;
  params: Promise<{ tenantId: string }>;
}) {
  const { tenantId } = await params;
  const tenant = await api<Tenant>("/api/tenancy/tenant", { tenantId });

  return (
    <div className={styles.page}>
      <header className={styles.header}>
        <h1>{tenant.name}</h1>
        <span className={styles.muted}>
          {tenant.slug} · {tenant.role}
        </span>
        <nav className={styles.nav}>
          <Link href={`/t/${tenantId}/tickets`}>Inbox</Link>
          <Link href={`/t/${tenantId}/team`}>Team</Link>
          <Link href="/">Switch workspace</Link>
        </nav>
      </header>
      {children}
    </div>
  );
}
