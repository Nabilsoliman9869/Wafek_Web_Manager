using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wafek_Web_Manager.Migrations
{
    /// <inheritdoc />
    public partial class AddStepCondition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StepCondition",
                table: "WorkflowSteps",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StepCondition",
                table: "WorkflowSteps");
        }
    }
}
