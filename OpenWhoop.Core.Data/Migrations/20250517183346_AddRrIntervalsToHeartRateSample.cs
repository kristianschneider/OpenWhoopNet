using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenWhoop.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRrIntervalsToHeartRateSample : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RrIntervals",
                table: "HeartRateSamples",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RrIntervals",
                table: "HeartRateSamples");
        }
    }
}
