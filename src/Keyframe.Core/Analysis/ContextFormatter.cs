namespace Keyframe.Core.Analysis
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Keyframe.Core.Requests;

    /// <summary>
    /// Renders request context items into the text placed inside the prompt's context block.
    /// Thread safety: stateless and safe for concurrent use.
    /// </summary>
    public static class ContextFormatter
    {
        /// <summary>
        /// Render context items. A single unlabeled item renders as its text; otherwise each item is a bullet,
        /// prefixed with its label when present.
        /// </summary>
        /// <param name="items">Context items. Null or empty renders as null.</param>
        /// <returns>Rendered text, or null when there is no context.</returns>
        public static string? Render(List<ContextItem>? items)
        {
            if (items == null || items.Count < 1) return null;
            List<ContextItem> usable = items.FindAll(i => i != null && !String.IsNullOrWhiteSpace(i.Text));
            if (usable.Count < 1) return null;
            if (usable.Count == 1 && String.IsNullOrWhiteSpace(usable[0].Label)) return usable[0].Text.Trim();

            StringBuilder sb = new StringBuilder();
            foreach (ContextItem item in usable)
            {
                sb.Append("- ");
                if (!String.IsNullOrWhiteSpace(item.Label)) sb.Append(item.Label.Trim()).Append(": ");
                sb.Append(item.Text.Trim()).Append('\n');
            }

            return sb.ToString().TrimEnd();
        }
    }
}
