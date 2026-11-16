import Link from "next/link";
import { redirect } from "next/navigation";
import { auth, signIn, signOut } from "@/auth";
import { api, ApiError } from "@/lib/api";
import styles from "../../page.module.css";

export default async function AcceptInvite({ params }: { params: Promise<{ token: string }> }) {
  const { token } = await params;
  const session = await auth();

  async function signInToAccept() {
    "use server";
    await signIn("keycloak", { redirectTo: `/invite/${token}` });
  }

  // Ends both the app session and the Keycloak session, then returns to this invite signed out,
  // so the invitee gets Keycloak's full login page (with Register) instead of a re-authentication prompt.
  async function switchAccount() {
    "use server";
    await signOut({ redirect: false });
    const logout = new URL(`${process.env.AUTH_KEYCLOAK_ISSUER}/protocol/openid-connect/logout`);
    logout.searchParams.set("client_id", process.env.AUTH_KEYCLOAK_ID ?? "relay-web");
    logout.searchParams.set("post_logout_redirect_uri", `${process.env.AUTH_URL}/invite/${token}`);
    redirect(logout.toString());
  }

  if (!session || session.error) {
    return (
      <main className={styles.center}>
        <h1>You&apos;re invited to Relay Desk</h1>
        <p>Sign in or create an account with the email address the invitation was sent to.</p>
        <form action={signInToAccept}>
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
    if (e instanceof ApiError && e.status === 409) {
      return (
        <main className={styles.center}>
          <h1>Invitation</h1>
          <p>You&apos;re already a member of this workspace.</p>
          <Link href="/">Go to your workspaces</Link>
        </main>
      );
    }
    // Deliberately doesn't reveal which email the invitation is for.
    return (
      <main className={styles.center}>
        <h1>Invitation</h1>
        <p>
          You&apos;re signed in as <strong>{session.user?.email ?? "another account"}</strong>.
        </p>
        <p className={styles.error}>
          This invitation is for a different email address, or it has expired or already been used.
        </p>
        <form action={switchAccount}>
          <button className={styles.primary}>Use a different account</button>
        </form>
      </main>
    );
  }
  redirect(`/t/${tenantId}`);
}
