#if NET6_0_OR_GREATER
using System;

namespace NodaTime.Extensions
{
    /// <summary>
    /// Extension methods for <see cref="DateOnly"/>.
    /// </summary>
    public static class DateOnlyExtensions
    {
        /// <summary>
        /// Converts a <see cref="DateOnly"/> to a <see cref="LocalDate"/> in the ISO calendar.
        /// </summary>
        /// <remarks>This is a convenience method which calls <see cref="LocalDate.FromDateOnly(System.DateOnly)"/>.</remarks>
        /// <param name="date">The <see cref="DateOnly"/> to convert.</param>
        /// <returns>A new <see cref="LocalDate"/> in the ISO calendar with the same year, month and day as the <paramref name="date"/>.</returns>
        public static LocalDate ToLocalDate(this DateOnly date) => LocalDate.FromDateOnly(date);
    }
}
#endif