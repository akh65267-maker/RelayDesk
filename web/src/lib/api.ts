import { redirect } from "next/navigation";
import { auth } from "@/auth";

const API_URL = process.env.RELAY_API_URL ?? "http://localhost:5000";

export type Role = "Owner" | "Admin" | "Agent";
export type Tenant = { id: string; name: string; slug: string; region: string; role: Role };
export type Member = { id: string; userId: string; email: string; role: Role; createdAt: string };

export const TICKET_STATUSES = ["New", "Open", "Pending", "Solved", "Closed"] as const;
export const TICKET_PRIORITIES = ["Low", "Normal", "High", "Urgent"] as const;
export type TicketStatus = (typeof TICKET_STATUSES)[number];
export type TicketPriority = (typeof TICKET_PRIORITIES)[number];

export type TicketSummary = {
  id: string;
  number: number;
  subject: string;
  status: TicketStatus;
  priority: TicketPriority;
  assigneeUserId: string | null;
  requesterEmail: string;
  tags: string[];
  createdAt: string;
  updatedAt: string;
};
export type TicketMessage = {
  id: string;
  authorType: "Agent" | "Customer";
  authorUserId: string | null;
  body: string;
  isInternal: boolean;
  createdAt: string;
};
export type TicketDetail = TicketSummary & {
  requesterName: string | null;
  createdBy: string;
  messages: TicketMessage[];
};
export type Page<T> = { items: T[]; total: number; page: number; pageSize: number };

export class ApiError extends Error {
  constructor(public status: number, public detail: string) {
    super(detail);
  }
}

/** Server-side call to the Relay API as the signed-in user, optionally inside a tenant. */
export async function api<T>(path: string, init: RequestInit & { tenantId?: string } = {}): Promise<T> {
  const session = await auth();
  if (!session?.accessToken || session.error) redirect("/");

  const { tenantId, headers, ...rest } = init;
  const res = await fetch(`${API_URL}${path}`, {
    ...rest,
    cache: "no-store",
    headers: {
      Authorization: `Bearer ${session.accessToken}`,
      "Content-Type": "application/json",
      ...(tenantId ? { "X-Tenant-Id": tenantId } : {}),
      ...headers,
    },
  });

  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new ApiError(res.status, body.detail ?? body.title ?? res.statusText);
  }
  return (res.status === 204 ? undefined : await res.json()) as T;
}
