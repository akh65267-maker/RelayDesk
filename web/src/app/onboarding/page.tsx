import { redirect } from "next/navigation";
import { api, ApiError, type Tenant } from "@/lib/api";
import styles from "../page.module.css";

async function createWorkspace(formData: FormData) {
  "use server";
  let tenant: Tenant;
  try {
    tenant = await api<Tenant>("/api/tenancy/tenants", {
      method: "POST",
      body: JSON.stringify({ name: formData.get("name"), slug: formData.get("slug") }),
    });
  } catch (e) {
    const message = e instanceof ApiError ? e.detail : "Something went wrong.";
    redirect(`/onboarding?error=${encodeURIComponent(message)}`);
  }
  redirect(`/t/${tenant.id}`);
}

export default async function Onboarding({ searchParams }: { searchParams: Promise<{ error?: string }> }) {
  const { error } = await searchParams;
  return (
    <main className={styles.center}>
      <h1>Create your workspace</h1>
      <form action={createWorkspace} className={styles.form}>
        <label>
          Company name
          <input name="name" required minLength={2} maxLength={100} />
        </label>
        <label>
          Workspace ID
          <input
            name="slug"
            required
            pattern="[a-z0-9](-?[a-z0-9]){2,39}"
            placeholder="nexa"
            title="3-40 lowercase letters, digits or single hyphens, e.g. nexa or nexa-support"
            aria-describedby="slug-hint"
            autoCapitalize="none"
            spellCheck={false}
          />
          <span id="slug-hint" className={styles.muted}>
            Lowercase letters, digits and hyphens, like <code>nexa</code>. Not a web address.
          </span>
        </label>
        {error && <p className={styles.error}>{error}</p>}
        <button className={styles.primary}>Create workspace</button>
      </form>
    </main>
  );
}
