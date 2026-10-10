using System.Drawing.Drawing2D;
using System.Globalization;

namespace SnapGit.App.Ui;

/// <summary>
/// Reads SVG path data ("M12 3l9 5…") into a <see cref="GraphicsPath"/>: M L H V C S Q T A Z, absolute and relative.
/// The icon set is written as path strings, so shapes stay compact and arcs give the rounded corners modern icons need.
/// </summary>
public static class SvgPath
{
    public static GraphicsPath Parse(string data)
    {
        var path = new GraphicsPath(FillMode.Alternate);
        var reader = new Reader(data);

        PointF current = default, start = default, lastControl = default;
        var previous = '\0';
        var command = '\0';
        var open = false;

        while (reader.SkipSeparators())
        {
            if (reader.PeekIsCommand())
            {
                command = reader.ReadCommand();
                if (!open && char.ToUpperInvariant(command) != 'M') throw new FormatException($"Path data must start with M: {data}");
            }
            else if (command == '\0') throw new FormatException($"Path data must start with a command: {data}");
            else if (command is 'M') command = 'L';
            else if (command is 'm') command = 'l';
            // otherwise: implicit repeat of the previous command

            var relative = char.IsLower(command);
            var offset = relative ? current : PointF.Empty;

            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                {
                    var p = new PointF(reader.Number() + offset.X, reader.Number() + offset.Y);
                    if (open) path.StartFigure();
                    current = start = p;
                    open = true;
                    break;
                }
                case 'L':
                {
                    var p = new PointF(reader.Number() + offset.X, reader.Number() + offset.Y);
                    path.AddLine(current, p);
                    current = p;
                    break;
                }
                case 'H':
                {
                    var p = new PointF(reader.Number() + offset.X, current.Y);
                    path.AddLine(current, p);
                    current = p;
                    break;
                }
                case 'V':
                {
                    var p = new PointF(current.X, reader.Number() + offset.Y);
                    path.AddLine(current, p);
                    current = p;
                    break;
                }
                case 'C':
                {
                    var c1 = new PointF(reader.Number() + offset.X, reader.Number() + offset.Y);
                    var c2 = new PointF(reader.Number() + offset.X, reader.Number() + offset.Y);
                    var p = new PointF(reader.Number() + offset.X, reader.Number() + offset.Y);
                    path.AddBezier(current, c1, c2, p);
                    lastControl = c2;
                    current = p;
                    break;
                }
                case 'S':
                {
                    var c1 = previous is 'C' or 'S' ? Reflect(lastControl, current) : current;
                    var c2 = new PointF(reader.Number() + offset.X, reader.Number() + offset.Y);
                    var p = new PointF(reader.Number() + offset.X, reader.Number() + offset.Y);
                    path.AddBezier(current, c1, c2, p);
                    lastControl = c2;
                    current = p;
                    break;
                }
                case 'Q':
                {
                    var c = new PointF(reader.Number() + offset.X, reader.Number() + offset.Y);
                    var p = new PointF(reader.Number() + offset.X, reader.Number() + offset.Y);
                    AddQuadratic(path, current, c, p);
                    lastControl = c;
                    current = p;
                    break;
                }
                case 'T':
                {
                    var c = previous is 'Q' or 'T' ? Reflect(lastControl, current) : current;
                    var p = new PointF(reader.Number() + offset.X, reader.Number() + offset.Y);
                    AddQuadratic(path, current, c, p);
                    lastControl = c;
                    current = p;
                    break;
                }
                case 'A':
                {
                    var rx = reader.Number();
                    var ry = reader.Number();
                    var rotation = reader.Number();
                    var large = reader.Flag();
                    var sweep = reader.Flag();
                    var p = new PointF(reader.Number() + offset.X, reader.Number() + offset.Y);
                    AddArc(path, current, rx, ry, rotation, large, sweep, p);
                    current = p;
                    break;
                }
                case 'Z':
                    path.CloseFigure();
                    current = start;
                    open = false;
                    break;
                default:
                    throw new FormatException($"Unsupported path command '{command}' in: {data}");
            }
            previous = char.ToUpperInvariant(command);
            if (previous == 'Z') command = '\0';
        }
        return path;
    }

    private static PointF Reflect(PointF control, PointF about) => new(2 * about.X - control.X, 2 * about.Y - control.Y);

    private static void AddQuadratic(GraphicsPath path, PointF from, PointF control, PointF to)
    {
        // a quadratic curve as the equivalent cubic
        var c1 = new PointF(from.X + 2f / 3f * (control.X - from.X), from.Y + 2f / 3f * (control.Y - from.Y));
        var c2 = new PointF(to.X + 2f / 3f * (control.X - to.X), to.Y + 2f / 3f * (control.Y - to.Y));
        path.AddBezier(from, c1, c2, to);
    }

    /// <summary>SVG elliptical arc (endpoint form) → cubic Béziers of at most a quarter turn each.</summary>
    private static void AddArc(GraphicsPath path, PointF from, double rx, double ry, double rotationDegrees, bool large, bool sweep, PointF to)
    {
        if (from == to) return;
        rx = Math.Abs(rx);
        ry = Math.Abs(ry);
        if (rx < 1e-6 || ry < 1e-6) { path.AddLine(from, to); return; }

        var phi = rotationDegrees * Math.PI / 180;
        double cos = Math.Cos(phi), sin = Math.Sin(phi);
        double dx = (from.X - to.X) / 2.0, dy = (from.Y - to.Y) / 2.0;
        double x1 = cos * dx + sin * dy, y1 = -sin * dx + cos * dy;

        var lambda = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry);
        if (lambda > 1)
        {
            var scale = Math.Sqrt(lambda);
            rx *= scale;
            ry *= scale;
        }

        var numerator = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1;
        var denominator = rx * rx * y1 * y1 + ry * ry * x1 * x1;
        var coefficient = (large == sweep ? -1 : 1) * Math.Sqrt(Math.Max(0, numerator / denominator));
        double cxp = coefficient * (rx * y1 / ry), cyp = coefficient * -(ry * x1 / rx);
        double cx = cos * cxp - sin * cyp + (from.X + to.X) / 2.0, cy = sin * cxp + cos * cyp + (from.Y + to.Y) / 2.0;

        double ux = (x1 - cxp) / rx, uy = (y1 - cyp) / ry, vx = (-x1 - cxp) / rx, vy = (-y1 - cyp) / ry;
        var theta = AngleOf(1, 0, ux, uy);
        var delta = AngleOf(ux, uy, vx, vy);
        if (!sweep && delta > 0) delta -= 2 * Math.PI;
        else if (sweep && delta < 0) delta += 2 * Math.PI;

        var segments = Math.Max(1, (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2) - 1e-9));
        var step = delta / segments;
        var t = 4.0 / 3.0 * Math.Tan(step / 4);

        PointF Map(double x, double y) => new((float)(cx + rx * cos * x - ry * sin * y), (float)(cy + rx * sin * x + ry * cos * y));

        var prev = from;
        for (var i = 0; i < segments; i++)
        {
            double a1 = theta + i * step, a2 = a1 + step;
            double c1x = Math.Cos(a1) - t * Math.Sin(a1), c1y = Math.Sin(a1) + t * Math.Cos(a1);
            double c2x = Math.Cos(a2) + t * Math.Sin(a2), c2y = Math.Sin(a2) - t * Math.Cos(a2);
            var end = i == segments - 1 ? to : Map(Math.Cos(a2), Math.Sin(a2));
            path.AddBezier(prev, Map(c1x, c1y), Map(c2x, c2y), end);
            prev = end;
        }
    }

    private static double AngleOf(double ux, double uy, double vx, double vy)
    {
        var dot = ux * vx + uy * vy;
        var length = Math.Sqrt(ux * ux + uy * uy) * Math.Sqrt(vx * vx + vy * vy);
        var angle = Math.Acos(Math.Clamp(dot / length, -1, 1));
        return ux * vy - uy * vx < 0 ? -angle : angle;
    }

    private sealed class Reader(string text)
    {
        private int _position;

        public bool SkipSeparators()
        {
            while (_position < text.Length && (char.IsWhiteSpace(text[_position]) || text[_position] == ',')) _position++;
            return _position < text.Length;
        }

        public bool PeekIsCommand() => char.IsLetter(text[_position]) && text[_position] is not ('e' or 'E');

        public char ReadCommand() => text[_position++];

        /// <summary>Arc flags are single characters and may touch the next number ("a2 2 0 011-2 2").</summary>
        public bool Flag()
        {
            SkipSeparators();
            var c = text[_position++];
            if (c is not ('0' or '1')) throw new FormatException($"Bad arc flag '{c}' at {_position} in: {text}");
            return c == '1';
        }

        public float Number()
        {
            SkipSeparators();
            var start = _position;
            if (_position < text.Length && text[_position] is '+' or '-') _position++;
            var seenDot = false;
            while (_position < text.Length)
            {
                var c = text[_position];
                if (char.IsDigit(c)) _position++;
                else if (c == '.' && !seenDot) { seenDot = true; _position++; }
                else if ((c is 'e' or 'E') && _position + 1 < text.Length && (char.IsDigit(text[_position + 1]) || text[_position + 1] is '-' or '+')) _position += 2;
                else break;
            }
            if (start == _position) throw new FormatException($"Number expected at {start} in: {text}");
            return float.Parse(text.AsSpan(start, _position - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}
