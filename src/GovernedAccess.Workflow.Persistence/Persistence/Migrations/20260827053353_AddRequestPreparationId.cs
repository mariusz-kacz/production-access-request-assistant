using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovernedAccess.Workflow.Persistence.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequestPreparationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PreparationId",
                table: "AccessRequests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_AccessRequests_PreparationId",
                table: "AccessRequests",
                column: "PreparationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_AccessRequests_PreparationId",
                table: "AccessRequests");

            migrationBuilder.DropColumn(
                name: "PreparationId",
                table: "AccessRequests");
        }
    }
}
