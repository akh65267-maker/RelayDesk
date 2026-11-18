"use client";

import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from "@microsoft/signalr";
import { useRouter } from "next/navigation";
import { createContext, useCallback, useContext, useEffect, useRef, useState } from "react";

type Typing = { ticketId: string; who: "agent" | "customer"; userId?: string; at: number };

type Live = {
  connection: HubConnection | null;
  online: string[];
  typing: Typing[];
  connected: boolean;
};

const LiveContext = createContext<Live>({ connection: null, online: [], typing: [], connected: false });

export const useLive = () => useContext(LiveContext);

/**
 * One SignalR connection per workspace page (ADR 0007). Pushes only say "something changed";
 * the page re-fetches through the API, so what each agent sees is still decided by the API.
 */
export function LiveWorkspace({
  tenantId,
  realtimeUrl,
  children,
}: {
  tenantId: string;
  realtimeUrl: string | undefined;
  children: React.ReactNode;
}) {
  const router = useRouter();
  const [state, setState] = useState<Live>({ connection: null, online: [], typing: [], connected: false });
  const refreshTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Coalesce bursts (one save can emit several events) into a single refresh.
  const refresh = useCallback(() => {
    if (refreshTimer.current) clearTimeout(refreshTimer.current);
    refreshTimer.current = setTimeout(() => router.refresh(), 250);
  }, [router]);

  useEffect(() => {
    if (!realtimeUrl) return;
    const connection = new HubConnectionBuilder()
      .withUrl(`${realtimeUrl}/hubs/agent`, {
        withCredentials: false,
        accessTokenFactory: async () => {
          const res = await fetch("/api/realtime-token", { cache: "no-store" });
          if (!res.ok) throw new Error("Not signed in");
          return (await res.json()).token as string;
        },
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    const join = async () => {
      const online = await connection.invoke<string[]>("JoinWorkspace", tenantId);
      setState((s) => ({ ...s, online, connected: true }));
    };

    connection.on("ticketChanged", refresh);
    connection.on("presence", (online: string[]) => setState((s) => ({ ...s, online })));
    connection.on("typing", (t: Omit<Typing, "at">) =>
      setState((s) => ({
        ...s,
        typing: [...s.typing.filter((x) => !(x.ticketId === t.ticketId && x.who === t.who && x.userId === t.userId)), { ...t, at: Date.now() }],
      })),
    );
    connection.onreconnecting(() => setState((s) => ({ ...s, connected: false })));
    connection.onreconnected(async () => {
      await join();
      refresh(); // catch up on anything missed while disconnected
    });

    setState((s) => ({ ...s, connection }));
    connection.start().then(join).catch((e) => console.warn("[live] could not connect", e));

    // Drop stale typing indicators.
    const sweep = setInterval(
      () => setState((s) => (s.typing.length ? { ...s, typing: s.typing.filter((t) => Date.now() - t.at < 4000) } : s)),
      1000,
    );

    return () => {
      clearInterval(sweep);
      if (connection.state !== HubConnectionState.Disconnected) connection.stop();
    };
  }, [realtimeUrl, tenantId, refresh]);

  return <LiveContext.Provider value={state}>{children}</LiveContext.Provider>;
}

/** "● 2 online" with the names in a tooltip. */
export function OnlineAgents({ emails }: { emails: Record<string, string> }) {
  const { online, connected } = useLive();
  const names = online.map((id) => emails[id] ?? "Former member");
  return (
    <span title={connected ? names.join("\n") : "Reconnecting…"} style={{ fontSize: "0.875rem", color: "var(--muted)" }}>
      <span aria-hidden style={{ color: connected ? "var(--success)" : "var(--muted)" }}>●</span>{" "}
      {connected ? `${online.length} online` : "offline"}
    </span>
  );
}

/** Joins the ticket's group while the page is open and shows who is typing. */
export function TicketLive({ ticketId, emails }: { ticketId: string; emails: Record<string, string> }) {
  const { connection, connected, typing } = useLive();

  useEffect(() => {
    if (!connection || !connected) return;
    connection.invoke("ViewTicket", ticketId).catch(() => {});
    return () => {
      if (connection.state === HubConnectionState.Connected) connection.invoke("LeaveTicket", ticketId).catch(() => {});
    };
  }, [connection, connected, ticketId]);

  const who = typing
    .filter((t) => t.ticketId === ticketId)
    .map((t) => (t.who === "customer" ? "Customer" : emails[t.userId ?? ""] ?? "A teammate"));
  return (
    <p aria-live="polite" style={{ minHeight: "1.4em", fontSize: "0.875rem", color: "var(--muted)" }}>
      {who.length ? `${who.join(", ")} ${who.length > 1 ? "are" : "is"} typing…` : ""}
    </p>
  );
}

/** Reply box that tells the customer (and teammates) the agent is typing, at most every 2 seconds. */
export function TypingTextarea({ ticketId, ...props }: { ticketId: string } & React.TextareaHTMLAttributes<HTMLTextAreaElement>) {
  const { connection, connected } = useLive();
  const last = useRef(0);
  return (
    <textarea
      {...props}
      onInput={() => {
        if (!connection || !connected || Date.now() - last.current < 2000) return;
        last.current = Date.now();
        connection.invoke("Typing", ticketId).catch(() => {});
      }}
    />
  );
}
