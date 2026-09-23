using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Animora.Persistence.Sqlite.Converters;

public sealed class DecimalStringConverter() : ValueConverter<decimal, string>(
    value => value.ToString("G29", CultureInfo.InvariantCulture),
    text => decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture));
