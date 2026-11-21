import { notFound } from "next/navigation";
import { api, ApiError, type Article, type Tenant } from "@/lib/api";
import { ArticleEditor } from "../ArticleEditor";

export default async function EditArticle({
  params,
  searchParams,
}: {
  params: Promise<{ tenantId: string; articleId: string }>;
  searchParams: Promise<{ error?: string }>;
}) {
  const { tenantId, articleId } = await params;
  const { error } = await searchParams;
  const tenant = await api<Tenant>("/api/tenancy/tenant", { tenantId });
  let article: Article;
  try {
    article = await api<Article>(`/api/kb/articles/${articleId}`, { tenantId });
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) notFound();
    throw e;
  }
  return <ArticleEditor tenantId={tenantId} article={article} canEdit={tenant.role !== "Agent"} error={error} />;
}
