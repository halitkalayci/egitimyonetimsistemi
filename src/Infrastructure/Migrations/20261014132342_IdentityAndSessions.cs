using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IdentityAndSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "participants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    member_firm_id = table.Column<Guid>(type: "uuid", nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    password_hash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    is_disabled = table.Column<bool>(type: "boolean", nullable: true),
                    must_change_password = table.Column<bool>(type: "boolean", nullable: true),
                    failed_attempts = table.Column<int>(type: "integer", nullable: true),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    session_version = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_participants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "refresh_chains",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    account_session_version = table.Column<int>(type: "integer", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    rotated_token_hashes = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_chains", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_chains_participants_participant_id",
                        column: x => x.participant_id,
                        principalTable: "participants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_participants_email",
                table: "participants",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_participants_member_firm_id",
                table: "participants",
                column: "member_firm_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_chains_current_token_hash",
                table: "refresh_chains",
                column: "current_token_hash");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_chains_participant_id",
                table: "refresh_chains",
                column: "participant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "refresh_chains");

            migrationBuilder.DropTable(
                name: "participants");
        }
    }
}
