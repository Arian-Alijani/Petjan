using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Animora.Persistence.Sqlite.Migrations
{
    /// <summary>
    /// Data-only migration: the seeded product categories were stored with English display names. Rename each
    /// to its Persian label, but only while it still carries the original English seed name, so a category the
    /// clinic has already renamed is never overwritten. No schema change (the model snapshot is unchanged).
    /// </summary>
    [DbContext(typeof(AnimoraLocalDbContext))]
    [Migration("20261004120000__027_PersianCategoryNames")]
    public partial class _027_PersianCategoryNames : Migration
    {
        private static readonly (string Code, string English, string Persian)[] Names =
        [
            ("MED", "Medicines", "داروها"),
            ("VAC", "Vaccines", "واکسن‌ها"),
            ("FOOD", "Food", "غذا و خوراک"),
            ("ACC", "Accessories", "لوازم جانبی"),
            ("SRV", "Services", "خدمات"),
            ("SUPP", "Supplements", "مکمل‌ها"),
            ("GROOM", "Grooming", "آرایش و نظافت"),
            ("TOY", "Toys", "اسباب‌بازی"),
            ("HYG", "Hygiene", "بهداشتی"),
            ("LAB", "Lab supplies", "ملزومات آزمایشگاه"),
            ("SURG", "Surgical consumables", "مصرفی جراحی"),
            ("EQUIP", "Equipment", "تجهیزات"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (code, english, persian) in Names)
            {
                migrationBuilder.Sql(
                    $"UPDATE \"ProductCategories\" SET \"DisplayName\" = '{persian}' " +
                    $"WHERE \"Code\" = '{code}' AND \"DisplayName\" = '{english}';");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (code, english, persian) in Names)
            {
                migrationBuilder.Sql(
                    $"UPDATE \"ProductCategories\" SET \"DisplayName\" = '{english}' " +
                    $"WHERE \"Code\" = '{code}' AND \"DisplayName\" = '{persian}';");
            }
        }
    }
}
