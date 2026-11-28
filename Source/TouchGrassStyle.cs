using UnityEngine;
using Verse;

namespace TouchGrass
{
    [StaticConstructorOnStartup]
    internal static class TouchGrassStyle
    {
        private const int RoundedSize = 48;
        private const int CircleSize = 64;
        private const int PatternSize = 64;
        private const int Slice = 12;

        /// <summary>Inner padding of the settings workspace. The corner accents
        /// are drawn inside it so they never overlap the content.</summary>
        public const float ContentPadding = 24f;

        private const float AccentInset = 6f;
        private const float AccentLength = 16f;

        public static readonly Color Workspace = FromBytes(16, 20, 23);
        public static readonly Color Panel = FromBytes(22, 27, 31);
        public static readonly Color Raised = FromBytes(31, 36, 40);
        public static readonly Color Hover = FromBytes(46, 51, 54);
        public static readonly Color Gold = FromBytes(235, 210, 84);
        public static readonly Color Cyan = FromBytes(49, 199, 213);
        public static readonly Color Warning = FromBytes(255, 168, 54);
        public static readonly Color Negative = FromBytes(224, 82, 86);
        public static readonly Color Muted = FromBytes(169, 171, 169);

        private static readonly Texture2D RoundedMask = MakeRoundedMask();
        private static readonly Texture2D CircleMask = MakeCircleMask();
        private static readonly Texture2D MicroPattern = MakeMicroPattern();
        private static readonly GUIStyle RoundedStyle = new GUIStyle
        {
            normal = { background = RoundedMask },
            border = new RectOffset(Slice, Slice, Slice, Slice),
            padding = new RectOffset(0, 0, 0, 0),
            margin = new RectOffset(0, 0, 0, 0)
        };

        public static void DrawWorkspace(Rect rect)
        {
            DrawRounded(rect, Workspace);
            Color previous = GUI.color;
            GUI.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.08f);
            GUI.DrawTextureWithTexCoords(
                rect.ContractedBy(4f),
                MicroPattern,
                new Rect(
                    0f,
                    0f,
                    rect.width / PatternSize,
                    rect.height / PatternSize));
            GUI.color = previous;

            Widgets.DrawBoxSolid(
                new Rect(
                    rect.x + AccentInset,
                    rect.y + AccentInset,
                    AccentLength,
                    2f),
                Gold);
            Widgets.DrawBoxSolid(
                new Rect(
                    rect.x + AccentInset,
                    rect.y + AccentInset,
                    2f,
                    AccentLength),
                Gold);
            Widgets.DrawBoxSolid(
                new Rect(
                    rect.xMax - AccentInset - AccentLength,
                    rect.yMax - AccentInset - 2f,
                    AccentLength,
                    2f),
                Cyan);
            Widgets.DrawBoxSolid(
                new Rect(
                    rect.xMax - AccentInset - 2f,
                    rect.yMax - AccentInset - AccentLength,
                    2f,
                    AccentLength),
                Cyan);
        }

        public static void DrawPanel(Rect rect, Color accent)
        {
            DrawRoundedOutline(
                rect,
                new Color(accent.r, accent.g, accent.b, 0.52f),
                Panel,
                1f,
                true);
        }

        /// <summary>A section bar; an optional chip on the right states the
        /// section's status (for example LOCKED).</summary>
        public static void DrawSectionHeading(
            Rect rect,
            string title,
            string chip)
        {
            DrawRoundedOutline(
                rect,
                new Color(Gold.r, Gold.g, Gold.b, 0.46f),
                Raised,
                1f);
            // A short capsule well inside the bar; orange when the section is
            // locked (matching its chip), gold otherwise.
            const float textInset = 26f;
            DrawCapsule(
                new Rect(
                    rect.x + 12f,
                    rect.y + rect.height * 0.25f,
                    3f,
                    rect.height * 0.5f),
                string.IsNullOrEmpty(chip) ? Gold : Warning);

            Color oldColor = GUI.color;
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            GUI.color = Gold;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.WordWrap = false;
            Widgets.Label(
                new Rect(
                    rect.x + textInset,
                    rect.y + 2f,
                    rect.width - textInset - 14f,
                    rect.height - 4f),
                title.ToUpperInvariant());

            if (!string.IsNullOrEmpty(chip))
            {
                Text.Font = GameFont.Tiny;
                float width = Text.CalcSize(chip).x + 22f;
                Rect chipRect = new Rect(
                    rect.xMax - width - 10f,
                    rect.y + (rect.height - 20f) / 2f,
                    width,
                    20f);
                DrawRoundedOutline(
                    chipRect,
                    new Color(Warning.r, Warning.g, Warning.b, 0.78f),
                    new Color(0.17f, 0.12f, 0.06f, 0.98f),
                    1f);
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Warning;
                Widgets.Label(chipRect, chip);
            }

            Text.WordWrap = oldWrap;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            GUI.color = oldColor;
        }

