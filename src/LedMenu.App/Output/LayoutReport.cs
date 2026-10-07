using System.Text;
using LedMenu.Core.Layout;
using LedMenu.Rendering;
using static LedMenu.Core.Layout.PortraitBasicTemplate;

namespace LedMenu.App.Output;

/// <summary>
/// A plain-text summary of how a menu laid out at one screen size: pages, wrapping, minimum-size use, problems.
/// Used by the diagnostic command line to compare sizes; it only reads the layout.
/// </summary>
public static class LayoutReport
{
    public static string Describe(string menuName, MenuRenderResult r, WpfTextMeasurer measurer)
    {
        var sb = new StringBuilder();
        var ops = r.Layout.Pages.SelectMany(p => p.Ops).ToList();
        var texts = ops.OfType<TextOp>().ToList();

        bool IsName(TextOp t) => (t.Color == NameColor || t.Color == FeaturedNameColor || t.Color == SoldOutNameColor)
                                 && (t.Style == ItemName || t.Style == ItemNameMin);

        // group wrapped name lines back into items (consecutive lines one line-height apart on the same page)
        var itemLines = new List<int>();
        foreach (var page in r.Layout.Pages)
        {
            var names = page.Ops.OfType<TextOp>().Where(IsName).OrderBy(t => t.Y).ToList();
            var run = 0;
            for (var i = 0; i < names.Count; i++)
            {
                var lh = (int)Math.Ceiling(measurer.LineHeight(names[i].Style));
                if (i > 0 && names[i].Y - names[i - 1].Y == lh) run++;
                else { if (run > 0) itemLines.Add(run); run = 1; }
            }
            if (run > 0) itemLines.Add(run);
        }

        var titleOps = texts.Where(t => t.Color == TitleColor && (t.Style == Header || t.Style == HeaderMin)).ToList();
        var subtitleOps = texts.Where(t => t.Color == SubtitleColor).ToList();
        var headings = texts.Where(t => t.Color == HeadingTextColor).ToList();
        var priceOwnLine = 0;
        foreach (var page in r.Layout.Pages)
            foreach (var price in page.Ops.OfType<TextOp>().Where(t => t.Color == PriceColor))
                if (page.Ops.OfType<TextOp>().Any(n => IsName(n) && price.Y >= n.Y + (int)Math.Ceiling(measurer.LineHeight(n.Style))
                                                         && price.Y < n.Y + 2 * (int)Math.Ceiling(measurer.LineHeight(n.Style))
                                                         && n.Y + (int)Math.Ceiling(measurer.LineHeight(n.Style)) == price.Y))
                    priceOwnLine++;

        var descLines = texts.Count(t => t.Color == DescriptionColor || t.Color == SoldOutDescriptionColor);
        var minNames = texts.Count(t => IsName(t) && t.Style == ItemNameMin);
        var minPrices = texts.Count(t => t.Color == PriceColor && t.Style == PriceMin);
        var minDesc = texts.Count(t => (t.Color == DescriptionColor || t.Color == SoldOutDescriptionColor) && t.Style == DescriptionMin);
        var minTitle = titleOps.Count(t => t.Style == HeaderMin);
        var minSub = subtitleOps.Count(t => t.Style == SubtitleMin);
        var minHeading = headings.Count(t => t.Style == CategoryMin);
        var minBadge = texts.Count(t => t.Color == BadgeTextColor && t.Style == SoldOutMin);

        var orphans = 0;
        foreach (var page in r.Layout.Pages)
            foreach (var h in page.Ops.OfType<TextOp>().Where(t => t.Color == HeadingTextColor))
                if (!page.Ops.OfType<TextOp>().Any(n => IsName(n) && n.Y > h.Y)) orphans++;

        sb.AppendLine($"  {menuName} @ {r.Width}x{r.Height}");
        sb.AppendLine($"    pages: {r.PageCount}   items drawn: {itemLines.Count}   problems: {r.Problems.Count}");
        sb.AppendLine($"    title lines: {titleOps.Count}   subtitle lines: {subtitleOps.Count}   category heading lines: {headings.Count} (continued: {headings.Count(h => h.Text.EndsWith("(CONT.)", StringComparison.Ordinal))})");
        sb.AppendLine($"    item name lines: total {itemLines.Sum()}, longest {(itemLines.Count == 0 ? 0 : itemLines.Max())}, items with 2+ lines {itemLines.Count(n => n >= 2)}, with 3+ lines {itemLines.Count(n => n >= 3)}");
        sb.AppendLine($"    prices on their own line: {priceOwnLine}   description lines: {descLines}");
        sb.AppendLine($"    minimum-size text drawn: title {minTitle}, subtitle {minSub}, heading {minHeading}, name {minNames}, price {minPrices}, description {minDesc}, badge {minBadge}");
        sb.AppendLine($"    orphan category headings: {orphans}");
        foreach (var p in r.Problems) sb.AppendLine($"    PROBLEM [{p.Kind}]: {p.Message}");
        return sb.ToString();
    }
}
