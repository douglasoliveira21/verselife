using System.Collections.Generic;
using System.Text;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The colours of the Lua code in the script editor (as the Godot client's highlighter): words of the language, text between quotes, comments, numbers, the names of the platform
    /// (<c>object</c>, <c>timer</c>…) and the functions called. <see cref="Tokens"/> splits the source; <see cref="Rich"/> gives it as rich text for the layer drawn under the text field.
    /// </summary>
    public static class LuaHighlight
    {
        public enum Kind { Text, Keyword, String, Comment, Number, Namespace, Call }
        public const string KeywordColour = "#C586C0", StringColour = "#CE9178", CommentColour = "#6A9955", NumberColour = "#B5CEA8", NamespaceColour = "#4EC9B0", CallColour = "#DCDCAA", TextColour = "#D4D4D4";
        private static readonly HashSet<string> Keywords = new HashSet<string>(LuaEditorRules.Keywords);
        private static readonly HashSet<string> Namespaces = new HashSet<string>(LuaEditorRules.Namespaces) { "math", "string", "table", "physics", "http", "npc", "vehicle", "media" };

        public static List<(Kind Kind, string Text)> Tokens(string source)
        {
            var list = new List<(Kind, string)>(); source ??= ""; var i = 0; var n = source.Length;
            void Add(Kind kind, int start, int end) { if (end > start) list.Add((kind, source.Substring(start, end - start))); }
            while (i < n)
            {
                var c = source[i]; var start = i;
                if (c == '-' && i + 1 < n && source[i + 1] == '-')
                {
                    if (i + 3 < n && source[i + 2] == '[' && source[i + 3] == '[') { var close = source.IndexOf("]]", i + 4, System.StringComparison.Ordinal); i = close < 0 ? n : close + 2; }
                    else { var eol = source.IndexOf('\n', i); i = eol < 0 ? n : eol; }
                    Add(Kind.Comment, start, i); continue;
                }
                if (c == '"' || c == '\'')
                {
                    i++; while (i < n && source[i] != c && source[i] != '\n') { if (source[i] == '\\' && i + 1 < n) i++; i++; }
                    if (i < n && source[i] == c) i++; Add(Kind.String, start, i); continue;
                }
                if (c == '[' && i + 1 < n && source[i + 1] == '[') { var close = source.IndexOf("]]", i + 2, System.StringComparison.Ordinal); i = close < 0 ? n : close + 2; Add(Kind.String, start, i); continue; }
                if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(source[i + 1])))
                {
                    i++; while (i < n && (char.IsLetterOrDigit(source[i]) || source[i] == '.' || ((source[i] == '-' || source[i] == '+') && (source[i - 1] == 'e' || source[i - 1] == 'E')))) i++;
                    Add(Kind.Number, start, i); continue;
                }
                if (char.IsLetter(c) || c == '_')
                {
                    i++; while (i < n && (char.IsLetterOrDigit(source[i]) || source[i] == '_')) i++;
                    var word = source.Substring(start, i - start); var next = i; while (next < n && (source[next] == ' ' || source[next] == '\t')) next++;
                    var kind = Keywords.Contains(word) ? Kind.Keyword : next < n && source[next] == '.' && Namespaces.Contains(word) ? Kind.Namespace : next < n && source[next] == '(' ? Kind.Call : Kind.Text;
                    Add(kind, start, i); continue;
                }
                i++; while (i < n && !IsStart(source, i)) i++;
                Add(Kind.Text, start, i);
            }
            return list;
        }

        private static bool IsStart(string s, int i) { var c = s[i]; return char.IsLetterOrDigit(c) || c == '_' || c == '"' || c == '\'' || (c == '-' && i + 1 < s.Length && s[i + 1] == '-') || (c == '[' && i + 1 < s.Length && s[i + 1] == '['); }

        public static string ColourOf(Kind kind) => kind switch { Kind.Keyword => KeywordColour, Kind.String => StringColour, Kind.Comment => CommentColour, Kind.Number => NumberColour, Kind.Namespace => NamespaceColour, Kind.Call => CallColour, _ => TextColour };

        /// <summary>The source as rich text: every piece in its colour, its characters never read as tags.</summary>
        public static string Rich(string source)
        {
            var text = new StringBuilder();
            foreach (var (kind, piece) in Tokens(source)) text.Append("<color=").Append(ColourOf(kind)).Append("><noparse>").Append(piece.Replace("</noparse>", "</no</noparse><noparse>parse>").Replace(' ', '\u00A0').Replace("\t", "\u00A0\u00A0\u00A0\u00A0")).Append("</noparse></color>");
            return text.ToString();
        }
    }
}
