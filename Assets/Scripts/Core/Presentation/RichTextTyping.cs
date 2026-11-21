using System;
using System.Collections.Generic;
using System.Text;

namespace CourtSim.Core.Presentation
{
    /// <summary>
    /// Text helpers for the "typewriter" effect on Unity rich text (&lt;b&gt;, &lt;i&gt;, &lt;color=…&gt;, &lt;size=…&gt;).
    /// Tags take no space on screen, so only the characters outside tags are counted, a tag is never cut in
    /// half, and any tag still open at the cut is closed so the partial text is always valid markup.
    /// </summary>
    public static class RichTextTyping
    {
        /// <summary>Fully transparent colour used to hide the not-yet-typed rest of a message.</summary>
        public const string HiddenColor = "#00000000";

        private static readonly string[] KnownTags = { "b", "i", "color", "size" };

        /// <summary>How many characters of <paramref name="richText"/> are visible once tags are removed.</summary>
        public static int VisibleLength(string richText)
        {
            if (string.IsNullOrEmpty(richText))
            {
                return 0;
            }

            int count = 0;
            int i = 0;
            while (i < richText.Length)
            {
                int tagLength = TagLengthAt(richText, i, out _, out _);
                if (tagLength > 0)
                {
                    i += tagLength;
                }
                else
                {
                    count++;
                    i++;
                }
            }

            return count;
        }

        /// <summary>
        /// The first <paramref name="visibleChars"/> visible characters of <paramref name="richText"/>, keeping
        /// every tag that opened before the cut and closing the ones still open.
        /// </summary>
        public static string Prefix(string richText, int visibleChars)
        {
            Split(richText, visibleChars, out string prefix, out _);
            return prefix;
        }

        /// <summary>
        /// Like <see cref="Prefix"/>, but the rest of the message follows in a transparent colour. The label
        /// then already has its final size, so the panel does not grow line by line while the text types.
        /// </summary>
        public static string PrefixWithHiddenRest(string richText, int visibleChars)
        {
            Split(richText, visibleChars, out string prefix, out string rest);
            if (rest.Length == 0)
            {
                return prefix;
            }

            return prefix + "<color=" + HiddenColor + ">" + rest + "</color>";
        }

        /// <summary>The text with every rich-text tag removed, e.g. for the speaking voice.</summary>
        public static string StripTags(string richText)
        {
            if (string.IsNullOrEmpty(richText))
            {
                return string.Empty;
            }

            var plain = new StringBuilder(richText.Length);
            int i = 0;
            while (i < richText.Length)
            {
                int tagLength = TagLengthAt(richText, i, out _, out _);
                if (tagLength > 0)
                {
                    i += tagLength;
                }
                else
                {
                    plain.Append(richText[i]);
                    i++;
                }
            }

            return plain.ToString();
        }

        /// <summary>
        /// Splits the text at the cut. <paramref name="prefix"/> ends by closing the open tags;
        /// <paramref name="rest"/> starts by re-opening them, so both halves are balanced on their own.
        /// </summary>
        private static void Split(string richText, int visibleChars, out string prefix, out string rest)
        {
            if (string.IsNullOrEmpty(richText))
            {
                prefix = string.Empty;
                rest = string.Empty;
                return;
            }

            // Each entry is the full opening tag (e.g. "<color=#fff>") and its name ("color").
            var open = new List<KeyValuePair<string, string>>();
            var text = new StringBuilder(richText.Length + 16);
            int shown = 0;
            int i = 0;
            while (i < richText.Length)
            {
                int tagLength = TagLengthAt(richText, i, out string name, out bool closing);
                if (tagLength > 0)
                {
                    // Stop before an opening tag at the cut: it would only wrap nothing.
                    if (shown >= visibleChars && !closing)
                    {
                        break;
                    }

                    string tag = richText.Substring(i, tagLength);
                    if (closing)
                    {
                        RemoveLast(open, name);
                    }
                    else
                    {
                        open.Add(new KeyValuePair<string, string>(tag, name));
                    }

                    text.Append(tag);
                    i += tagLength;
                    continue;
                }

                if (shown >= visibleChars)
                {
                    break;
                }

                text.Append(richText[i]);
                shown++;
                i++;
            }

            var reopen = new StringBuilder();
            for (int k = open.Count - 1; k >= 0; k--)
            {
                text.Append("</").Append(open[k].Value).Append('>');
            }

            for (int k = 0; k < open.Count; k++)
            {
                reopen.Append(open[k].Key);
            }

            prefix = text.ToString();
            rest = i < richText.Length ? reopen.Append(richText, i, richText.Length - i).ToString() : string.Empty;
        }

        private static void RemoveLast(List<KeyValuePair<string, string>> open, string name)
        {
            for (int k = open.Count - 1; k >= 0; k--)
            {
                if (open[k].Value == name)
                {
                    open.RemoveAt(k);
                    return;
                }
            }
        }

        /// <summary>
        /// If a known tag such as &lt;b&gt; or &lt;/color&gt; starts at <paramref name="index"/>, returns its
        /// length; otherwise 0, and the '&lt;' is shown as an ordinary character (as Unity does).
        /// </summary>
        private static int TagLengthAt(string text, int index, out string name, out bool closing)
        {
            name = null;
            closing = false;
            if (text[index] != '<')
            {
                return 0;
            }

            int end = text.IndexOf('>', index + 1);
            if (end < 0)
            {
                return 0;
            }

            int start = index + 1;
            if (start < end && text[start] == '/')
            {
                closing = true;
                start++;
            }

            int nameEnd = start;
            while (nameEnd < end && char.IsLetter(text[nameEnd]))
            {
                nameEnd++;
            }

            string candidate = text.Substring(start, nameEnd - start).ToLowerInvariant();
            if (Array.IndexOf(KnownTags, candidate) < 0)
            {
                return 0;
            }

            // A closing tag is exactly "</name>"; an opening tag may carry a value, as in "<color=#fff>".
            bool valid = closing ? nameEnd == end : nameEnd == end || text[nameEnd] == '=';
            if (!valid)
            {
                return 0;
            }

            name = candidate;
            return end - index + 1;
        }
    }
}
