import { ArticleEditor } from "../ArticleEditor";

export default async function NewArticle({
  params,
  searchParams,
}: {
  params: Promise<{ tenantId: string }>;
  searchParams: Promise<{ error?: string }>;
}) {
  const { tenantId } = await params;
  const { error } = await searchParams;
  return <ArticleEditor tenantId={tenantId} article={null} canEdit error={error} />;
}
