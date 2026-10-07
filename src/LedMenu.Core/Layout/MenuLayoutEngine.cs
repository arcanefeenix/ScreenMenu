using LedMenu.Core.Menus;
using static LedMenu.Core.Layout.PortraitBasicTemplate;

namespace LedMenu.Core.Layout;

public enum LayoutProblemKind
{
    /// <summary>One item is taller than an empty page even at the minimum type sizes. It is not drawn.</summary>
    ItemTooTall,
    /// <summary>An item fits alone, but not together with its category heading. It is not drawn.</summary>
    ItemWithHeadingTooTall,
}

public sealed record LayoutProblem(LayoutProblemKind Kind, string Message, string? ItemName = null);

public sealed record MenuPage(int Number, int Count, IReadOnlyList<DrawOp> Ops);

public sealed record MenuLayoutResult(int Width, int Height, IReadOnlyList<MenuPage> Pages, IReadOnlyList<LayoutProblem> Problems)
{
    public int PageCount => Pages.Count;
    public bool HasOverflow => Problems.Count > 0;
}

/// <summary>
/// Turns a <see cref="MenuView"/> into pages of drawing instructions at the screen's exact pixel size.
/// Page breaks come from measured text heights. Rules:
/// an item is never split across pages; a category heading is never left at the bottom of a page without at
/// least one of its items; a category continued on the next page repeats its heading with "(CONT.)"; hidden
/// things are not in the view so they take no space; an item that cannot fit an empty page is reported, never clipped.
/// The header (logo, title, subtitle) is repeated on every page.
/// </summary>
public static class MenuLayoutEngine
{
    public static MenuLayoutResult Layout(MenuView view, MenuTheme theme, int width, int height,
        LogoMetrics? logo, ITextMeasurer m)
    {
        var first = Pass(view, width, height, logo, m, footerReserve: 0, indicator: false);
        if (first.Pages.Count <= 1 || !theme.ShowPageIndicator) return first;

        // several pages: take a strip at the bottom for the page number and lay out again
        var footer = (int)Math.Ceiling(m.LineHeight(PageIndicator)) + FooterGap;
        return Pass(view, width, height, logo, m, footer, indicator: true);
    }

    private static MenuLayoutResult Pass(MenuView view, int width, int height, LogoMetrics? logo,
        ITextMeasurer m, int footerReserve, bool indicator)
    {
        var contentW = Math.Max(1, width - 2 * MarginX);
        var headerOps = new List<DrawOp>();
        var contentTop = BuildHeader(view, logo, m, contentW, headerOps);
        var contentBottom = height - MarginBottom - footerReserve;
        var contentHeight = Math.Max(0, contentBottom - contentTop);

        var pages = new List<List<DrawOp>>();
        var problems = new List<LayoutProblem>();
        var ops = new List<DrawOp>(headerOps);
        pages.Add(ops);
        var y = contentTop;
        var pageHasContent = false;

        foreach (var group in view.Groups)
        {
            var heading = string.IsNullOrWhiteSpace(group.Category?.Name) ? null : group.Category!.Name.Trim().ToUpperInvariant();
            var groupStarted = false;          // has any item of this group been placed, on any page
            var headingOnThisPage = false;

            foreach (var item in group.Items)
            {
                var block = Measure(item, m, contentW, useMinimum: false);
                if (block.Height > contentHeight) block = Measure(item, m, contentW, useMinimum: true);
                if (block.Height > contentHeight)
                {
                    problems.Add(new LayoutProblem(LayoutProblemKind.ItemTooTall,
                        $"\"{ItemLabel(item)}\" is too tall for one page even at the smallest allowed type " +
                        $"({block.Height} px needed, {contentHeight} px available). It was not drawn.", ItemLabel(item)));
                    continue;
                }

                var headingBlock = heading != null && !headingOnThisPage ? MeasureHeading(heading, groupStarted, m, contentW) : null;

                if (pageHasContent && y + Gap(headingBlock) + Total(headingBlock, block) > contentBottom)
                {
                    // does not fit below what is already here: start a new page; the heading (if the group is open) comes with it
                    ops = new List<DrawOp>(headerOps);
                    pages.Add(ops);
                    y = contentTop;
                    pageHasContent = false;
                    headingOnThisPage = false;
                    headingBlock = heading != null ? MeasureHeading(heading, groupStarted, m, contentW) : null;
                }

                if (!pageHasContent && y + Total(headingBlock, block) > contentBottom)
                {
                    problems.Add(new LayoutProblem(LayoutProblemKind.ItemWithHeadingTooTall,
                        $"\"{ItemLabel(item)}\" does not fit on a page together with its category heading. It was not drawn.",
                        ItemLabel(item)));
                    continue;
                }

                if (pageHasContent) y += Gap(headingBlock);
                if (headingBlock != null)
                {
                    EmitHeading(headingBlock, y, contentW, ops);
                    y += headingBlock.Height + HeadingAfterGap;
                    headingOnThisPage = true;
                }
                EmitItem(block, y, contentW, m, ops);
                y += block.Height;
                pageHasContent = true;
                groupStarted = true;
            }
        }

        var count = pages.Count;
        var result = new List<MenuPage>(count);
        for (var i = 0; i < count; i++)
        {
            var pageOps = pages[i];
            if (indicator && count > 1)
            {
                var text = $"{i + 1}/{count}";
                var w = (int)Math.Ceiling(m.Width(text, PageIndicator));
                var lh = (int)Math.Ceiling(m.LineHeight(PageIndicator));
                pageOps.Add(new TextOp(text, MarginX + contentW - w, height - MarginBottom - lh, PageIndicator, IndicatorColor));
            }
            result.Add(new MenuPage(i + 1, count, pageOps));
        }
        return new MenuLayoutResult(width, height, result, problems);
    }

