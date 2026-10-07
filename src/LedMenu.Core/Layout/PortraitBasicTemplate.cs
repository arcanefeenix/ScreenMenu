using LedMenu.Core.Calibration;

namespace LedMenu.Core.Layout;

/// <summary>
/// The first template, designed for the 336x672 portrait LED screen. Every size here is in LED pixels.
/// Each text role has a preferred size and an explicit minimum. The preferred size is always used; the
/// minimum is only a last resort for a single item that would not otherwise fit on an empty page.
/// Nothing is shrunk to fit more content on a page: that is what pagination is for.
/// </summary>
public static class PortraitBasicTemplate
{
    public const string Id = "portrait-basic";

    // ---- typography: preferred / minimum, in pixels ----
    public static readonly TextStyle Header = new(28, FontWeightKind.Bold);
    public static readonly TextStyle HeaderMin = new(22, FontWeightKind.Bold);
    public static readonly TextStyle Subtitle = new(15, FontWeightKind.Regular);
    public static readonly TextStyle SubtitleMin = new(13, FontWeightKind.Regular);
    public static readonly TextStyle Category = new(18, FontWeightKind.Bold);
    public static readonly TextStyle CategoryMin = new(16, FontWeightKind.Bold);
    public static readonly TextStyle ItemName = new(20, FontWeightKind.Bold);
    public static readonly TextStyle ItemNameMin = new(18, FontWeightKind.Bold);
    public static readonly TextStyle Description = new(14, FontWeightKind.Regular);
    public static readonly TextStyle DescriptionMin = new(13, FontWeightKind.Regular);
    public static readonly TextStyle Price = new(20, FontWeightKind.Bold);
    public static readonly TextStyle PriceMin = new(18, FontWeightKind.Bold);
    public static readonly TextStyle SoldOut = new(12, FontWeightKind.Bold);
    public static readonly TextStyle SoldOutMin = new(11, FontWeightKind.Bold);
    public static readonly TextStyle PageIndicator = new(12, FontWeightKind.Regular);

    // ---- spacing, pixels ----
    public const int MarginX = 8;
    public const int MarginTop = 6;
    public const int MarginBottom = 6;
    public const int LogoMaxHeight = 64;
    public const int LogoGap = 6;
    public const int HeaderLineMax = 2;           // title lines at the preferred size before dropping to the minimum
    public const int SubtitleLineMax = 2;
    public const int RuleHeight = 2;
    public const int RuleGap = 6;
    public const int ItemGap = 8;
    public const int CategoryGap = 12;
    public const int HeadingPadX = 6;
    public const int HeadingPadY = 2;
    public const int HeadingAfterGap = 6;
    public const int NameGap = 8;
    public const int DescriptionGap = 1;
    public const int BadgePadX = 5;
    public const int BadgePadY = 1;
    public const int FeaturedBarWidth = 4;
    public const int FeaturedBarX = 1;
    public const int FooterGap = 2;
    public const double InlinePriceMaxShare = 0.45;   // a price wider than this share of the row goes on its own line

    public const string SoldOutText = "SOLD OUT";
    public const string ContinuedSuffix = " (CONT.)";

    // ---- colors: black background, high contrast, one accent ----
    public static readonly Rgb Background = Rgb.Black;
    public static readonly Rgb Accent = new(255, 179, 0);           // amber
    public static readonly Rgb TitleColor = Rgb.White;
    public static readonly Rgb SubtitleColor = new(190, 190, 190);
    public static readonly Rgb NameColor = Rgb.White;
    public static readonly Rgb FeaturedNameColor = new(255, 214, 79);
    public static readonly Rgb PriceColor = Accent;
    public static readonly Rgb DescriptionColor = new(175, 175, 175);
    public static readonly Rgb SoldOutNameColor = new(120, 120, 120);
    public static readonly Rgb SoldOutDescriptionColor = new(85, 85, 85);
    public static readonly Rgb StrikeColor = new(150, 150, 150);
    public static readonly Rgb BadgeColor = new(211, 47, 47);
    public static readonly Rgb BadgeTextColor = Rgb.White;
    public static readonly Rgb HeadingTextColor = Rgb.Black;
    public static readonly Rgb IndicatorColor = new(150, 150, 150);
}
