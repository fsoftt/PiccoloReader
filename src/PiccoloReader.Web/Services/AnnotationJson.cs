using System.Globalization;
using System.Text;
using System.Text.Json;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;

namespace PiccoloReader.Web.Services;

// Turns Core annotations into the draw list annot.js paints (all geometry rules stay in Core:
// hairpin lines, slur control points, icon catalog). Written by hand so it survives trimming.
public static class AnnotationJson
{
    public static string Build(IEnumerable<Annotation> items)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartArray();
            foreach (var a in items)
            {
                Write(w, a);
            }

            w.WriteEndArray();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public static string Tiles(IReadOnlyList<MusicIcon> icons)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartArray();
            foreach (var icon in icons)
            {
                w.WriteStartObject();
                if (SlurGeometry.IsSlur(icon.Key))
                {
                    w.WriteBoolean("slur", true);
                    w.WriteStartArray("ctl");
                    foreach (var p in SlurGeometry.DefaultPoints(SlurGeometry.IsAbove(icon.Key)))
                    {
                        w.WriteNumberValue(p.X);
                        w.WriteNumberValue(p.Y);
                    }

                    w.WriteEndArray();
                }
                else
                {
                    w.WriteNumber("cp", icon.Codepoint);
                    w.WriteNumber("vs", icon.VisualScale);
                }

                w.WriteEndObject();
            }

            w.WriteEndArray();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    // "#RRGGBB" or "#AARRGGBB" (the highlighter) as a CSS color.
    public static string Css(string? hex)
    {
        if (hex is { Length: 7 or 9 } && hex[0] == '#'
            && int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
        {
            var a = hex.Length == 9 ? (v >> 24) & 0xFF : 255;
            return string.Create(CultureInfo.InvariantCulture, $"rgba({(v >> 16) & 0xFF},{(v >> 8) & 0xFF},{v & 0xFF},{a / 255.0:0.###})");
        }

        return "#000";
    }

    private static void Write(Utf8JsonWriter w, Annotation a)
    {
        if (a.IsStroke)
        {
            if (a.ColorHex is null)
            {
                return;
            }

            w.WriteStartObject();
            w.WriteNumber("id", a.Id);
            w.WriteString("k", "s");
            w.WriteString("c", Css(a.ColorHex));
            w.WriteNumber("w", a.StrokeWidth);
            w.WriteStartArray("p");
            foreach (var p in AnnotationService.DeserializePoints(a.Points))
            {
                w.WriteNumberValue(p.X);
                w.WriteNumberValue(p.Y);
            }

            w.WriteEndArray();
            w.WriteEndObject();
            return;
        }

        string kind;
        if (a.IsText)
        {
            if (string.IsNullOrEmpty(a.Text))
            {
                return;
            }

            kind = "x";
        }
        else
        {
            if (MusicIconCatalog.FindByKey(a.IconKey) is null)
            {
                return;
            }

            kind = HairpinGeometry.IsHairpin(a.IconKey) ? "h" : SlurGeometry.IsSlur(a.IconKey) ? "l" : "g";
        }

        w.WriteStartObject();
        w.WriteNumber("id", a.Id);
        w.WriteString("k", kind);
        w.WriteNumber("x", a.X);
        w.WriteNumber("y", a.Y);
        w.WriteNumber("w", a.Width);
        w.WriteNumber("h", a.Height);
        switch (kind)
        {
            case "x":
                w.WriteString("t", a.Text);
                w.WriteString("c", Css(a.ColorHex));
                break;
            case "h":
                w.WriteString("c", Css(a.ColorHex));
                w.WriteStartArray("ln");
                foreach (var (x1, y1, x2, y2) in HairpinGeometry.Lines(a.IconKey))
                {
                    w.WriteNumberValue(x1);
                    w.WriteNumberValue(y1);
                    w.WriteNumberValue(x2);
                    w.WriteNumberValue(y2);
                }

                w.WriteEndArray();
                break;
            case "l":
                w.WriteString("c", Css(a.ColorHex));
                w.WriteNumber("th", SlurGeometry.MaxThickness);
                w.WriteNumber("tip", SlurGeometry.TipThicknessFactor);
                w.WriteStartArray("cp");
                foreach (var p in SlurGeometry.ControlPoints(a))
                {
                    w.WriteNumberValue(p.X);
                    w.WriteNumberValue(p.Y);
                }

                w.WriteEndArray();
                break;
            default:
                var icon = MusicIconCatalog.FindByKey(a.IconKey)!;
                w.WriteNumber("cp", icon.Codepoint);
                w.WriteNumber("vs", icon.VisualScale);
                break;
        }

        w.WriteEndObject();
    }
}
