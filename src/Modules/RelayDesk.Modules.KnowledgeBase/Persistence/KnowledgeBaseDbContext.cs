using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RelayDesk.Modules.KnowledgeBase.Domain;
using RelayDesk.SharedKernel.Persistence;
using RelayDesk.SharedKernel.Tenancy;

namespace RelayDesk.Modules.KnowledgeBase.Persistence;

internal sealed class KnowledgeBaseDbContext(DbContextOptions<KnowledgeBaseDbContext> options, ITenantContext tenant)
    : TenantDbContext(options, tenant)
{
    public const string SchemaName = "kb";

    /// <summary>nomic-embed-text produces 768 dimensions; changing models means a migration and re-index.</summary>
    public const int EmbeddingDimensions = 768;

    protected override string Schema => SchemaName;

    public DbSet<Article> Articles => Set<Article>();
    public DbSet<ArticleChunk> Chunks => Set<ArticleChunk>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");
        base.OnModelCreating(modelBuilder);
    }
}

internal sealed class ArticleConfig : IEntityTypeConfiguration<Article>
{
    public void Configure(EntityTypeBuilder<Article> b)
    {
        b.Property(x => x.Title).HasMaxLength(200);
        b.Property(x => x.Body).HasMaxLength(100_000);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.CreatedBy).HasMaxLength(64);
        b.HasIndex(x => new { x.TenantId, x.Status, x.UpdatedAt });
    }
}

internal sealed class ArticleChunkConfig : IEntityTypeConfiguration<ArticleChunk>
{
    public void Configure(EntityTypeBuilder<ArticleChunk> b)
    {
        b.ToTable("article_chunks");
        b.Property(x => x.Title).HasMaxLength(200);
        b.Property(x => x.Content).HasMaxLength(4_000);
        b.Property(x => x.Embedding).HasColumnType($"vector({KnowledgeBaseDbContext.EmbeddingDimensions})");
        b.HasGeneratedTsVectorColumn(x => x.SearchVector, "english", x => new { x.Title, x.Content })
            .HasIndex(x => x.SearchVector).HasMethod("gin");
        b.HasIndex(x => x.Embedding).HasMethod("hnsw").HasOperators("vector_cosine_ops");
        b.HasIndex(x => new { x.TenantId, x.ArticleId });
        b.HasOne<Article>().WithMany().HasForeignKey(x => x.ArticleId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Used only by <c>dotnet ef</c>.</summary>
internal sealed class KnowledgeBaseDesignTimeFactory : IDesignTimeDbContextFactory<KnowledgeBaseDbContext>
{
    public KnowledgeBaseDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<KnowledgeBaseDbContext>()
                .UseNpgsql("Host=localhost;Database=relaydesk", o => o
                    .MigrationsHistoryTable("__ef_migrations", KnowledgeBaseDbContext.SchemaName)
                    .UseVector())
                .Options,
            new TenantContext());
}
