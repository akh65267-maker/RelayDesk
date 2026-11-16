import NextAuth from "next-auth";
import Keycloak from "next-auth/providers/keycloak";

// ADR 0005: relay-web is a public OIDC client (PKCE, no secret). Tokens stay server-side in the
// encrypted session cookie; the browser never sees the access token.
export const { handlers, auth, signIn, signOut } = NextAuth({
  providers: [
    Keycloak({
      clientId: process.env.AUTH_KEYCLOAK_ID ?? "relay-web",
      issuer: process.env.AUTH_KEYCLOAK_ISSUER,
      client: { token_endpoint_auth_method: "none" },
      checks: ["pkce", "state"],
    }),
  ],
  session: { strategy: "jwt" },
  callbacks: {
    async jwt({ token, account }) {
      if (account) {
        return {
          ...token,
          accessToken: account.access_token,
          refreshToken: account.refresh_token,
          expiresAt: account.expires_at,
        };
      }
      if (token.expiresAt && Date.now() < (token.expiresAt - 30) * 1000) return token;
      return refresh(token);
    },
    async session({ session, token }) {
      session.accessToken = token.accessToken;
      session.error = token.error;
      return session;
    },
  },
});

async function refresh(token: import("next-auth/jwt").JWT) {
  if (!token.refreshToken) return { ...token, error: "RefreshTokenMissing" as const };
  const res = await fetch(`${process.env.AUTH_KEYCLOAK_ISSUER}/protocol/openid-connect/token`, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({
      grant_type: "refresh_token",
      client_id: process.env.AUTH_KEYCLOAK_ID ?? "relay-web",
      refresh_token: token.refreshToken,
    }),
  });
  if (!res.ok) return { ...token, error: "RefreshTokenError" as const };
  const t = await res.json();
  return {
    ...token,
    accessToken: t.access_token,
    refreshToken: t.refresh_token ?? token.refreshToken,
    expiresAt: Math.floor(Date.now() / 1000) + t.expires_in,
    error: undefined,
  };
}

declare module "next-auth" {
  interface Session {
    accessToken?: string;
    error?: "RefreshTokenMissing" | "RefreshTokenError";
  }
}

declare module "next-auth/jwt" {
  interface JWT {
    accessToken?: string;
    refreshToken?: string;
    expiresAt?: number;
    error?: "RefreshTokenMissing" | "RefreshTokenError";
  }
}
