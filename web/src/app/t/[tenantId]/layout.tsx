import Link from "next/link";
import { LiveWorkspace, OnlineAgents } from "@/components/live/LiveWorkspace";
import { api, type Member, type Tenant } from "@/lib/api";
import styles from "../../page.module.css";

export default async function WorkspaceLayout({
  children,
  params,
}: {
  children: React.ReactNode;
  params: Promise<{ tenantId: string }>;
}) {
  const { tenantId } = await params;
  const [tenant, members] = await Promise.all([
    api<Tenant>("/api/tenancy/tenant", { tenantId }),
    api<Member[]>("/api/tenancy/members", { tenantId }),
  ]);
  const emails = Object.fromEntries(members.map((m) => [m.userId, m.email]));

  return (
    <LiveWorkspace tenantId={tenantId} realtimeUrl={process.env.RELAY_REALTIME_PUBLIC_URL}>
      <div className={styles.page}>
        <header className={styles.header}>
          <h1>{tenant.name}</h1>
          <span className={styles.muted}>
            {tenant.slug} · {tenant.role}
          </span>
          <OnlineAgents emails={emails} />
          <nav className={styles.nav}>
            <Link href={`/t/${tenantId}/tickets`}>Inbox</Link>
            <Link href={`/t/${tenantId}/kb`}>Knowledge base</Link>
            <Link href={`/t/${tenantId}/team`}>Team</Link>
            <Link href={`/demo/${tenant.slug}`} target="_blank">
              Demo site
            </Link>
            <Link href="/">Switch workspace</Link>
          </nav>
        </header>
        {children}
      </div>
    </LiveWorkspace>
  );
}
