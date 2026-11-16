import { redirect } from "next/navigation";
import { auth, signIn } from "@/auth";
import { api, ApiError } from "@/lib/api";
import styles from "../../page.module.css";

export default async function AcceptInvite({ params }: { params: Promise<{ token: string }> }) {
  const { token } = await params;
  const session = await auth();

  if (!session || session.error) {
    return (
      <main className={styles.center}>
        <h1>You&apos;re invited to Relay Desk</h1>
        <p>Sign in or create an account with the email address the invitation was sent to.</p>
        <form action={async () => { "use server"; await signIn("keycloak", { redirectTo: `/invite/${token}` }); }}>
          <button className={styles.primary}>Continue</button>
        </form>
      </main>
    );
  }

  let tenantId: string;
  try {
    ({ tenantId } = await api<{ tenantId: string }>("/api/tenancy/invitations/accept", {
      method: "POST",
      body: JSON.stringify({ token }),
    }));
  } catch (e) {
    const message = e instanceof ApiError && e.status === 409 ? "You're already a member." : "This invitation is invalid, expired, or for another email.";
    return (
      <main className={styles.center}>
        <h1>Invitation</h1>
        <p className={styles.error}>{message}</p>
      </main>
    );
  }
  redirect(`/t/${tenantId}`);
}
