using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nudge.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ChangeLogAndSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Contexts_Name",
                table: "Contexts");

            migrationBuilder.AddColumn<string>(
                name: "DeletedAt",
                table: "Projects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedAt",
                table: "Items",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedAt",
                table: "Contexts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Changes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    At = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Undoes = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Changes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Changes_Changes_Undoes",
                        column: x => x.Undoes,
                        principalTable: "Changes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChangeEntries",
                columns: table => new
                {
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    ChangeId = table.Column<long>(type: "INTEGER", nullable: false),
                    TableName = table.Column<string>(type: "TEXT", nullable: false),
                    RowKey = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Before = table.Column<string>(type: "TEXT", nullable: true),
                    After = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChangeEntries", x => new { x.ChangeId, x.Ordinal });
                    table.CheckConstraint("CK_ChangeEntries_Kind", "(\"Before\" IS NULL) = (Kind = 'Added') AND (\"After\" IS NULL) = (Kind = 'Removed')");
                    table.ForeignKey(
                        name: "FK_ChangeEntries_Changes_ChangeId",
                        column: x => x.ChangeId,
                        principalTable: "Changes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Contexts_Name",
                table: "Contexts",
                column: "Name",
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeEntries_TableName_RowKey",
                table: "ChangeEntries",
                columns: new[] { "TableName", "RowKey" });

            migrationBuilder.CreateIndex(
                name: "IX_Changes_Undoes",
                table: "Changes",
                column: "Undoes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChangeEntries");

            migrationBuilder.DropTable(
                name: "Changes");

            migrationBuilder.DropIndex(
                name: "IX_Contexts_Name",
                table: "Contexts");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Contexts");

            migrationBuilder.CreateIndex(
                name: "IX_Contexts_Name",
                table: "Contexts",
                column: "Name",
                unique: true);
        }
    }
}
