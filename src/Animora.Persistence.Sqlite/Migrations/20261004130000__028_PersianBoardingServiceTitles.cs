using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Animora.Persistence.Sqlite.Migrations
{
    /// <summary>
    /// Data-only migration: the seeded boarding services were stored with English titles. Rename each to its
    /// Persian label, but only while it still carries the original English seed title, so a service the clinic
    /// has already renamed is never overwritten. No schema change (the model snapshot is unchanged).
    /// </summary>
    [DbContext(typeof(AnimoraLocalDbContext))]
    [Migration("20261004130000__028_PersianBoardingServiceTitles")]
    public partial class _028_PersianBoardingServiceTitles : Migration
    {
        private static readonly (string Code, string English, string Persian)[] Titles =
        [
            ("WALK", "Extra walk", "پیاده‌روی اضافه"),
            ("DIET", "Special diet", "رژیم غذایی ویژه"),
            ("BATH", "Bath", "حمام"),
            ("GROOM", "Grooming", "آرایش و نظافت"),
            ("PLAY", "Play session", "بازی"),
            ("CARE", "Extra care", "مراقبت ویژه"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (code, english, persian) in Titles)
            {
                migrationBuilder.Sql(
                    $"UPDATE \"BoardingServiceDefs\" SET \"Title\" = '{persian}' " +
                    $"WHERE \"Code\" = '{code}' AND \"Title\" = '{english}';");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (code, english, persian) in Titles)
            {
                migrationBuilder.Sql(
                    $"UPDATE \"BoardingServiceDefs\" SET \"Title\" = '{english}' " +
                    $"WHERE \"Code\" = '{code}' AND \"Title\" = '{persian}';");
            }
        }
    }
}
