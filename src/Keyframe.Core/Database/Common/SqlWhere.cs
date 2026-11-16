namespace Keyframe.Core.Database.Common
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// Builds a parameterized WHERE clause from optional conditions.
    /// </summary>
    public class SqlWhere
    {
        #region Public-Members

        /// <summary>
        /// Parameters collected so far.
        /// </summary>
        public Dictionary<string, object?> Parameters { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);

        #endregion

        #region Private-Members

        private readonly List<string> _Conditions = new List<string>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add a condition and its parameter.
        /// </summary>
        /// <param name="condition">Condition text referencing the parameter.</param>
        /// <param name="parameterName">Parameter name including "@".</param>
        /// <param name="value">Parameter value.</param>
        /// <returns>This instance.</returns>
        /// <exception cref="ArgumentNullException">Thrown when condition or parameterName is null or empty.</exception>
        public SqlWhere Add(string condition, string parameterName, object? value)
        {
            if (String.IsNullOrWhiteSpace(condition)) throw new ArgumentNullException(nameof(condition));
            if (String.IsNullOrWhiteSpace(parameterName)) throw new ArgumentNullException(nameof(parameterName));
            _Conditions.Add(condition);
            Parameters[parameterName] = value;
            return this;
        }

        /// <summary>
        /// Add a condition with no parameter.
        /// </summary>
        /// <param name="condition">Condition text.</param>
        /// <returns>This instance.</returns>
        /// <exception cref="ArgumentNullException">Thrown when condition is null or empty.</exception>
        public SqlWhere Add(string condition)
        {
            if (String.IsNullOrWhiteSpace(condition)) throw new ArgumentNullException(nameof(condition));
            _Conditions.Add(condition);
            return this;
        }

        /// <summary>
        /// Render the clause, including the leading " WHERE ", or an empty string when there are no conditions.
        /// </summary>
        /// <returns>SQL fragment.</returns>
        public string ToSql()
        {
            if (_Conditions.Count < 1) return String.Empty;
            StringBuilder sb = new StringBuilder(" WHERE ");
            sb.Append(String.Join(" AND ", _Conditions));
            return sb.ToString();
        }

        /// <summary>
        /// Copy the parameters, optionally adding more.
        /// </summary>
        /// <returns>New dictionary.</returns>
        public Dictionary<string, object?> CopyParameters()
        {
            return new Dictionary<string, object?>(Parameters, StringComparer.Ordinal);
        }

        /// <summary>
        /// Escape a value for a LIKE pattern that uses ESCAPE '!'.
        /// </summary>
        /// <param name="value">Raw value.</param>
        /// <returns>Escaped value.</returns>
        public static string EscapeLike(string value)
        {
            if (value == null) return String.Empty;
            return value.Replace("!", "!!").Replace("%", "!%").Replace("_", "!_");
        }

        #endregion
    }
}
