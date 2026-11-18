"use client";

import { useFormStatus } from "react-dom";

/**
 * Submit button for server-action forms: disabled with a pending label while the action runs,
 * so slow requests show feedback and a double click can't submit twice (e.g. duplicate tickets).
 */
export function SubmitButton({
  children,
  pendingText = "Saving…",
  className,
}: {
  children: React.ReactNode;
  pendingText?: string;
  className?: string;
}) {
  const { pending } = useFormStatus();
  return (
    <button className={className} disabled={pending} aria-busy={pending}>
      {pending ? pendingText : children}
    </button>
  );
}
