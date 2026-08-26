using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // Generated migration uses an inline column array.

namespace GovernedAccess.Workflow.Persistence.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialWorkflowPersistence : Migration
    {
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
                    CandidateVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ConcurrencyVersion = table.Column<long>(type: "INTEGER", nullable: false),
                    InterpretedTurnCount = table.Column<int>(type: "INTEGER", nullable: false),
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
                columns: new[] { "Channel", "TenantId", "ChannelActorId", "ConversationId", "RequesterId" },
                unique: true,
                filter: "\"Lifecycle\" IN ('Collecting', 'Ready')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RequestPreparations");

            migrationBuilder.DropTable(
                name: "AuthenticatedPrincipals");
        }
    }
}
#pragma warning restore CA1861
