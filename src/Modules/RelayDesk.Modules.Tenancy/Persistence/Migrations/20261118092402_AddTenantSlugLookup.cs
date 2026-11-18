using Microsoft.EntityFrameworkCore.Migrations;
using RelayDesk.SharedKernel.Persistence;

#nullable disable

namespace RelayDesk.Modules.Tenancy.Persistence.Migrations
{
    /// <summary>
    /// The chat widget identifies a workspace by its public ID (slug) before anyone is signed in.
    /// RLS hides tenants from the app role, so this narrow SECURITY DEFINER function returns only the
    /// tenant id for an exact slug: no names, no listing, nothing else.
    /// </summary>
    public partial class AddTenantSlugLookup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                CREATE FUNCTION tenancy.tenant_id_by_slug(p_slug text) RETURNS uuid
                    LANGUAGE sql STABLE SECURITY DEFINER
                    SET search_path = pg_catalog, tenancy
                AS $$ SELECT id FROM tenancy.tenants WHERE slug = p_slug $$;
                REVOKE ALL ON FUNCTION tenancy.tenant_id_by_slug(text) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION tenancy.tenant_id_by_slug(text) TO {RlsSql.AppRole};
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenancy.tenant_id_by_slug(text);");
        }
    }
}
