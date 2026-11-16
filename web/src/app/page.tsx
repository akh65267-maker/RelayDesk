import Link from "next/link";
import { redirect } from "next/navigation";
import { auth, signIn, signOut } from "@/auth";
import { api, type Tenant } from "@/lib/api";
import styles from "./page.module.css";

export default async function Home() {
  const session = await auth();

  if (!session || session.error) {
    return (
      <main className={styles.center}>
        <h1>Relay Desk</h1>
        <p>Customer support with live chat and AI assistance.</p>
        <form action={async () => { "use server"; await signIn("keycloak", { redirectTo: "/" }); }}>
          <button className={styles.primary}>Sign in or create an account</button>
        </form>
      </main>
    );
  }

  const tenants = await api<Tenant[]>("/api/tenancy/me/tenants");
  if (tenants.length === 0) redirect("/onboarding");

  return (
    <main className={styles.center}>
      <h1>Your workspaces</h1>
      <ul className={styles.list}>
        {tenants.map((t) => (
          <li key={t.id}>
            <Link href={`/t/${t.id}`}>{t.name}</Link> <span className={styles.muted}>{t.role}</span>
          </li>
        ))}
      </ul>
      <Link href="/onboarding">Create another workspace</Link>
      <form action={async () => { "use server"; await signOut({ redirectTo: "/" }); }}>
        <button className={styles.link}>Sign out</button>
      </form>
    </main>
  );
}
