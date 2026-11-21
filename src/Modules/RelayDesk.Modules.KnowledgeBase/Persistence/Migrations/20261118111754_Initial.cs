using System;
using Microsoft.EntityFrameworkCore.Migrations;
using RelayDesk.SharedKernel.Persistence;
using NpgsqlTypes;
using Pgvector;

#nullable disable

namespace RelayDesk.Modules.KnowledgeBase.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "kb");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "articles",
                schema: "kb",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_articles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "article_chunks",
                schema: "kb",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    article_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    embedding = table.Column<Vector>(type: "vector(768)", nullable: true),
                    search_vector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: false)
                        .Annotation("Npgsql:TsVectorConfig", "english")
                        .Annotation("Npgsql:TsVectorProperties", new[] { "title", "content" })
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_article_chunks", x => x.id);
                    table.ForeignKey(
                        name: "fk_article_chunks_articles_article_id",
                        column: x => x.article_id,
                        principalSchema: "kb",
                        principalTable: "articles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_article_chunks_article_id",
                schema: "kb",
                table: "article_chunks",
                column: "article_id");

            migrationBuilder.CreateIndex(
                name: "ix_article_chunks_embedding",
                schema: "kb",
                table: "article_chunks",
                column: "embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_article_chunks_search_vector",
                schema: "kb",
                table: "article_chunks",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_article_chunks_tenant_id_article_id",
                schema: "kb",
                table: "article_chunks",
                columns: new[] { "tenant_id", "article_id" });

            migrationBuilder.CreateIndex(
                name: "ix_articles_tenant_id_status_updated_at",
                schema: "kb",
                table: "articles",
                columns: new[] { "tenant_id", "status", "updated_at" });

            // ADR 0003: articles and their search index stay inside the current tenant.
            migrationBuilder.Sql(RlsSql.TenantIsolation("kb", "articles"));
            migrationBuilder.Sql(RlsSql.TenantIsolation("kb", "article_chunks"));
            migrationBuilder.Sql(RlsSql.GrantSchema("kb"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "article_chunks",
                schema: "kb");

            migrationBuilder.DropTable(
                name: "articles",
                schema: "kb");
        }
    }
}
