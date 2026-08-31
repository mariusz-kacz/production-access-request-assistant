using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovernedAccess.Workflow.Persistence.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialWorkflowPersistence : Migration
    {
        private static readonly string[] ApprovalDecisionKeyColumns =
            ["RequestId", "Stage"];

        private static readonly string[] AuditEventOrderingColumns =
            ["RequestId", "OccurredAt", "Id"];

        private static readonly string[] ActivePreparationBindingColumns =
        [
            "Channel",
            "TenantId",
            "ChannelActorId",
            "ConversationId",
            "RequesterId",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuthenticatedPrincipals",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ClientId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthenticatedPrincipals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccessRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PreparationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequesterId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ClientId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EnvironmentId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    RequestedRoleId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Justification = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    IncidentId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    LastModifiedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PersistenceVersion = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessRequests_AuthenticatedPrincipals_RequesterId",
                        column: x => x.RequesterId,
                        principalTable: "AuthenticatedPrincipals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RequestPreparations",
                columns: table => new
                {
                    PreparationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PredecessorPreparationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ChannelActorId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ConversationId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    RequesterId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Lifecycle = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ClientId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    EnvironmentId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    RoleId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Justification = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    IncidentId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ConcurrencyVersion = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ReadyAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ReadyDeadline = table.Column<long>(type: "INTEGER", nullable: true),
                    TerminalAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ClarificationJson = table.Column<string>(type: "TEXT", nullable: true),
                    MaterialChangeAttributionsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestPreparations", x => x.PreparationId);
                    table.ForeignKey(
                        name: "FK_RequestPreparations_AuthenticatedPrincipals_RequesterId",
                        column: x => x.RequesterId,
                        principalTable: "AuthenticatedPrincipals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RequestPreparations_RequestPreparations_PredecessorPreparationId",
                        column: x => x.PredecessorPreparationId,
                        principalTable: "RequestPreparations",
                        principalColumn: "PreparationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Stage = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Decision = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ApproverId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Comment = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    DecidedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalDecisions_AccessRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "AccessRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApprovalDecisions_AuthenticatedPrincipals_ApproverId",
                        column: x => x.ApproverId,
                        principalTable: "AuthenticatedPrincipals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ActorId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    OccurredAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    OutcomeCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DetailsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditEvents_AccessRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "AccessRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AuditEvents_AuthenticatedPrincipals_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AuthenticatedPrincipals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProvisioningOperations",
                columns: table => new
                {
                    RequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastOutcomeCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    LastAttemptAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProvisioningOperations", x => x.RequestId);
                    table.ForeignKey(
                        name: "FK_ProvisioningOperations_AccessRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "AccessRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AccessGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActivatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessGrants_AccessRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "AccessRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccessGrants_ProvisioningOperations_RequestId",
                        column: x => x.RequestId,
                        principalTable: "ProvisioningOperations",
                        principalColumn: "RequestId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessGrants_RequestId",
                table: "AccessGrants",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccessRequests_RequesterId",
                table: "AccessRequests",
                column: "RequesterId");

            migrationBuilder.CreateIndex(
                name: "UX_AccessRequests_PreparationId",
                table: "AccessRequests",
                column: "PreparationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDecisions_ApproverId",
                table: "ApprovalDecisions",
                column: "ApproverId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDecisions_RequestId_Stage",
                table: "ApprovalDecisions",
                columns: ApprovalDecisionKeyColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_ActorId",
                table: "AuditEvents",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_RequestId_OccurredAt_Id",
                table: "AuditEvents",
                columns: AuditEventOrderingColumns);

            migrationBuilder.CreateIndex(
                name: "IX_RequestPreparations_PredecessorPreparationId",
                table: "RequestPreparations",
                column: "PredecessorPreparationId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestPreparations_RequesterId",
                table: "RequestPreparations",
                column: "RequesterId");

            migrationBuilder.CreateIndex(
                name: "UX_RequestPreparations_ActiveBinding",
                table: "RequestPreparations",
                columns: ActivePreparationBindingColumns,
                unique: true,
                filter: "\"Lifecycle\" IN ('Collecting', 'Ready')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessGrants");

            migrationBuilder.DropTable(
                name: "ApprovalDecisions");

            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "RequestPreparations");

            migrationBuilder.DropTable(
                name: "ProvisioningOperations");

            migrationBuilder.DropTable(
                name: "AccessRequests");

            migrationBuilder.DropTable(
                name: "AuthenticatedPrincipals");
        }
    }
}
