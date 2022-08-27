using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MpbkpuLib.Migrations
{
    public partial class Initial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Backups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    Guid = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Settings = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    Result = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Backups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BackupVolumes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BackupId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupVolumes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BackupVolumes_Backups_BackupId",
                        column: x => x.BackupId,
                        principalTable: "Backups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BackupWarnings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BackupId = table.Column<int>(type: "INTEGER", nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupWarnings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BackupWarnings_Backups_BackupId",
                        column: x => x.BackupId,
                        principalTable: "Backups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BackupItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BackupId = table.Column<int>(type: "INTEGER", nullable: false),
                    VolumeId = table.Column<int>(type: "INTEGER", nullable: true),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Action = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    OriginalPath = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    OriginalRelativePath = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    OriginalName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    OriginalExtension = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    OriginalSha256 = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    OriginalSize = table.Column<long>(type: "INTEGER", nullable: false),
                    OriginalHidden = table.Column<bool>(type: "INTEGER", nullable: false),
                    OriginalCreated = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OriginalLastModified = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BackupItems_Backups_BackupId",
                        column: x => x.BackupId,
                        principalTable: "Backups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BackupItems_BackupVolumes_VolumeId",
                        column: x => x.VolumeId,
                        principalTable: "BackupVolumes",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Files",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    RootPath = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    FullPath = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Extension = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Hidden = table.Column<bool>(type: "INTEGER", nullable: false),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastModified = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FileAction = table.Column<int>(type: "INTEGER", nullable: false),
                    Modified = table.Column<bool>(type: "INTEGER", nullable: false),
                    BackupItemId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Files", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Files_BackupItems_BackupItemId",
                        column: x => x.BackupItemId,
                        principalTable: "BackupItems",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_BackupItems_BackupId_OriginalPath",
                table: "BackupItems",
                columns: new[] { "BackupId", "OriginalPath" });

            migrationBuilder.CreateIndex(
                name: "IX_BackupItems_BackupId_OriginalRelativePath",
                table: "BackupItems",
                columns: new[] { "BackupId", "OriginalRelativePath" });

            migrationBuilder.CreateIndex(
                name: "IX_BackupItems_VolumeId",
                table: "BackupItems",
                column: "VolumeId");

            migrationBuilder.CreateIndex(
                name: "IX_Backups_Guid",
                table: "Backups",
                column: "Guid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BackupVolumes_BackupId_Name",
                table: "BackupVolumes",
                columns: new[] { "BackupId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BackupWarnings_BackupId",
                table: "BackupWarnings",
                column: "BackupId");

            migrationBuilder.CreateIndex(
                name: "IX_Files_BackupItemId",
                table: "Files",
                column: "BackupItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Files_FileAction",
                table: "Files",
                column: "FileAction");

            migrationBuilder.CreateIndex(
                name: "IX_Files_FullPath",
                table: "Files",
                column: "FullPath");

            migrationBuilder.CreateIndex(
                name: "IX_Files_RelativePath",
                table: "Files",
                column: "RelativePath");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackupWarnings");

            migrationBuilder.DropTable(
                name: "Files");

            migrationBuilder.DropTable(
                name: "BackupItems");

            migrationBuilder.DropTable(
                name: "BackupVolumes");

            migrationBuilder.DropTable(
                name: "Backups");
        }
    }
}