        public static void Toggle(
            Listing_Standard listing,
            string label,
            ref bool value)
        {
            Rect rect = listing.GetRect(36f);
            bool hovered = GUI.enabled && Mouse.IsOver(rect);
            Color accent = value ? Cyan : Gold;
            Color border = value || hovered
                ? new Color(accent.r, accent.g, accent.b, 0.92f)
                : new Color(0.38f, 0.40f, 0.40f, 0.78f);
            Color fill = value
                ? new Color(0.10f, 0.23f, 0.25f, 0.98f)
                : hovered ? Hover : Panel;
            DrawRoundedOutline(rect, border, fill, 1f);

            Rect switchRect = new Rect(
                rect.xMax - 56f,
                rect.y + (rect.height - 20f) / 2f,
                40f,
                20f);
            Rect labelRect = new Rect(
                rect.x + 12f,
                rect.y,
                Mathf.Max(0f, switchRect.x - rect.x - 24f),
                rect.height);

            Color oldColor = GUI.color;
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            Text.Font = GameFont.Small;
            Text.WordWrap = false;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = value || hovered ? Color.white : Muted;
            Widgets.Label(labelRect, label);
            if (Text.CalcSize(label).x > labelRect.width)
            {
                TooltipHandler.TipRegion(labelRect, label);
            }
            Text.WordWrap = oldWrap;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            GUI.color = oldColor;
            DrawSwitch(switchRect, value, hovered);

            if (Widgets.ButtonInvisible(rect))
            {
                value = !value;
            }
            listing.Gap(4f);
        }

        /// <summary>A pill switch built from the cached capsule and circle
        /// masks. The whole row still lights up; the switch is the cue.</summary>
        private static void DrawSwitch(Rect rect, bool on, bool hovered)
        {
            DrawCapsule(
                new Rect(rect.x - 1f, rect.y - 1f, rect.width + 2f, rect.height + 2f),
                on
                    ? new Color(Cyan.r, Cyan.g, Cyan.b, 0.38f)
                    : new Color(0.50f, 0.52f, 0.52f, hovered ? 0.95f : 0.62f));
            DrawCapsule(
                rect,
                on
                    ? new Color(Cyan.r, Cyan.g, Cyan.b, 0.95f)
                    : new Color(0.25f, 0.27f, 0.28f, 0.98f));
            float knob = rect.height - 6f;
            DrawCircle(
                new Rect(
                    on ? rect.xMax - knob - 3f : rect.x + 3f,
                    rect.y + 3f,
                    knob,
                    knob),
                on ? Color.white : new Color(0.80f, 0.82f, 0.81f, 1f));
        }

        public static bool Button(
            Rect rect,
            string label,
            bool active = false,
            bool destructive = false,
            Color? accentOverride = null,
            bool emphasized = false)
        {
            bool enabled = GUI.enabled;
            bool hovered = enabled && Mouse.IsOver(rect);
            Color accent = accentOverride
                ?? (destructive ? Negative : active ? Cyan : Gold);
            Color border = active || hovered
                ? new Color(
                    accent.r,
                    accent.g,
                    accent.b,
                    enabled ? 0.94f : 0.30f)
                : emphasized
                    ? new Color(
                        accent.r,
                        accent.g,
                        accent.b,
                        enabled ? 0.62f : 0.28f)
                    : new Color(
                        0.38f,
                        0.40f,
                        0.40f,
                        enabled ? 0.80f : 0.28f);
            Color fill = active
                ? new Color(0.10f, 0.23f, 0.25f, 0.98f)
                : hovered
                    ? Hover
                    : new Color(
                        Panel.r,
                        Panel.g,
                        Panel.b,
                        enabled ? 0.98f : 0.55f);
            DrawRoundedOutline(rect, border, fill, 1f);

            Rect labelRect = rect.ContractedBy(8f, 2f);
            Color oldColor = GUI.color;
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.WordWrap = false;
            GUI.color = enabled
                ? hovered || active || emphasized ? Color.white : Muted
                : new Color(0.45f, 0.46f, 0.45f);
            Widgets.Label(labelRect, label);
            if (Text.CalcSize(label).x > labelRect.width)
            {
                TooltipHandler.TipRegion(labelRect, label);
            }
            GUI.color = oldColor;
            Text.WordWrap = oldWrap;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            return Widgets.ButtonInvisible(rect);
        }

