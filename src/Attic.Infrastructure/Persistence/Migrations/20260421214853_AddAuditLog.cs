using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Attic.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_channel_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_message_id = table.Column<long>(type: "bigint", nullable: true),
                    data = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_actor",
                table: "audit_logs",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_created",
                table: "audit_logs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_target_channel",
                table: "audit_logs",
                column: "target_channel_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs");
        }
    }
}
