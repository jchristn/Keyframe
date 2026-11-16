namespace Keyframe.Core.Database
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A parameterized SQL statement queued for execution.
    /// </summary>
    public class DbCommandSpec
    {
        #region Public-Members

        /// <summary>
        /// SQL text.
        /// </summary>
        public string Sql { get; }

        /// <summary>
        /// Parameters keyed by name (including the "@" prefix).
        /// </summary>
        public Dictionary<string, object?> Parameters { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="sql">SQL text.</param>
        /// <exception cref="ArgumentNullException">Thrown when sql is null or empty.</exception>
        public DbCommandSpec(string sql)
        {
            if (String.IsNullOrWhiteSpace(sql)) throw new ArgumentNullException(nameof(sql));
            Sql = sql;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add a parameter.
        /// </summary>
        /// <param name="name">Name, including the "@" prefix.</param>
        /// <param name="value">Value; null becomes a database null.</param>
        /// <returns>This instance.</returns>
        /// <exception cref="ArgumentNullException">Thrown when name is null or empty.</exception>
        public DbCommandSpec Add(string name, object? value)
        {
            if (String.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            Parameters[name] = value;
            return this;
        }

        #endregion
    }
}
