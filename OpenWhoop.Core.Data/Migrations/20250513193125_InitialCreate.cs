using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenWhoop.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Activities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ActivityId = table.Column<long>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    SportId = table.Column<int>(type: "INTEGER", nullable: false),
                    Start = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    End = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TimezoneOffset = table.Column<string>(type: "TEXT", nullable: false),
                    Score = table.Column<double>(type: "REAL", nullable: true),
                    Strain = table.Column<double>(type: "REAL", nullable: true),
                    AverageHeartRate = table.Column<int>(type: "INTEGER", nullable: true),
                    MaxHeartRate = table.Column<int>(type: "INTEGER", nullable: true),
                    Kilojoules = table.Column<double>(type: "REAL", nullable: true),
                    Distance = table.Column<double>(type: "REAL", nullable: true),
                    Zone0Duration = table.Column<int>(type: "INTEGER", nullable: true),
                    Zone1Duration = table.Column<int>(type: "INTEGER", nullable: true),
                    Zone2Duration = table.Column<int>(type: "INTEGER", nullable: true),
                    Zone3Duration = table.Column<int>(type: "INTEGER", nullable: true),
                    Zone4Duration = table.Column<int>(type: "INTEGER", nullable: true),
                    Zone5Duration = table.Column<int>(type: "INTEGER", nullable: true),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now', 'utc')"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now', 'utc')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Activities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HeartRateSamples",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ActivityId = table.Column<int>(type: "INTEGER", nullable: true),
                    SleepCycleId = table.Column<int>(type: "INTEGER", nullable: true),
                    Timestamp = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Value = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now', 'utc')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HeartRateSamples", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Packets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Uuid = table.Column<Guid>(type: "TEXT", nullable: false),
                    Bytes = table.Column<byte[]>(type: "BLOB", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now', 'utc')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Packets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SleepCycles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SleepId = table.Column<long>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Start = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    End = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TimezoneOffset = table.Column<string>(type: "TEXT", nullable: false),
                    Nap = table.Column<bool>(type: "INTEGER", nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: true),
                    RecoveryScore = table.Column<int>(type: "INTEGER", nullable: true),
                    HrvRmssd = table.Column<double>(type: "REAL", nullable: true),
                    RestingHeartRate = table.Column<int>(type: "INTEGER", nullable: true),
                    Kilojoules = table.Column<double>(type: "REAL", nullable: true),
                    AverageHeartRate = table.Column<int>(type: "INTEGER", nullable: true),
                    SleepNeedSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    RespiratoryRate = table.Column<double>(type: "REAL", nullable: true),
                    SleepDebtSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    SleepEfficiency = table.Column<double>(type: "REAL", nullable: true),
                    SleepConsistency = table.Column<int>(type: "INTEGER", nullable: true),
                    CyclesCount = table.Column<int>(type: "INTEGER", nullable: true),
                    Disturbances = table.Column<int>(type: "INTEGER", nullable: true),
                    TimeInBedSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    LatencySeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    LightSleepDurationSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    SlowWaveSleepDurationSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    RemSleepDurationSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    AwakeDurationSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    ArousalTimeSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now', 'utc')"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now', 'utc')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SleepCycles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SleepEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SleepId = table.Column<long>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now', 'utc')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SleepEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StoredDeviceSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DeviceId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    DeviceName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    LastConnectedUtc = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now', 'utc')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredDeviceSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StressSamples",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Timestamp = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Score = table.Column<double>(type: "REAL", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now', 'utc')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StressSamples", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now', 'utc')"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now', 'utc')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Activities_ActivityId",
                table: "Activities",
                column: "ActivityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Activities_End",
                table: "Activities",
                column: "End");

            migrationBuilder.CreateIndex(
                name: "IX_Activities_Start",
                table: "Activities",
                column: "Start");

            migrationBuilder.CreateIndex(
                name: "IX_Activities_UserId",
                table: "Activities",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_HeartRateSamples_ActivityId",
                table: "HeartRateSamples",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_HeartRateSamples_SleepCycleId",
                table: "HeartRateSamples",
                column: "SleepCycleId");

            migrationBuilder.CreateIndex(
                name: "IX_HeartRateSamples_Timestamp",
                table: "HeartRateSamples",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_Packets_Uuid",
                table: "Packets",
                column: "Uuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SleepCycles_End",
                table: "SleepCycles",
                column: "End");

            migrationBuilder.CreateIndex(
                name: "IX_SleepCycles_SleepId",
                table: "SleepCycles",
                column: "SleepId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SleepCycles_Start",
                table: "SleepCycles",
                column: "Start");

            migrationBuilder.CreateIndex(
                name: "IX_SleepCycles_UserId",
                table: "SleepCycles",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SleepEvents_SleepId",
                table: "SleepEvents",
                column: "SleepId");

            migrationBuilder.CreateIndex(
                name: "IX_SleepEvents_Timestamp",
                table: "SleepEvents",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_StoredDeviceSettings_DeviceId",
                table: "StoredDeviceSettings",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_StressSamples_Timestamp",
                table: "StressSamples",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_Users_UserId",
                table: "Users",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Activities");

            migrationBuilder.DropTable(
                name: "HeartRateSamples");

            migrationBuilder.DropTable(
                name: "Packets");

            migrationBuilder.DropTable(
                name: "SleepCycles");

            migrationBuilder.DropTable(
                name: "SleepEvents");

            migrationBuilder.DropTable(
                name: "StoredDeviceSettings");

            migrationBuilder.DropTable(
                name: "StressSamples");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
