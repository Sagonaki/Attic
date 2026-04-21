using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Attic.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "channel_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inviter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invitee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_channel_invitations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_channel_invitations_invitee_status",
                table: "channel_invitations",
                columns: new[] { "invitee_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_channel_invitations_channel_invitee_pending",
                table: "channel_invitations",
                columns: new[] { "channel_id", "invitee_id" },
                unique: true,
                filter: "status = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "channel_invitations");
        }
    }
}