    private static int Gap(HeadingBlock? heading) => heading != null ? CategoryGap : ItemGap;
    private static int Total(HeadingBlock? heading, ItemBlock item) =>
        (heading != null ? heading.Height + HeadingAfterGap : 0) + item.Height;

    // ---- header ----------------------------------------------------------------------------

    /// <summary>Draws the header and returns the y where content starts.</summary>
    private static int BuildHeader(MenuView view, LogoMetrics? logo, ITextMeasurer m, int contentW, List<DrawOp> ops)
    {
        var y = MarginTop;

        if (logo is { PixelWidth: > 0, PixelHeight: > 0 } l)
        {
            // fit inside contentW x LogoMaxHeight, keeping the logo's own proportions (never stretched)
            var scale = Math.Min((double)contentW / l.PixelWidth, (double)LogoMaxHeight / l.PixelHeight);
            var w = Math.Min(contentW, Math.Max(1, (int)Math.Round(l.PixelWidth * scale)));
            var h = Math.Min(LogoMaxHeight, Math.Max(1, (int)Math.Round(l.PixelHeight * scale)));
            ops.Add(new LogoOp(MarginX + (contentW - w) / 2, y, w, h));
            y += h + LogoGap;
        }

        var center = MarginX + contentW / 2;

        if (!string.IsNullOrWhiteSpace(view.Header))
        {
            var style = Header;
            var lines = Wrap(view.Header, style, contentW, m);
            if (lines.Count > HeaderLineMax)
            {
                style = HeaderMin;
                lines = Wrap(view.Header, style, contentW, m);
            }
            y = CenteredLines(lines, style, center, y, TitleColor, m, ops);
        }

        if (!string.IsNullOrWhiteSpace(view.Subtitle))
        {
            y += 2;
            var style = Subtitle;
            var lines = Wrap(view.Subtitle!, style, contentW, m);
            if (lines.Count > SubtitleLineMax)
            {
                style = SubtitleMin;
                lines = Wrap(view.Subtitle!, style, contentW, m);
            }
            y = CenteredLines(lines, style, center, y, SubtitleColor, m, ops);
        }

        y += RuleGap;
        ops.Add(new FillOp(MarginX, y, contentW, RuleHeight, Accent));
        return y + RuleHeight + RuleGap;
    }

    private static int CenteredLines(IReadOnlyList<string> lines, TextStyle style, int center, int y,
        Calibration.Rgb color, ITextMeasurer m, List<DrawOp> ops)
    {
        var lh = (int)Math.Ceiling(m.LineHeight(style));
        foreach (var line in lines)
        {
            var w = (int)Math.Ceiling(m.Width(line, style));
            ops.Add(new TextOp(line, center - w / 2, y, style, color));
            y += lh;
        }
        return y;
    }

