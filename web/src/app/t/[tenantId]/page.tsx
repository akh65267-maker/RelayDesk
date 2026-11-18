import { redirect } from "next/navigation";

// The inbox is the workspace home.
export default async function WorkspaceHome({ params }: { params: Promise<{ tenantId: string }> }) {
  const { tenantId } = await params;
  redirect(`/t/${tenantId}/tickets`);
}
