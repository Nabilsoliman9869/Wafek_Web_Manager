using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wafek_Web_Manager.Migrations
{
    /// <inheritdoc />
    public partial class AddSelectedValueToStep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SelectedValue",
                table: "WorkflowSteps",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SelectedValue",
                table: "WorkflowSteps");
        }
    }
}
