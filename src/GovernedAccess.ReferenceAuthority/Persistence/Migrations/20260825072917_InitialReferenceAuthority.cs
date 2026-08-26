using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovernedAccess.ReferenceAuthority.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialReferenceAuthority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Clients",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    BusinessApproverPrincipalId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductionEnvironments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ClientId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Region = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Classification = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsProduction = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsEligibleForIntake = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionEnvironments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionEnvironments_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EnvironmentRoles",
                columns: table => new
                {
                    EnvironmentId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RoleId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsCurrentlyAssignable = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnvironmentRoles", x => new { x.EnvironmentId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_EnvironmentRoles_ProductionEnvironments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "ProductionEnvironments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Incidents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnvironmentId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Incidents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Incidents_ProductionEnvironments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "ProductionEnvironments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_EnvironmentId",
                table: "Incidents",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionEnvironments_ClientId",
                table: "ProductionEnvironments",
                column: "ClientId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EnvironmentRoles");

            migrationBuilder.DropTable(
                name: "Incidents");

            migrationBuilder.DropTable(
                name: "ProductionEnvironments");

            migrationBuilder.DropTable(
                name: "Clients");
        }
    }
}
