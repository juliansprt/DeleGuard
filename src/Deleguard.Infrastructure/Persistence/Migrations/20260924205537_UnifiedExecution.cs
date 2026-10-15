using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deleguard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UnifiedExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Suspended",
                table: "organization_applications",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "OperationHash",
                table: "authorization_decisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperationId",
                table: "authorization_decisions",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "operation_executions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    OrganizationId = table.Column<string>(type: "text", nullable: false),
                    ApplicationId = table.Column<string>(type: "text", nullable: false),
                    EnvironmentId = table.Column<string>(type: "text", nullable: false),
                    AgentId = table.Column<string>(type: "text", nullable: false),
                    DecisionId = table.Column<string>(type: "text", nullable: false),
                    PermitId = table.Column<string>(type: "character varying(64)", nullable: false),
                    OperationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OperationHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResourceSnapshotHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    Verification = table.Column<string>(type: "text", nullable: false),
                    ReportDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ProviderReference = table.Column<string>(type: "text", nullable: true),
                    ResultHash = table.Column<string>(type: "text", nullable: true),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExecuteBefore = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AssignedTo = table.Column<string>(type: "text", nullable: true),
                    Resolution = table.Column<string>(type: "text", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AlertedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_executions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_operation_executions_permits_PermitId",
                        column: x => x.PermitId,
                        principalTable: "permits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_operation_executions_DecisionId",
                table: "operation_executions",
                column: "DecisionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_operation_executions_OrganizationId_ApplicationId_AgentId_O~",
                table: "operation_executions",
                columns: new[] { "OrganizationId", "ApplicationId", "AgentId", "OperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_operation_executions_OrganizationId_ApplicationId_State_Con~",
                table: "operation_executions",
                columns: new[] { "OrganizationId", "ApplicationId", "State", "ConsumedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_operation_executions_PermitId",
                table: "operation_executions",
                column: "PermitId");

            // Serialize authorization state writers, including bulk SQL paths, with permit consumption.
            migrationBuilder.Sql("""
                CREATE FUNCTION deleguard_state_lock() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  PERFORM pg_advisory_xact_lock(hashtextextended('deleguard-state:' || COALESCE(NEW."OrganizationId", OLD."OrganizationId"), 0));
                  IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                  RETURN NEW;
                END $$;
                CREATE FUNCTION deleguard_execution_deadline() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF clock_timestamp() >= NEW."ExecuteBefore" THEN
                    RAISE EXCEPTION 'Execution permission expired before durable consumption' USING ERRCODE = 'P0001';
                  END IF;
                  NEW."ConsumedAt" := clock_timestamp();
                  RETURN NEW;
                END $$;
                CREATE TRIGGER execution_deadline BEFORE INSERT ON operation_executions
                  FOR EACH ROW EXECUTE FUNCTION deleguard_execution_deadline();
                CREATE FUNCTION deleguard_approval_deadline() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  PERFORM pg_advisory_xact_lock(hashtextextended('deleguard-state:' || NEW."OrganizationId", 0));
                  IF NEW."Status" = 'Approved' AND OLD."Status" = 'Pending' AND clock_timestamp() >= OLD."ExpiresAt" THEN
                    RAISE EXCEPTION 'Approval expired before resolution' USING ERRCODE = 'P0001';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER approval_deadline BEFORE UPDATE ON approval_requests
                  FOR EACH ROW EXECUTE FUNCTION deleguard_approval_deadline();
                """);
            foreach (var table in StateTables)
                migrationBuilder.Sql($"CREATE TRIGGER state_lock BEFORE INSERT OR UPDATE OR DELETE ON {table} FOR EACH ROW EXECUTE FUNCTION deleguard_state_lock();");
        }

        /// <inheritdoc />
        private static readonly string[] StateTables = ["agents", "agent_credentials", "organization_applications", "organization_environments",
            "action_definitions", "agent_action_grants", "policies", "policy_versions", "agent_policy_grants", "permits", "approval_requests",
            "ide_projects", "ide_grants", "users", "user_roles", "roles", "role_permissions"];

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in StateTables) migrationBuilder.Sql($"DROP TRIGGER IF EXISTS state_lock ON {table};");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS approval_deadline ON approval_requests; DROP FUNCTION deleguard_approval_deadline(); DROP FUNCTION deleguard_state_lock();");
            migrationBuilder.DropTable(
                name: "operation_executions");
            migrationBuilder.Sql("DROP FUNCTION deleguard_execution_deadline();");

            migrationBuilder.DropColumn(
                name: "Suspended",
                table: "organization_applications");

            migrationBuilder.DropColumn(
                name: "OperationHash",
                table: "authorization_decisions");

            migrationBuilder.DropColumn(
                name: "OperationId",
                table: "authorization_decisions");
        }
    }
}
