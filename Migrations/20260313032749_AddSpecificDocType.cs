using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wafek_Web_Manager.Migrations
{
    /// <inheritdoc />
    public partial class AddSpecificDocType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SpecificDocTypeGuid",
                table: "WorkflowDefinitions",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SpecificDocTypeGuid",
                table: "WorkflowDefinitions");
        }
    }
}
