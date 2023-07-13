using HarmonyLib;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace EMTK {
    [HarmonyPatch]
    public static class PatchVTML {
        public static MethodInfo richtextify;

        public static bool cleanText = false;
        public static int listLevel = 0;
        public static int ignoreNewlines = 0;

        public static readonly CairoFont TABLE_FONT = new CairoFont(16.0, "Consolas", new[] {1.0, 1.0, 1.0, 1.0}, null).WithLineHeightMultiplier(0.9);
        public static int tableWidth = 0;

        public static void Patch() {
            richtextify = typeof(VtmlUtil).GetMethods(BindingFlags.NonPublic | BindingFlags.Static).Where(m => m.Name.StartsWith("Richtextify")).Last();
            EMTK.harmony.Patch(richtextify, new HarmonyMethod(typeof(PatchVTML), "Richtextify"));
            EMTK.harmony.Patch(typeof(VtmlUtil).GetMethod("Richtextify"), null, new HarmonyMethod(typeof(PatchVTML), "RichtextifyWrap"));
            // EMTK.harmony.Patch(
            //     typeof(GuiElementRichtext).GetConstructor(new[] {typeof(ICoreClientAPI), typeof(RichTextComponentBase[]), typeof(ElementBounds)}),
            //     null, new HarmonyMethod(typeof(PatchVTML), "GuiElementRichtextConstruct")
            // );
        }

        // public static void GuiElementRichtextConstruct(GuiElementRichtext __instance) {
        //     __instance.Bounds.CalcWorldBounds();
        //     tableWidth = (int)(__instance.Bounds.fixedWidth / 16.0);
        // }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(VtmlTagToken), "ContentText", MethodType.Getter)]
        public static bool ContentText(VtmlTagToken __instance, ref string __result) {
            string text = "";
            foreach (VtmlToken val in __instance.ChildElements) {
                if (val is VtmlTextToken) {
                    text += (val as VtmlTextToken).Text;
                } else {
                    VtmlTagToken tval = (VtmlTagToken) val;
                    if (tval.Name == "img") {
                        if (tval.Attributes.ContainsKey("alt")) {
                            text += tval.Attributes["alt"];
                        } else if (tval.Attributes.ContainsKey("src")) {
                            string src = tval.Attributes["src"];
                            text += src.Contains("/") ? src.Substring(src.LastIndexOf("/")+1) : src;
                        } else {
                            text += "Image";
                        }
                        text += " ";
                    } else {
                        text += tval.ContentText;
                    }
                }
            }
            __result = replaceText(text);
            return false;
        }

        public static void RichtextifyWrap(ref RichTextComponentBase[] __result) {
            if (!cleanText) return;

            List<RichTextComponentBase> texts = new List<RichTextComponentBase>(__result);
            int n = 0;

            int newlineCount = 0;
            bool prevIsList = false;
            int tempInt = 0;

            while (n < texts.Count) {
                if (!(texts[n] is RichTextComponent)) {
                    newlineCount = 0;
                    prevIsList = false;
                    n++;
                    continue;
                }

                RichTextComponent r = (RichTextComponent) texts[n];
                string text = r.DisplayText;

                if (text == "\r\n") {
                    if (++newlineCount > 2 || prevIsList) texts.RemoveAt(n);
                    else n++;
                    continue;
                }

                r.DisplayText = r.DisplayText.Trim('\r', '\n');

                n++;

                if (text.Trim() == "") continue;

                newlineCount = 0;

                if (text.Length <= 0 || text[0] != '|') {
                    prevIsList = false;
                    continue;
                }
                
                string ttext = text.Substring(1).TrimStart();
                prevIsList = (
                    ttext == "-" ||
                    ttext.Length > 1 &&
                    ttext.Substring(ttext.Length-1) == "." &&
                    int.TryParse(ttext.Substring(0, ttext.Length-1), out tempInt)
                );
            }

            __result = texts.ToArray();
            cleanText = false;
        }

        public static bool Richtextify(ICoreClientAPI capi, VtmlToken token, ref List<RichTextComponentBase> elems, Stack<CairoFont> fontStack, Action<LinkTextComponent> didClickLink) {
            if (token is VtmlTagToken) {
                VtmlTagToken tagToken = (VtmlTagToken) token;
                CairoFont font;

                switch (tagToken.Name) {
                    case "a":
                        LinkTextComponent cmp = new LinkTextComponent(capi, tagToken.ContentText, fontStack.Peek(), didClickLink);
                        cmp.PaddingRight += 2.0;
                        tagToken.Attributes.TryGetValue("href", out cmp.Href);
                        elems.Add(cmp);
                        return false;
                    case "span":
						foreach (VtmlToken v in tagToken.ChildElements) {
                            richtextify.Invoke(null, new object[] {capi, v, elems, fontStack, didClickLink});
						}
                        return false;
                    case "p": case "div":
						foreach (VtmlToken v in tagToken.ChildElements) {
                            richtextify.Invoke(null, new object[] {capi, v, elems, fontStack, didClickLink});
						}
                        addNewline(capi, ref elems, fontStack.Peek());
                        return false;
                    case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
                        font = fontStack.Peek().Clone().WithFontSize(20.0f + ('6' - tagToken.Name[1])*3.0f);
						font.FontWeight = Cairo.FontWeight.Bold;
						fontStack.Push(font);
						foreach (VtmlToken v in tagToken.ChildElements) {
							richtextify.Invoke(null, new object[] {capi, v, elems, fontStack, didClickLink});
						}
                        fontStack.Pop();
                        addNewline(capi, ref elems, fontStack.Peek());
                        return false;
                    case "em":
                        font = fontStack.Peek().Clone();
						font.Slant = Cairo.FontSlant.Italic;
						fontStack.Push(font);
						foreach (VtmlToken v in tagToken.ChildElements) {
							richtextify.Invoke(null, new object[] {capi, v, elems, fontStack, didClickLink});
						}
						fontStack.Pop();
                        return false;
                    case "ul":
                        listLevel++;
                        foreach (VtmlToken v in tagToken.ChildElements) {
                            if (v is VtmlTagToken && ((VtmlTagToken) v).Name == "li") {
                                VtmlTagToken vtagToken = (VtmlTagToken) v;
                                // This is the only way I could find to render the space correctly
                                var x = new RichTextComponent(capi, "| " + String.Join("", Enumerable.Repeat("      ", listLevel-1)) + "- ", fontStack.Peek());
                                elems.Add(x);
                                elems.Add(new RichTextComponent(capi, " ", fontStack.Peek()));
                                foreach (VtmlToken vi in vtagToken.ChildElements) {
                                    richtextify.Invoke(null, new object[] {capi, vi, elems, fontStack, didClickLink});
                                }
                                addNewline(capi, ref elems, fontStack.Peek());
                            }
                        }
                        if (listLevel > 1) {
                            ignoreNewlines++;
                            elems.RemoveAt(elems.Count-1);
                        }
                        listLevel--;
                        return false;
                    case "ol":
                        listLevel++;
                        int n = 0;
                        foreach (VtmlToken v in tagToken.ChildElements) {
                            if (v is VtmlTagToken && ((VtmlTagToken) v).Name == "li") {
                                VtmlTagToken vtagToken = (VtmlTagToken) v;
                                // This is the only way I could find to render the space correctly
                                elems.Add(
                                    new RichTextComponent(capi, "│ " + String.Join("", Enumerable.Repeat("      ", listLevel-1)) + ++n + ". ", fontStack.Peek())
                                );
                                elems.Add(new RichTextComponent(capi, " ", fontStack.Peek()));
                                foreach (VtmlToken vi in vtagToken.ChildElements) {
                                    richtextify.Invoke(null, new object[] {capi, vi, elems, fontStack, didClickLink});
                                }
                                addNewline(capi, ref elems, fontStack.Peek());
                            }
                        }
                        if (listLevel > 1) {
                            ignoreNewlines++;
                            elems.RemoveAt(elems.Count-1);
                        }
                        listLevel--;
                        return false;
                    case "img":
                        string text;
                        if (tagToken.Attributes.ContainsKey("alt")) {
                            text = tagToken.Attributes["alt"];
                        } else if (tagToken.Attributes.ContainsKey("src")) {
                            string src = tagToken.Attributes["src"];
                            text = src.Contains("/") ? src.Substring(src.LastIndexOf("/")+1) : src;
                        } else {
                            text = "Image";
                        }
                        text += " ";
                        elems.Add(new RichTextComponent(capi, text, fontStack.Peek()));
                        return false;
                    case "table":
                        foreach (VtmlToken root in tagToken.ChildElements) {
                            if (root is VtmlTagToken) {
                                VtmlTagToken tagRoot = (VtmlTagToken) root;
                                if (tagRoot.Name == "tbody") {
                                    renderTable(capi, tagRoot, ref elems, fontStack, didClickLink);
                                    break;
                                } else if (tagRoot.Name == "th" || tagRoot.Name == "tr") {
                                    renderTable(capi, tagToken, ref elems, fontStack, didClickLink);
                                    break;
                                }
                            }
                        }
                        return false;
                    default:
                        return true;
                }
            } else {
                VtmlTextToken textToken = (VtmlTextToken) token;
                if (!cleanText || textToken.Text.Trim(' ').Length > 0) {
                    elems.Add(new RichTextComponent(capi, replaceText(textToken.Text), fontStack.Peek()));
                }
                return false;
            }
        }

        public static string replaceText(string text) {
            return text
                .Replace("&nbsp;", " ")
                .Replace("&amp;", "&")
                .Replace("&deg;", "°");
        }

        public static void addNewline(ICoreClientAPI capi, ref List<RichTextComponentBase> elems, CairoFont font) {
            if (ignoreNewlines > 0) {
                ignoreNewlines--;
            } else if (!(elems[elems.Count-1] is RichTextComponent && ((RichTextComponent)elems[elems.Count-1]).DisplayText.EndsWith("\r\n"))) {
                elems.Add(new RichTextComponent(capi, "\r\n", font));
            }
        }

        public static void renderTable(ICoreClientAPI capi, VtmlTagToken tagToken, ref List<RichTextComponentBase> elems, Stack<CairoFont> fontStack, Action<LinkTextComponent> didClickLink) {
            List<List<string>> textTable = new List<List<string>>();
            List<string> textRow;

            int span = 0;

            // Get text in the table
            foreach (VtmlToken row in tagToken.ChildElements) {
                if (!(row is VtmlTagToken)) continue;
                VtmlTagToken tagRow = (VtmlTagToken) row;
                if (tagRow.Name == "tr") {
                    textRow = new List<string>();
                    foreach (VtmlToken col in tagRow.ChildElements) {
                        if (col is VtmlTagToken) {
                            VtmlTagToken tagCol = (VtmlTagToken) col;
                            if (tagCol.Name == "td" || tagCol.Name == "th") {
                                textRow.Add(tagCol.ContentText);
                                if (tagCol.Attributes.ContainsKey("colspan") && int.TryParse(tagCol.Attributes["colspan"], out span) && span > 1) {
                                    for (int i = 0; i < span - 1; i++) textRow.Add("<<");
                                }
                            }
                        }
                    }
                    textTable.Add(textRow);
                    if (tagRow.Attributes.ContainsKey("rowspan") && int.TryParse(tagRow.Attributes["rowspan"], out span) && span > 1) {
                        List<string> spanrow = new List<string>();
                        for (int i = 0; i < textRow.Count; i++) spanrow.Add("^^");
                        for (int i = 0; i < span - 1; i++) textTable.Add(spanrow);
                    }
                }
            }

            // Ensure table is of reasonable bounds
            int colCount = textTable[0].Count;
            if (textTable.Count < 1) return;
            if (colCount < 1) return;
            // Ignore uneven tables
            for (int row = 1; row < textTable.Count; row++) {
                if (textTable[row].Count != colCount) return;
            }

            // Determine maximum characters in each column
            int[] cols = new int[colCount];
            int sum = 0;
            
            for (int col = 0; col < colCount; col++) {
                int max = 0;
                for (int row = 0; row < textTable.Count; row++) {
                    int len = textTable[row][col].Length;
                    if (len > max) max = len;
                }
                max = Math.Max(5, max);
                cols[col] = max;
                sum += max;
            }
            if (sum > tableWidth) {
                int space = tableWidth;
                int maxCol = Math.Max(5, (tableWidth - 1) / cols.Length - 2);
                int n = colCount;

                for (int col = 0; col < colCount; col++) {
                    if (cols[col] < maxCol) {
                        space -= cols[col];
                        n--;
                    }
                }

                maxCol = Math.Max(5, (space - 1) / n - 2);
                for (int col = 0; col < colCount; col++) {
                    cols[col] = Math.Min(cols[col], maxCol);
                }
            }

            // Render table
            StringBuilder sb = new StringBuilder();

            // Top row
            sb.Append("┌─");
            for (int col = 0; col < colCount-1; col++) {
                sb.Append('─', cols[col]);
                sb.Append("─┬─");
            }
            sb.Append('─', cols[colCount-1]);
            sb.Append("─┐\r\n");

            // Cells
            for (int row = 0; row < textTable.Count; row++) {
                // Data

                List<string> rowText;
                List<string> overflow = textTable[row];
                bool overflowed = true;
                while (overflowed) {
                    rowText = overflow;
                    overflow = new List<string>(colCount);
                    overflowed = false;

                    sb.Append("│ ");

                    for (int col = 0; col < colCount; col++) {
                        string text = rowText[col].Trim();
                        if (text.Length <= cols[col] && !text.Contains("\r\n")) {
                            sb.Append(text);
                            sb.Append(' ', Math.Max(cols[col] - text.Length, 0));
                            overflow.Add("");
                        } else {
                            int m = Math.Min(text.Length-1, cols[col]);
                            int s = text.IndexOf("\r\n");
                            if (s > m) s = -1;
                            if (s <= 0) s = text.LastIndexOf('(', m);
                            if (s <= 0) s = text.LastIndexOf(' ', m);
                            if (s <= 0) s = m;

                            overflow.Add(text.Substring(s).Trim());
                            overflowed = true;

                            text = text.Substring(0, s).Trim();
                            sb.Append(text);
                            sb.Append(' ', Math.Max(cols[col] - text.Length, 0));
                        }
                        sb.Append(" │ ");
                    }

                    sb.Append("\r\n");
                }


                // Footer
                if (row == textTable.Count-1) {
                    sb.Append("└─");
                    for (int col = 0; col < colCount-1; col++) {
                        sb.Append('─', cols[col]);
                        sb.Append("─┴─");
                    }
                    sb.Append('─', cols[colCount-1]);
                    sb.Append("─┘\r\n");
                } else {
                    sb.Append("├─");
                    for (int col = 0; col < colCount-1; col++) {
                        sb.Append('─', cols[col]);
                        sb.Append("─┼─");
                    }
                    sb.Append('─', cols[colCount-1]);
                    sb.Append("─┤\r\n");
                }
            }

            sb.Append("\t\r\n");
            elems.Add(new RichTextComponent(capi, sb.ToString(), TABLE_FONT));
        }
    }
}