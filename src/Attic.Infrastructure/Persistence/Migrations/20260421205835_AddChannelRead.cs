using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Attic.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelRead : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "channel_reads",
                columns: table => new
                {
                    channel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_read_message_id = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_channel_reads", x => new { x.channel_id, x.user_id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_channel_reads_user",
                table: "channel_reads",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "channel_reads");
        }
    }
}
