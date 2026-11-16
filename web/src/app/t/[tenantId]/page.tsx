import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { api, type Member, type Tenant } from "@/lib/api";
import styles from "../../page.module.css";

type InvitationCreated = { id: string; email: string; role: string; token: string };

export default async function TenantHome({
  params,
}: {
  params: Promise<{ tenantId: string }>;
}) {
  const { tenantId } = await params;
  const invite = (await cookies()).get("relay_invite")?.value;
  const appUrl = process.env.AUTH_URL ?? "";
  const [tenant, members] = await Promise.all([
    api<Tenant>("/api/tenancy/tenant", { tenantId }),
    api<Member[]>("/api/tenancy/members", { tenantId }),
  ]);
  const canInvite = tenant.role !== "Agent";

  async function inviteMember(formData: FormData) {
    "use server";
    const created = await api<InvitationCreated>("/api/tenancy/invitations", {
      method: "POST",
      tenantId,
      body: JSON.stringify({ email: formData.get("email"), role: formData.get("role") }),
    });
    // Email delivery comes later. Hand the link over in a short-lived httpOnly cookie, not the URL.
    (await cookies()).set("relay_invite", created.token, { httpOnly: true, sameSite: "lax", maxAge: 60, path: `/t/${tenantId}` });
    redirect(`/t/${tenantId}`);
  }

  return (
    <main className={styles.page}>
      <header className={styles.header}>
        <h1>{tenant.name}</h1>
        <span className={styles.muted}>
          {tenant.slug} · {tenant.role}
        </span>
        <Link href="/">Switch workspace</Link>
      </header>

      <section>
        <h2>Team</h2>
        <ul className={styles.list}>
          {members.map((m) => (
            <li key={m.id}>
              {m.email} <span className={styles.muted}>{m.role}</span>
            </li>
          ))}
        </ul>
      </section>

      {canInvite && (
        <section>
          <h2>Invite a teammate</h2>
          <form action={inviteMember} className={styles.form}>
            <input name="email" type="email" required placeholder="teammate@company.com" />
            <select name="role" defaultValue="Agent">
              <option>Agent</option>
              <option>Admin</option>
              {tenant.role === "Owner" && <option>Owner</option>}
            </select>
            <button className={styles.primary}>Create invite link</button>
          </form>
          {invite && (
            <p className={styles.notice}>
              Share this link (valid 7 days): <code>{`${appUrl}/invite/${invite}`}</code>
            </p>
          )}
        </section>
      )}
    </main>
  );
}
