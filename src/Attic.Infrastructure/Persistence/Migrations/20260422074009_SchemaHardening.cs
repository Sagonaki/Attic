using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Attic.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SchemaHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_sessions_expires_at",
                table: "sessions",
                column: "expires_at",
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_sessions_token_hash",
                table: "sessions",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_messages_reply_to_id",
                table: "messages",
                column: "reply_to_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_friend_requests_status_enum",
                table: "friend_requests",
                sql: "status IN (0,1,2,3)");

            migrationBuilder.CreateIndex(
                name: "ix_channels_owner",
                table: "channels",
                column: "owner_id",
                filter: "owner_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_channels_kind_enum",
                table: "channels",
                sql: "kind IN (0,1,2)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_channel_members_role_enum",
                table: "channel_members",
                sql: "role IN (0,1,2)");

            migrationBuilder.CreateIndex(
                name: "ix_channel_invitations_inviter_id",
                table: "channel_invitations",
                column: "inviter_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_channel_invitations_status_enum",
                table: "channel_invitations",
                sql: "status IN (0,1,2,3)");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_target_message",
                table: "audit_logs",
                column: "target_message_id",
                filter: "target_message_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_target_user",
                table: "audit_logs",
                column: "target_user_id",
                filter: "target_user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_attachments_uploader_id",
                table: "attachments",
                column: "uploader_id");

            migrationBuilder.AddForeignKey(
                name: "fk_attachments_message",
                table: "attachments",
                column: "message_id",
                principalTable: "messages",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_attachments_uploader",
                table: "attachments",
                column: "uploader_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_audit_logs_actor",
                table: "audit_logs",
                column: "actor_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_channel_invitations_channel",
                table: "channel_invitations",
                column: "channel_id",
                principalTable: "channels",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_channel_invitations_invitee",
                table: "channel_invitations",
                column: "invitee_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_channel_invitations_inviter",
                table: "channel_invitations",
                column: "inviter_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_channel_members_channel",
                table: "channel_members",
                column: "channel_id",
                principalTable: "channels",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_channel_members_user",
                table: "channel_members",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_channel_reads_channel",
                table: "channel_reads",
                column: "channel_id",
                principalTable: "channels",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_channel_reads_user",
                table: "channel_reads",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_channels_owner",
                table: "channels",
                column: "owner_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_friend_requests_recipient",
                table: "friend_requests",
                column: "recipient_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_friend_requests_sender",
                table: "friend_requests",
                column: "sender_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_friendships_user_a",
                table: "friendships",
                column: "user_a_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_friendships_user_b",
                table: "friendships",
                column: "user_b_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_messages_channel",
                table: "messages",
                column: "channel_id",
                principalTable: "channels",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_messages_reply_to",
                table: "messages",
                column: "reply_to_id",
                principalTable: "messages",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_messages_sender",
                table: "messages",
                column: "sender_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_sessions_user",
                table: "sessions",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_user_blocks_blocked",
                table: "user_blocks",
                column: "blocked_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_user_blocks_blocker",
                table: "user_blocks",
                column: "blocker_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_attachments_message",
                table: "attachments");

            migrationBuilder.DropForeignKey(
                name: "fk_attachments_uploader",
                table: "attachments");

            migrationBuilder.DropForeignKey(
                name: "fk_audit_logs_actor",
                table: "audit_logs");

            migrationBuilder.DropForeignKey(
                name: "fk_channel_invitations_channel",
                table: "channel_invitations");

            migrationBuilder.DropForeignKey(
                name: "fk_channel_invitations_invitee",
                table: "channel_invitations");

            migrationBuilder.DropForeignKey(
                name: "fk_channel_invitations_inviter",
                table: "channel_invitations");

            migrationBuilder.DropForeignKey(
                name: "fk_channel_members_channel",
                table: "channel_members");

            migrationBuilder.DropForeignKey(
                name: "fk_channel_members_user",
                table: "channel_members");

            migrationBuilder.DropForeignKey(
                name: "fk_channel_reads_channel",
                table: "channel_reads");

            migrationBuilder.DropForeignKey(
                name: "fk_channel_reads_user",
                table: "channel_reads");

            migrationBuilder.DropForeignKey(
                name: "fk_channels_owner",
                table: "channels");

            migrationBuilder.DropForeignKey(
                name: "fk_friend_requests_recipient",
                table: "friend_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_friend_requests_sender",
                table: "friend_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_friendships_user_a",
                table: "friendships");

            migrationBuilder.DropForeignKey(
                name: "fk_friendships_user_b",
                table: "friendships");

            migrationBuilder.DropForeignKey(
                name: "fk_messages_channel",
                table: "messages");

            migrationBuilder.DropForeignKey(
                name: "fk_messages_reply_to",
                table: "messages");

            migrationBuilder.DropForeignKey(
                name: "fk_messages_sender",
                table: "messages");

            migrationBuilder.DropForeignKey(
                name: "fk_sessions_user",
                table: "sessions");

            migrationBuilder.DropForeignKey(
                name: "fk_user_blocks_blocked",
                table: "user_blocks");

            migrationBuilder.DropForeignKey(
                name: "fk_user_blocks_blocker",
                table: "user_blocks");

            migrationBuilder.DropIndex(
                name: "ix_sessions_expires_at",
                table: "sessions");

            migrationBuilder.DropIndex(
                name: "ux_sessions_token_hash",
                table: "sessions");

            migrationBuilder.DropIndex(
                name: "ix_messages_reply_to_id",
                table: "messages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_friend_requests_status_enum",
                table: "friend_requests");

            migrationBuilder.DropIndex(
                name: "ix_channels_owner",
                table: "channels");

            migrationBuilder.DropCheckConstraint(
                name: "ck_channels_kind_enum",
                table: "channels");

            migrationBuilder.DropCheckConstraint(
                name: "ck_channel_members_role_enum",
                table: "channel_members");

            migrationBuilder.DropIndex(
                name: "ix_channel_invitations_inviter_id",
                table: "channel_invitations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_channel_invitations_status_enum",
                table: "channel_invitations");

            migrationBuilder.DropIndex(
                name: "ix_audit_logs_target_message",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "ix_audit_logs_target_user",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "ix_attachments_uploader_id",
                table: "attachments");
        }
    }
}
