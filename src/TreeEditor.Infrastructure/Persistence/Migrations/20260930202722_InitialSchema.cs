using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TreeEditor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "node_version_seq");

            migrationBuilder.CreateTable(
                name: "apply_requests",
                columns: table => new
                {
                    key = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    response = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_apply_requests", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "nodes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('node_version_seq')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_nodes", x => x.id);
                    table.CheckConstraint("ck_nodes_parent_is_not_self", "parent_id <> id");
                    table.ForeignKey(
                        name: "fk_nodes_parent",
                        column: x => x.parent_id,
                        principalTable: "nodes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tree_state",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    generation = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tree_state", x => x.id);
                    table.CheckConstraint("ck_tree_state_single_row", "id = 1");
                });

            migrationBuilder.InsertData(
                table: "tree_state",
                columns: new[] { "id", "generation" },
                values: new object[] { 1, 1L });

            migrationBuilder.CreateIndex(
                name: "ix_apply_requests_created_at",
                table: "apply_requests",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_nodes_parent_id_id",
                table: "nodes",
                columns: new[] { "parent_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_nodes_roots",
                table: "nodes",
                column: "id",
                filter: "parent_id IS NULL");

            migrationBuilder.Sql("""
                CREATE VIEW node_state AS
                WITH RECURSIVE walk (id, parent_id, value, version, is_deleted_effective) AS (
                    SELECT n.id, n.parent_id, n.value, n.version, n.is_deleted
                    FROM nodes AS n
                    WHERE n.parent_id IS NULL
                    UNION
                    SELECT c.id, c.parent_id, c.value, c.version, w.is_deleted_effective OR c.is_deleted
                    FROM nodes AS c
                    JOIN walk AS w ON c.parent_id = w.id
                )
                SELECT id, parent_id, value, version, is_deleted_effective
                FROM walk;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW node_state;");

            migrationBuilder.DropTable(
                name: "apply_requests");

            migrationBuilder.DropTable(
                name: "nodes");

            migrationBuilder.DropTable(
                name: "tree_state");

            migrationBuilder.DropSequence(
                name: "node_version_seq");
        }
    }
}