    // ---- category heading ------------------------------------------------------------------

    private sealed record HeadingBlock(IReadOnlyList<string> Lines, TextStyle Style, int LineHeight, int Height);

    private static HeadingBlock MeasureHeading(string name, bool continued, ITextMeasurer m, int contentW)
    {
        var text = continued ? name + ContinuedSuffix : name;
        var lines = Wrap(text, Category, contentW - 2 * HeadingPadX, m);
        var lh = (int)Math.Ceiling(m.LineHeight(Category));
        return new HeadingBlock(lines, Category, lh, lines.Count * lh + 2 * HeadingPadY);
    }

    private static void EmitHeading(HeadingBlock h, int y, int contentW, List<DrawOp> ops)
    {
        ops.Add(new FillOp(MarginX, y, contentW, h.Height, Accent));
        for (var i = 0; i < h.Lines.Count; i++)
            ops.Add(new TextOp(h.Lines[i], MarginX + HeadingPadX, y + HeadingPadY + i * h.LineHeight, h.Style, HeadingTextColor));
    }

    // ---- items -----------------------------------------------------------------------------

    private sealed class ItemBlock
    {
        public required MenuItem Item { get; init; }
        public required TextStyle NameStyle { get; init; }
        public required TextStyle PriceStyle { get; init; }
        public required TextStyle DescStyle { get; init; }
        public required TextStyle BadgeStyle { get; init; }
        public required IReadOnlyList<string> NameLines { get; init; }
        public required IReadOnlyList<string> PriceLines { get; init; }
        public required IReadOnlyList<string> DescLines { get; init; }
        public required bool Inline { get; init; }
        public required bool SoldOut { get; init; }
        public required int NameLH { get; init; }
        public required int PriceLH { get; init; }
        public required int DescLH { get; init; }
        public required int BadgeW { get; init; }
        public required int BadgeH { get; init; }
        public required int FirstRowHeight { get; init; }
        public required int PriceRowsHeight { get; init; }
        public required int Height { get; init; }
    }

    private static ItemBlock Measure(MenuItem item, ITextMeasurer m, int contentW, bool useMinimum)
    {
        var nameStyle = useMinimum ? ItemNameMin : ItemName;
        var priceStyle = useMinimum ? PriceMin : Price;
        var descStyle = useMinimum ? DescriptionMin : Description;
        var badgeStyle = useMinimum ? SoldOutMin : SoldOut;

        var price = (item.Price ?? "").Trim();
        var desc = (item.Description ?? "").Trim();

        var nameLH = (int)Math.Ceiling(m.LineHeight(nameStyle));
        var priceLH = (int)Math.Ceiling(m.LineHeight(priceStyle));
        var descLH = (int)Math.Ceiling(m.LineHeight(descStyle));
        var badgeW = (int)Math.Ceiling(m.Width(SoldOutText, badgeStyle)) + 2 * BadgePadX;
        var badgeH = (int)Math.Ceiling(m.LineHeight(badgeStyle)) + 2 * BadgePadY;
        var priceW = price.Length > 0 ? (int)Math.Ceiling(m.Width(price, priceStyle)) : 0;

        // a sold-out item reserves room for its badge in place of the price, so the badge never overlaps the name
        var reserve = item.SoldOut ? Math.Max(priceW, badgeW) : priceW;
        var inline = reserve > 0 && reserve <= contentW * InlinePriceMaxShare;
        var nameAreaW = inline ? contentW - reserve - NameGap : contentW;

        var nameLines = Wrap(item.Name ?? "", nameStyle, nameAreaW, m);
        var nameH = nameLines.Count * nameLH;

        IReadOnlyList<string> priceLines = Array.Empty<string>();
        var priceRowsH = 0;
        int firstRow;
        if (inline)
        {
            firstRow = Math.Max(nameH, item.SoldOut ? badgeH : priceLH);
        }
        else
        {
            firstRow = nameH;
            if (item.SoldOut) priceRowsH = badgeH;
            else if (price.Length > 0)
            {
                priceLines = Wrap(price, priceStyle, contentW, m);
                priceRowsH = priceLines.Count * priceLH;
            }
        }

        var descLines = desc.Length > 0 ? Wrap(desc, descStyle, contentW, m) : Array.Empty<string>();
        var descH = descLines.Count > 0 ? DescriptionGap + descLines.Count * descLH : 0;

        return new ItemBlock
        {
            Item = item, NameStyle = nameStyle, PriceStyle = priceStyle, DescStyle = descStyle, BadgeStyle = badgeStyle,
            NameLines = nameLines, PriceLines = priceLines, DescLines = descLines,
            Inline = inline, SoldOut = item.SoldOut,
            NameLH = nameLH, PriceLH = priceLH, DescLH = descLH, BadgeW = badgeW, BadgeH = badgeH,
            FirstRowHeight = firstRow, PriceRowsHeight = priceRowsH,
            Height = firstRow + priceRowsH + descH,
        };
    }