        /// <summary>Muted secondary text. The height follows the wrapped text.</summary>
        public static void Note(Listing_Standard listing, string text)
        {
            Color oldColor = GUI.color;
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;
            GUI.color = Muted;
            Rect rect = listing.GetRect(Text.CalcHeight(text, listing.ColumnWidth));
            Widgets.Label(rect, text);
            GUI.color = oldColor;
            Text.WordWrap = oldWrap;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            listing.Gap(6f);
        }

        /// <summary>One read-only value: muted label on the left, accent value on
        /// the right, with a faint divider. Both are clipped, never wrapped.</summary>
        public static void Readout(
            Listing_Standard listing,
            string label,
            string value,
            Color valueColor)
        {
            Rect rect = listing.GetRect(28f);
            Color oldColor = GUI.color;
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            Text.Font = GameFont.Small;
            Text.WordWrap = false;

            Widgets.DrawBoxSolid(
                new Rect(rect.x, rect.yMax - 1f, rect.width, 1f),
                new Color(1f, 1f, 1f, 0.07f));
            Rect labelRect = new Rect(
                rect.x + 4f,
                rect.y,
                rect.width * 0.4f,
                rect.height);
            Rect valueRect = new Rect(
                labelRect.xMax,
                rect.y,
                rect.xMax - labelRect.xMax - 4f,
                rect.height);
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Muted;
            Widgets.Label(labelRect, label);
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = valueColor;
            Widgets.Label(valueRect, value);
            if (Text.CalcSize(value).x > valueRect.width)
            {
                TooltipHandler.TipRegion(valueRect, value);
            }

            GUI.color = oldColor;
            Text.WordWrap = oldWrap;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            listing.Gap(2f);
        }

        /// <summary>A clipped, muted label for a row that has a control on its
        /// right (numeric fields, text fields).</summary>
        public static void RowLabel(Rect rect, string text)
        {
            Color oldColor = GUI.color;
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.WordWrap = false;
            GUI.color = Muted;
            Widgets.Label(rect, text);
            if (Text.CalcSize(text).x > rect.width)
            {
                TooltipHandler.TipRegion(rect, text);
            }

            GUI.color = oldColor;
            Text.WordWrap = oldWrap;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
        }

        /// <summary>A call-out box: accent title over wrapped detail text.</summary>
        public static void Banner(
            Listing_Standard listing,
            string title,
            string detail,
            Color accent)
        {
            const float padding = 12f;
            const float titleHeight = 22f;
            Color oldColor = GUI.color;
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            Text.Font = GameFont.Small;
            Text.WordWrap = true;
            float detailHeight = Text.CalcHeight(
                detail,
                listing.ColumnWidth - padding * 2f - 4f);
            Rect rect = listing.GetRect(
                padding + titleHeight + detailHeight + padding - 2f);
            DrawRoundedOutline(
                rect,
                new Color(accent.r, accent.g, accent.b, 0.78f),
                new Color(
                    accent.r * 0.16f + 0.04f,
                    accent.g * 0.16f + 0.04f,
                    accent.b * 0.16f + 0.04f,
                    0.98f),
                1f);
            Widgets.DrawBoxSolid(
                new Rect(rect.x + 5f, rect.y + 7f, 3f, rect.height - 14f),
                accent);

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = accent;
            Text.WordWrap = false;
            Widgets.Label(
                new Rect(
                    rect.x + padding + 4f,
                    rect.y + padding - 2f,
                    rect.width - padding * 2f - 4f,
                    titleHeight),
                title);
            Text.WordWrap = true;
            GUI.color = Color.white;
            Widgets.Label(
                new Rect(
                    rect.x + padding + 4f,
                    rect.y + padding - 2f + titleHeight,
                    rect.width - padding * 2f - 4f,
                    detailHeight),
                detail);
            GUI.color = oldColor;
            Text.WordWrap = oldWrap;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            listing.Gap(6f);
        }

