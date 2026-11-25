using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MpbkpuLib.Migrations
{
    /// <inheritdoc />
    public partial class MultipleDestinations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DestinationId",
                table: "BackupVolumes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Destinations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Destinations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BackupVolumes_DestinationId",
                table: "BackupVolumes",
                column: "DestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_Destinations_Key",
                table: "Destinations",
                column: "Key",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BackupVolumes_Destinations_DestinationId",
                table: "BackupVolumes",
                column: "DestinationId",
                principalTable: "Destinations",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BackupVolumes_Destinations_DestinationId",
                table: "BackupVolumes");

            migrationBuilder.DropTable(
                name: "Destinations");

            migrationBuilder.DropIndex(
                name: "IX_BackupVolumes_DestinationId",
                table: "BackupVolumes");

            migrationBuilder.DropColumn(
                name: "DestinationId",
                table: "BackupVolumes");
        }
    }
}