    private static void EmitItem(ItemBlock b, int y, int contentW, ITextMeasurer m, List<DrawOp> ops)
    {
        var x0 = MarginX;
        var right = MarginX + contentW;
        var item = b.Item;

        var nameColor = b.SoldOut ? SoldOutNameColor : item.Featured ? FeaturedNameColor : NameColor;
        for (var i = 0; i < b.NameLines.Count; i++)
        {
            var ly = y + i * b.NameLH;
            ops.Add(new TextOp(b.NameLines[i], x0, ly, b.NameStyle, nameColor));
            if (b.SoldOut && b.NameLines[i].Length > 0)
            {
                // struck through: the item is still there, but clearly not available
                var w = (int)Math.Ceiling(m.Width(b.NameLines[i], b.NameStyle));
                ops.Add(new FillOp(x0, ly + b.NameLH / 2, w, 2, StrikeColor));
            }
        }

        var price = (item.Price ?? "").Trim();
        if (b.Inline)
        {
            if (b.SoldOut)
                EmitBadge(b, right - b.BadgeW, y + (b.NameLH - b.BadgeH) / 2, ops);
            else if (price.Length > 0)
                ops.Add(new TextOp(price, right - (int)Math.Ceiling(m.Width(price, b.PriceStyle)), y, b.PriceStyle, PriceColor));
        }
        else if (b.PriceRowsHeight > 0)
        {
            var py = y + b.FirstRowHeight;
            if (b.SoldOut) EmitBadge(b, right - b.BadgeW, py, ops);
            else
                for (var i = 0; i < b.PriceLines.Count; i++)
                    ops.Add(new TextOp(b.PriceLines[i], right - (int)Math.Ceiling(m.Width(b.PriceLines[i], b.PriceStyle)),
                        py + i * b.PriceLH, b.PriceStyle, PriceColor));
        }

        var descColor = b.SoldOut ? SoldOutDescriptionColor : DescriptionColor;
        var dy = y + b.FirstRowHeight + b.PriceRowsHeight + DescriptionGap;
        for (var i = 0; i < b.DescLines.Count; i++)
            ops.Add(new TextOp(b.DescLines[i], x0, dy + i * b.DescLH, b.DescStyle, descColor));

        // featured: a short accent bar in the left margin; it adds no height and does not move any text
        if (item.Featured && !b.SoldOut)
            ops.Add(new FillOp(FeaturedBarX, y, FeaturedBarWidth, b.Height, Accent));
    }

    private static void EmitBadge(ItemBlock b, int x, int y, List<DrawOp> ops)
    {
        ops.Add(new FillOp(x, y, b.BadgeW, b.BadgeH, BadgeColor));
        ops.Add(new TextOp(SoldOutText, x + BadgePadX, y + BadgePadY, b.BadgeStyle, BadgeTextColor));
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static IReadOnlyList<string> Wrap(string text, TextStyle style, double maxWidth, ITextMeasurer m) =>
        TextWrapping.Wrap(text, maxWidth, s => m.Width(s, style));

    private static string ItemLabel(MenuItem item) => string.IsNullOrWhiteSpace(item.Name) ? "(unnamed item)" : item.Name.Trim();
}