        /// <summary>A row of equal-width choices; the selected one is lit as a
        /// whole. Returns the clicked index, or -1.</summary>
        public static int Segmented(
            Listing_Standard listing,
            string[] labels,
            int selected)
        {
            const float gap = 6f;
            Rect row = listing.GetRect(36f);
            float cell = (row.width - gap * (labels.Length - 1)) / labels.Length;
            int clicked = -1;
            for (int i = 0; i < labels.Length; i++)
            {
                Rect rect = new Rect(
                    row.x + i * (cell + gap),
                    row.y,
                    cell,
                    row.height);
                if (Button(rect, labels[i], i == selected, false, Cyan))
                {
                    clicked = i;
                }
            }

            listing.Gap(4f);
            return clicked;
        }

        /// <summary>A thin capsule progress meter: dark track, coloured fill.
        /// Two capsules per call, no allocation.</summary>
        public static void DrawMeter(Rect rect, float fraction, Color fill)
        {
            DrawRoundedOutline(
                rect,
                new Color(1f, 1f, 1f, 0.30f),
                new Color(0.04f, 0.05f, 0.06f, 0.82f),
                1f);
            Rect inner = rect.ContractedBy(1f);
            float width = Mathf.Clamp01(fraction) * inner.width;
            if (width < 0.5f)
            {
                return;
            }

            DrawCapsule(
                new Rect(
                    inner.x,
                    inner.y,
                    Mathf.Max(inner.height, width),
                    inner.height),
                fill);
        }

        private static readonly string[] HourLabels =
        {
            "00",
            "06",
            "12",
            "18",
            "24"
        };

        private const string ScheduleTip =
            "One day from 00:00 to 24:00. Cyan is permitted play; the gold "
            + "marker is the time now.";

        /// <summary>The day as a bar: permitted windows in cyan (a window that
        /// crosses midnight wraps), hour ticks, and a gold marker for now.</summary>
        public static void ScheduleStrip(
            Listing_Standard listing,
            IntRange first,
            IntRange second,
            IntRange third,
            int count,
            int nowMinute)
        {
            Rect row = listing.GetRect(52f);
            Rect bar = new Rect(row.x + 14f, row.y + 9f, row.width - 28f, 16f);
            DrawRoundedOutline(
                bar,
                new Color(0.38f, 0.40f, 0.40f, 0.80f),
                new Color(0.035f, 0.042f, 0.047f, 0.98f),
                1f);

            Color segment = new Color(Cyan.r, Cyan.g, Cyan.b, 0.88f);
            DrawWindowSegments(bar, first, segment);
            if (count >= 2)
            {
                DrawWindowSegments(bar, second, segment);
            }

            if (count >= 3)
            {
                DrawWindowSegments(bar, third, segment);
            }

            for (int hour = 3; hour < 24; hour += 3)
            {
                Widgets.DrawBoxSolid(
                    new Rect(
                        bar.x + bar.width * hour / 24f - 0.5f,
                        bar.y + 2f,
                        1f,
                        bar.height - 4f),
                    new Color(1f, 1f, 1f, hour % 6 == 0 ? 0.24f : 0.10f));
            }

            Color oldColor = GUI.color;
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperCenter;
            Text.WordWrap = false;
            GUI.color = Muted;
            for (int i = 0; i < HourLabels.Length; i++)
            {
                Widgets.Label(
                    new Rect(
                        bar.x + bar.width * (i * 6) / 24f - 14f,
                        bar.yMax + 3f,
                        28f,
                        // 1.6's Unity font metrics need more than 14px or the
                        // bottoms of the digits are clipped.
                        18f),
                    HourLabels[i]);
            }

            GUI.color = oldColor;
            Text.WordWrap = oldWrap;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;

            float nowX = bar.x + bar.width * Mathf.Clamp(nowMinute, 0, 1439)
                / 1440f;
            Widgets.DrawBoxSolid(
                new Rect(nowX - 1f, bar.y - 3f, 2f, bar.height + 6f),
                Gold);
            DrawCircle(new Rect(nowX - 3.5f, bar.y - 8f, 7f, 7f), Gold);

            if (Mouse.IsOver(row))
            {
                TooltipHandler.TipRegion(row, ScheduleTip);
            }

            listing.Gap(2f);
        }

