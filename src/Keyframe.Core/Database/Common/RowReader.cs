namespace Keyframe.Core.Database.Common
{
    using System;
    using System.Data;
    using System.Globalization;

    /// <summary>
    /// Typed column readers that tolerate provider differences (integers for booleans, text for timestamps).
    /// </summary>
    public static class RowReader
    {
        /// <summary>
        /// Read a string column.
        /// </summary>
        /// <param name="row">Row.</param>
        /// <param name="column">Column name.</param>
        /// <returns>Value or null.</returns>
        public static string? GetString(DataRow row, string column)
        {
            object value = row[column];
            if (value is DBNull) return null;
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Read an integer column.
        /// </summary>
        /// <param name="row">Row.</param>
        /// <param name="column">Column name.</param>
        /// <returns>Value, or zero for nulls.</returns>
        public static long GetInt64(DataRow row, string column)
        {
            object value = row[column];
            if (value is DBNull) return 0;
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Read a floating point column.
        /// </summary>
        /// <param name="row">Row.</param>
        /// <param name="column">Column name.</param>
        /// <returns>Value, or zero for nulls.</returns>
        public static double GetDouble(DataRow row, string column)
        {
            object value = row[column];
            if (value is DBNull) return 0;
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Read a nullable integer column.
        /// </summary>
        /// <param name="row">Row.</param>
        /// <param name="column">Column name.</param>
        /// <returns>Value or null.</returns>
        public static long? GetNullableInt64(DataRow row, string column)
        {
            object value = row[column];
            if (value is DBNull) return null;
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Read a nullable floating point column.
        /// </summary>
        /// <param name="row">Row.</param>
        /// <param name="column">Column name.</param>
        /// <returns>Value or null.</returns>
        public static double? GetNullableDouble(DataRow row, string column)
        {
            object value = row[column];
            if (value is DBNull) return null;
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Read a boolean column stored as a boolean, bit, or integer.
        /// </summary>
        /// <param name="row">Row.</param>
        /// <param name="column">Column name.</param>
        /// <returns>Value, or false for nulls.</returns>
        public static bool GetBoolean(DataRow row, string column)
        {
            object value = row[column];
            if (value is DBNull) return false;
            if (value is bool b) return b;
            return Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0;
        }

        /// <summary>
        /// Read a timestamp column.
        /// </summary>
        /// <param name="row">Row.</param>
        /// <param name="column">Column name.</param>
        /// <returns>UTC value or null.</returns>
        public static DateTime? GetDate(DataRow row, string column)
        {
            return DatabaseDriverBase.FromDbDate(row[column]);
        }

        /// <summary>
        /// Read an enum stored as its name.
        /// </summary>
        /// <typeparam name="T">Enum type.</typeparam>
        /// <param name="row">Row.</param>
        /// <param name="column">Column name.</param>
        /// <param name="fallback">Value when the column is null or unrecognized.</param>
        /// <returns>Value.</returns>
        public static T GetEnum<T>(DataRow row, string column, T fallback) where T : struct, System.Enum
        {
            string? text = GetString(row, column);
            if (text == null) return fallback;
            T parsed;
            if (System.Enum.TryParse<T>(text, true, out parsed)) return parsed;
            return fallback;
        }
    }
}
