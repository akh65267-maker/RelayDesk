import { redirect } from "next/navigation";
import { SubmitButton } from "@/components/SubmitButton";
import { api, ApiError, type Article } from "@/lib/api";
import styles from "../../../page.module.css";

/** Shared by the new and edit pages. Read-only for agents. */
export function ArticleEditor({
  tenantId,
  article,
  canEdit,
  error,
}: {
  tenantId: string;
  article: Article | null;
  canEdit: boolean;
  error?: string;
}) {
  const base = `/t/${tenantId}/kb`;
  // Server actions capture the values they use when the page renders, so capture the id itself
  // (null on the "new" page) rather than reading article.id inside the action.
  const articleId = article?.id ?? null;
  const self = articleId ? `${base}/${articleId}` : `${base}/new`;

  async function save(formData: FormData) {
    "use server";
    const body = JSON.stringify({
      title: formData.get("title"),
      body: formData.get("body"),
      publish: formData.get("publish") === "on",
    });
    let saved: Article;
    try {
      saved = articleId
        ? await api<Article>(`/api/kb/articles/${articleId}`, { method: "PUT", tenantId, body })
        : await api<Article>("/api/kb/articles", { method: "POST", tenantId, body });
    } catch (e) {
      redirect(`${self}?error=${encodeURIComponent(e instanceof ApiError ? e.detail : "Something went wrong.")}`);
    }
    redirect(`${base}/${saved.id}`);
  }

  async function remove() {
    "use server";
    await api(`/api/kb/articles/${articleId}`, { method: "DELETE", tenantId });
    redirect(base);
  }

  if (!canEdit && article) {
    return (
      <article className={styles.narrow}>
        <h2>{article.title}</h2>
        <p className={styles.messageBody}>{article.body}</p>
      </article>
    );
  }

  return (
    <section className={styles.narrow}>
      <h2>{article ? "Edit article" : "New article"}</h2>
      <form action={save} className={styles.form}>
        <label>
          Title
          <input name="title" required maxLength={200} defaultValue={article?.title} />
        </label>
        <label>
          Content <span className={styles.muted}>Markdown. Headings (#) and blank lines split it into searchable sections.</span>
          <textarea name="body" required rows={16} maxLength={100000} defaultValue={article?.body} />
        </label>
        <label className={styles.checkbox}>
          <input type="checkbox" name="publish" defaultChecked={article ? article.status === "Published" : true} /> Published (searchable
          by the AI)
        </label>
        {error && <p className={styles.error}>{error}</p>}
        <SubmitButton className={styles.primary}>Save</SubmitButton>
      </form>
      {article && (
        <form action={remove}>
          <SubmitButton className={styles.link} pendingText="Deleting…">
            Delete article
          </SubmitButton>
        </form>
      )}
    </section>
  );
}