        private static void DrawWindowSegments(
            Rect bar,
            IntRange range,
            Color color)
        {
            int start = Mathf.Clamp(range.min, 0, 1439);
            int end = Mathf.Clamp(range.max, 0, 1439) + 1;
            if (start < end)
            {
                FillSegment(bar, start, end, color);
                return;
            }

            // The window crosses midnight: draw the evening and the morning.
            FillSegment(bar, start, 1440, color);
            FillSegment(bar, 0, end, color);
        }

        private static void FillSegment(
            Rect bar,
            int startMinute,
            int endMinute,
            Color color)
        {
            float x0 = bar.x + bar.width * startMinute / 1440f;
            float x1 = bar.x + bar.width * endMinute / 1440f;
            Widgets.DrawBoxSolid(
                new Rect(
                    x0,
                    bar.y + 2f,
                    Mathf.Max(2f, x1 - x0),
                    bar.height - 4f),
                color);
        }

        /// <summary>Explains the HUD colours: one dot and label per state.</summary>
        public static void ColorLegend(Listing_Standard listing)
        {
            Rect row = listing.GetRect(24f);
            float cell = row.width / 4f;
            DrawLegendItem(new Rect(row.x, row.y, cell, row.height), Cyan, "On track");
            DrawLegendItem(
                new Rect(row.x + cell, row.y, cell, row.height),
                Gold,
                "Wrap up soon");
            DrawLegendItem(
                new Rect(row.x + cell * 2f, row.y, cell, row.height),
                Warning,
                "Snoozing");
            DrawLegendItem(
                new Rect(row.x + cell * 3f, row.y, cell, row.height),
                Negative,
                "Over the limit");
            listing.Gap(4f);
        }

        private static void DrawLegendItem(Rect rect, Color color, string label)
        {
            DrawCircle(
                new Rect(rect.x + 4f, rect.y + (rect.height - 10f) / 2f, 10f, 10f),
                color);
            Color oldColor = GUI.color;
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.WordWrap = false;
            GUI.color = Muted;
            Widgets.Label(
                new Rect(rect.x + 20f, rect.y, rect.width - 22f, rect.height),
                label);
            GUI.color = oldColor;
            Text.WordWrap = oldWrap;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
        }

        public static bool IconButton(
            Rect rect,
            Texture2D icon,
            string tooltip)
        {
            bool hovered = GUI.enabled && Mouse.IsOver(rect);
            DrawRoundedOutline(
                rect,
                hovered
                    ? new Color(Gold.r, Gold.g, Gold.b, 0.94f)
                    : new Color(0.38f, 0.40f, 0.40f, 0.80f),
                hovered ? Hover : Panel,
                1f);
            Color previous = GUI.color;
            GUI.color = hovered ? Gold : Color.white;
            GUI.DrawTexture(rect.ContractedBy(5f), icon, ScaleMode.ScaleToFit);
            GUI.color = previous;
            TooltipHandler.TipRegion(rect, tooltip);
            return Widgets.ButtonInvisible(rect);
        }

        public static void DrawField(Rect rect)
        {
            DrawRoundedOutline(
                rect,
                new Color(Cyan.r, Cyan.g, Cyan.b, 0.56f),
                new Color(0.035f, 0.042f, 0.047f, 0.98f),
                1f);
        }

        private static void DrawRounded(
            Rect rect,
            Color color,
            bool shadow = false)
        {
            if (shadow)
            {
                DrawRoundedShape(
                    new Rect(rect.x + 2f, rect.y + 3f, rect.width, rect.height),
                    new Color(0f, 0f, 0f, 0.30f));
            }
            DrawRoundedShape(rect, color);
        }

        private static void DrawRoundedShape(Rect rect, Color color)
        {
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            if (rect.width <= Slice * 2f || rect.height <= Slice * 2f)
            {
                DrawCapsule(rect, color);
                return;
            }

            Color previous = GUI.color;
            GUI.color = color;
            GUI.Box(rect, GUIContent.none, RoundedStyle);
            GUI.color = previous;
        }

