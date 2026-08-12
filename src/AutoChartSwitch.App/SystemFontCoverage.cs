using System.Text;
using System.Windows;
using System.Windows.Media;

namespace AutoChartSwitch.App;

internal sealed class SystemFontCoverage
{
    private readonly IDictionary<int, ushort>? _glyphs;

    public SystemFontCoverage(string familyName)
    {
        try
        {
            var family = Fonts.SystemFontFamilies.FirstOrDefault(candidate =>
                string.Equals(candidate.Source, familyName, StringComparison.OrdinalIgnoreCase));
            if (family is null) return;

            var normal = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            if (normal.TryGetGlyphTypeface(out var glyphTypeface))
            {
                _glyphs = glyphTypeface.CharacterToGlyphMap;
                return;
            }

            foreach (var typeface in family.GetTypefaces())
            {
                if (!typeface.TryGetGlyphTypeface(out glyphTypeface)) continue;
                _glyphs = glyphTypeface.CharacterToGlyphMap;
                return;
            }
        }
        catch (Exception) { }
    }

    public bool Supports(string text)
    {
        if (_glyphs is null) return false;
        foreach (var rune in text.EnumerateRunes())
        {
            if (!Rune.IsControl(rune) && !_glyphs.ContainsKey(rune.Value)) return false;
        }
        return true;
    }
}
