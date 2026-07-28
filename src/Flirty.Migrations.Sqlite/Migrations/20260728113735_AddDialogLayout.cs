using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flirty.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddDialogLayout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DialogLayout",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DialogId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ElementKind = table.Column<int>(type: "INTEGER", nullable: false),
                    ElementId = table.Column<Guid>(type: "TEXT", nullable: false),
                    X = table.Column<int>(type: "INTEGER", nullable: false),
                    Y = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DialogLayout", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DialogLayout_Dialogs_DialogId",
                        column: x => x.DialogId,
                        principalTable: "Dialogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DialogLayout_DialogId_ElementKind_ElementId",
                table: "DialogLayout",
                columns: new[] { "DialogId", "ElementKind", "ElementId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DialogLayout");
        }
    }
}