        private static void DrawCapsule(Rect rect, Color color)
        {
            if (Mathf.Approximately(rect.width, rect.height))
            {
                DrawCircle(rect, color);
                return;
            }

            if (rect.width > rect.height)
            {
                float diameter = rect.height;
                float radius = diameter * 0.5f;
                Widgets.DrawBoxSolid(
                    new Rect(
                        rect.x + radius,
                        rect.y,
                        Mathf.Max(0f, rect.width - diameter),
                        rect.height),
                    color);
                DrawCircle(new Rect(rect.x, rect.y, diameter, diameter), color);
                DrawCircle(
                    new Rect(rect.xMax - diameter, rect.y, diameter, diameter),
                    color);
                return;
            }

            float verticalDiameter = rect.width;
            float verticalRadius = verticalDiameter * 0.5f;
            Widgets.DrawBoxSolid(
                new Rect(
                    rect.x,
                    rect.y + verticalRadius,
                    rect.width,
                    Mathf.Max(0f, rect.height - verticalDiameter)),
                color);
            DrawCircle(
                new Rect(rect.x, rect.y, verticalDiameter, verticalDiameter),
                color);
            DrawCircle(
                new Rect(
                    rect.x,
                    rect.yMax - verticalDiameter,
                    verticalDiameter,
                    verticalDiameter),
                color);
        }

        private static void DrawCircle(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, CircleMask);
            GUI.color = previous;
        }

        private static void DrawRoundedOutline(
            Rect rect,
            Color border,
            Color fill,
            float thickness,
            bool shadow = false)
        {
            DrawRounded(rect, border, shadow);
            DrawRounded(rect.ContractedBy(thickness), fill);
        }

        private static Texture2D MakeRoundedMask()
        {
            Texture2D texture = NewTexture(
                RoundedSize,
                RoundedSize,
                "TouchGrass_RoundedMask");
            Color[] pixels = new Color[RoundedSize * RoundedSize];
            float radius = Slice - 1f;
            for (int y = 0; y < RoundedSize; y++)
            {
                for (int x = 0; x < RoundedSize; x++)
                {
                    float nearestX = Mathf.Clamp(
                        x,
                        radius,
                        RoundedSize - 1f - radius);
                    float nearestY = Mathf.Clamp(
                        y,
                        radius,
                        RoundedSize - 1f - radius);
                    float distance = Vector2.Distance(
                        new Vector2(x, y),
                        new Vector2(nearestX, nearestY));
                    float alpha = Mathf.Clamp01(radius + 0.75f - distance);
                    pixels[y * RoundedSize + x] =
                        new Color(1f, 1f, 1f, alpha);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static Texture2D MakeCircleMask()
        {
            Texture2D texture = NewTexture(
                CircleSize,
                CircleSize,
                "TouchGrass_CircleMask");
            Color[] pixels = new Color[CircleSize * CircleSize];
            Vector2 center = new Vector2(
                (CircleSize - 1f) * 0.5f,
                (CircleSize - 1f) * 0.5f);
            float radius = CircleSize * 0.5f - 1.5f;
            for (int y = 0; y < CircleSize; y++)
            {
                for (int x = 0; x < CircleSize; x++)
                {
                    float distance = Vector2.Distance(
                        new Vector2(x, y),
                        center);
                    float alpha = Mathf.Clamp01(radius + 0.75f - distance);
                    pixels[y * CircleSize + x] =
                        new Color(1f, 1f, 1f, alpha);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static Texture2D MakeMicroPattern()
        {
            Texture2D texture = NewTexture(
                PatternSize,
                PatternSize,
                "TouchGrass_MicroPattern");
            texture.wrapMode = TextureWrapMode.Repeat;
            Color[] pixels = new Color[PatternSize * PatternSize];
            for (int y = 0; y < PatternSize; y++)
            {
                for (int x = 0; x < PatternSize; x++)
                {
                    float alpha = 0f;
                    if (x % 16 == 0 || y % 16 == 0)
                    {
                        alpha = 0.07f;
                    }
                    if (x % 16 == 0 && y % 16 == 0)
                    {
                        alpha = 0.21f;
                    }
                    pixels[y * PatternSize + x] =
                        new Color(1f, 1f, 1f, alpha);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static Texture2D NewTexture(
            int width,
            int height,
            string name)
        {
            return new Texture2D(width, height, TextureFormat.ARGB32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
#if !RIMWORLD_1_7
                // Unity (1.6) may unload unreferenced assets; 1.7's Unity
                // compatibility layer implements hideFlags as a logging stub.
                hideFlags = HideFlags.HideAndDontSave
#endif
            };
        }

        private static Color FromBytes(byte red, byte green, byte blue)
        {
            return new Color32(red, green, blue, 255);
        }
    }
}
