using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shortener.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AccountOwnershipFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "owner_workspace_id",
                table: "url_mappings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "application_users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    contact_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "external_identities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issuer = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_identities", x => x.id);
                    table.CheckConstraint("ck_external_identity_key", "length(trim(issuer)) > 0 AND length(trim(subject)) > 0");
                    table.ForeignKey(
                        name: "FK_external_identities_application_users_user_id",
                        column: x => x.user_id,
                        principalTable: "application_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workspaces",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    personal_owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    legacy_tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspaces", x => x.id);
                    table.CheckConstraint("ck_workspaces_kind_owner", "(kind = 0 AND personal_owner_user_id IS NOT NULL AND legacy_tenant_id IS NULL) OR (kind = 1 AND personal_owner_user_id IS NULL)");
                    table.ForeignKey(
                        name: "FK_workspaces_application_users_personal_owner_user_id",
                        column: x => x.personal_owner_user_id,
                        principalTable: "application_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workspace_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    verifier_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    redeemed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    redeemed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_invitations", x => x.id);
                    table.CheckConstraint("ck_workspace_invitations_expiry", "expires_at_utc > created_at_utc");
                    table.CheckConstraint("ck_workspace_invitations_state", "(redeemed_at_utc IS NULL) = (redeemed_by_user_id IS NULL) AND NOT (redeemed_at_utc IS NOT NULL AND revoked_at_utc IS NOT NULL) AND (redeemed_at_utc IS NULL OR (redeemed_at_utc >= created_at_utc AND redeemed_at_utc <= expires_at_utc)) AND (revoked_at_utc IS NULL OR revoked_at_utc >= created_at_utc)");
                    table.CheckConstraint("ck_workspace_invitations_verifier", "verifier_hash ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_workspace_invitations_application_users_redeemed_by_user_id",
                        column: x => x.redeemed_by_user_id,
                        principalTable: "application_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workspace_invitations_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workspace_memberships",
                columns: table => new
                {
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<short>(type: "smallint", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_memberships", x => new { x.workspace_id, x.user_id });
                    table.CheckConstraint("ck_workspace_memberships_role", "role IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_workspace_memberships_application_users_user_id",
                        column: x => x.user_id,
                        principalTable: "application_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workspace_memberships_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_url_mappings_owner_workspace",
                table: "url_mappings",
                column: "owner_workspace_id");

            migrationBuilder.CreateIndex(
                name: "IX_external_identities_user_id",
                table: "external_identities",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_external_identities_issuer_subject",
                table: "external_identities",
                columns: new[] { "issuer", "subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workspace_invitations_redeemed_by_user_id",
                table: "workspace_invitations",
                column: "redeemed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_workspace_invitations_workspace_id",
                table: "workspace_invitations",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "ux_workspace_invitations_verifier_hash",
                table: "workspace_invitations",
                column: "verifier_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workspace_memberships_user_id",
                table: "workspace_memberships",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_workspace_memberships_one_owner",
                table: "workspace_memberships",
                column: "workspace_id",
                unique: true,
                filter: "role = 0");

            migrationBuilder.CreateIndex(
                name: "ux_workspaces_legacy_tenant",
                table: "workspaces",
                column: "legacy_tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_workspaces_personal_owner",
                table: "workspaces",
                column: "personal_owner_user_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_url_mappings_workspaces_owner_workspace_id",
                table: "url_mappings",
                column: "owner_workspace_id",
                principalTable: "workspaces",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // EF runs this migration in a transaction. A failed count check rolls back
            // both the new workspaces and every ownership assignment.
            migrationBuilder.Sql("""
                DO $backfill$
                DECLARE
                    expected_mappings bigint;
                    assigned_mappings bigint;
                    expected_tenants bigint;
                    created_workspaces bigint;
                BEGIN
                    SELECT count(*), count(DISTINCT tenant_id)
                    INTO expected_mappings, expected_tenants
                    FROM url_mappings
                    WHERE mode = 1;

                    INSERT INTO workspaces (id, kind, personal_owner_user_id, legacy_tenant_id, name, created_at_utc)
                    SELECT md5('schwab-url-shortener:legacy-entra-workspace:' || tenant_id::text)::uuid,
                           1, NULL, tenant_id, 'Legacy Entra team', now()
                    FROM (SELECT DISTINCT tenant_id FROM url_mappings WHERE mode = 1) tenants;

                    GET DIAGNOSTICS created_workspaces = ROW_COUNT;

                    UPDATE url_mappings AS mapping
                    SET owner_workspace_id = workspace.id
                    FROM workspaces AS workspace
                    WHERE mapping.mode = 1
                      AND mapping.tenant_id = workspace.legacy_tenant_id;

                    SELECT count(*) INTO assigned_mappings
                    FROM url_mappings AS mapping
                    JOIN workspaces AS workspace ON workspace.id = mapping.owner_workspace_id
                    WHERE mapping.mode = 1
                      AND mapping.tenant_id = workspace.legacy_tenant_id;

                    IF created_workspaces <> expected_tenants OR assigned_mappings <> expected_mappings THEN
                        RAISE EXCEPTION 'Account ownership backfill count mismatch (tenants %, workspaces %, mappings %, assigned %)',
                            expected_tenants, created_workspaces, expected_mappings, assigned_mappings;
                    END IF;
                END
                $backfill$;
                """);

            // These checks cover references that PostgreSQL cannot express with a
            // row-local CHECK constraint. Null ownership remains valid for current
            // API writes until the later authorization cutover.
            migrationBuilder.Sql("""
                CREATE FUNCTION check_mapping_workspace_owner() RETURNS trigger
                LANGUAGE plpgsql AS $owner$
                BEGIN
                    IF NEW.owner_workspace_id IS NULL THEN
                        RETURN NEW;
                    END IF;
                    IF NEW.mode <> 1 OR NOT EXISTS (
                        SELECT 1 FROM workspaces AS workspace
                        WHERE workspace.id = NEW.owner_workspace_id
                          AND (workspace.legacy_tenant_id IS NULL OR workspace.legacy_tenant_id = NEW.tenant_id)
                    ) THEN
                        RAISE EXCEPTION 'Invalid mapping workspace ownership'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_url_mappings_owner_workspace';
                    END IF;
                    RETURN NEW;
                END
                $owner$;

                CREATE TRIGGER tr_url_mappings_workspace_owner
                BEFORE INSERT OR UPDATE OF mode, tenant_id, owner_workspace_id ON url_mappings
                FOR EACH ROW EXECUTE FUNCTION check_mapping_workspace_owner();

                CREATE FUNCTION require_team_workspace() RETURNS trigger
                LANGUAGE plpgsql AS $team$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM workspaces WHERE id = NEW.workspace_id AND kind = 1) THEN
                        RAISE EXCEPTION 'Membership or invitation requires a team workspace'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_team_workspace_scope';
                    END IF;
                    RETURN NEW;
                END
                $team$;

                CREATE TRIGGER tr_workspace_memberships_team_only
                BEFORE INSERT OR UPDATE OF workspace_id ON workspace_memberships
                FOR EACH ROW EXECUTE FUNCTION require_team_workspace();

                CREATE TRIGGER tr_workspace_invitations_team_only
                BEFORE INSERT OR UPDATE OF workspace_id ON workspace_invitations
                FOR EACH ROW EXECUTE FUNCTION require_team_workspace();

                CREATE FUNCTION keep_workspace_kind_and_legacy_tenant() RETURNS trigger
                LANGUAGE plpgsql AS $workspace$
                BEGIN
                    IF NEW.kind IS DISTINCT FROM OLD.kind OR
                       NEW.legacy_tenant_id IS DISTINCT FROM OLD.legacy_tenant_id THEN
                        RAISE EXCEPTION 'Workspace kind and legacy tenant are immutable'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_workspace_identity_immutable';
                    END IF;
                    RETURN NEW;
                END
                $workspace$;

                CREATE TRIGGER tr_workspaces_identity_immutable
                BEFORE UPDATE OF kind, legacy_tenant_id ON workspaces
                FOR EACH ROW EXECUTE FUNCTION keep_workspace_kind_and_legacy_tenant();

                CREATE FUNCTION keep_invitation_history() RETURNS trigger
                LANGUAGE plpgsql AS $invitation$
                BEGIN
                    IF NEW.workspace_id IS DISTINCT FROM OLD.workspace_id OR
                       NEW.verifier_hash IS DISTINCT FROM OLD.verifier_hash OR
                       NEW.created_at_utc IS DISTINCT FROM OLD.created_at_utc OR
                       NEW.expires_at_utc IS DISTINCT FROM OLD.expires_at_utc OR
                       (OLD.redeemed_at_utc IS NOT NULL AND
                         (NEW.redeemed_at_utc IS DISTINCT FROM OLD.redeemed_at_utc OR
                          NEW.redeemed_by_user_id IS DISTINCT FROM OLD.redeemed_by_user_id)) OR
                       (OLD.revoked_at_utc IS NOT NULL AND
                          NEW.revoked_at_utc IS DISTINCT FROM OLD.revoked_at_utc) THEN
                        RAISE EXCEPTION 'Invitation history cannot be changed'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_invitation_history_immutable';
                    END IF;
                    RETURN NEW;
                END
                $invitation$;

                CREATE TRIGGER tr_workspace_invitations_history
                BEFORE UPDATE ON workspace_invitations
                FOR EACH ROW EXECUTE FUNCTION keep_invitation_history();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new InvalidOperationException(
                "Account ownership rollback is blocked because it can discard account data. Restore a verified backup or use a reviewed forward repair.");
        }
    }
}
