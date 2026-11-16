namespace Keyframe.Core.Database
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One schema migration: a version, a description, and the statements that apply it.
    /// </summary>
    public class Migration
    {
        #region Public-Members

        /// <summary>
        /// Migration version. Minimum 1.
        /// </summary>
        public int Version { get; }

        /// <summary>
        /// Human readable description.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Statements, run in order inside one transaction.
        /// </summary>
        public IReadOnlyList<string> Statements { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="version">Version, 1 or greater.</param>
        /// <param name="description">Description.</param>
        /// <param name="statements">Statements.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when version is below 1.</exception>
        /// <exception cref="ArgumentNullException">Thrown when description or statements is null.</exception>
        public Migration(int version, string description, IReadOnlyList<string> statements)
        {
            if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
            if (String.IsNullOrWhiteSpace(description)) throw new ArgumentNullException(nameof(description));
            if (statements == null) throw new ArgumentNullException(nameof(statements));

            Version = version;
            Description = description;
            Statements = statements;
        }

        #endregion
    }
}
