"use client";

/** Copies an AI draft into the ticket's reply box for the agent to edit; it never sends anything itself. */
export function UseDraftButton({ text }: { text: string }) {
  return (
    <button
      type="button"
      style={{
        padding: "8px 14px",
        border: "1px solid var(--border)",
        borderRadius: 6,
        background: "var(--background)",
        color: "var(--foreground)",
        font: "inherit",
        cursor: "pointer",
      }}
      onClick={() => {
        const box = document.querySelector<HTMLTextAreaElement>('textarea[name="body"]');
        if (!box) return;
        box.value = text;
        box.dispatchEvent(new Event("input", { bubbles: true }));
        box.focus();
        box.scrollIntoView({ behavior: "smooth", block: "center" });
      }}
    >
      Use this draft
    </button>
  );
}
