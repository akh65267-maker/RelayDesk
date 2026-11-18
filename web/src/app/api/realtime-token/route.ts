import { auth } from "@/auth";

/**
 * Hands the signed-in agent's short-lived access token to the browser's SignalR connection.
 * Same-origin only (no CORS), so other sites can't read it; SignalR calls this on every (re)connect,
 * which also picks up refreshed tokens.
 */
export async function GET() {
  const session = await auth();
  if (!session?.accessToken || session.error) return Response.json({ error: "unauthenticated" }, { status: 401 });
  return Response.json({ token: session.accessToken }, { headers: { "Cache-Control": "no-store" } });
}
