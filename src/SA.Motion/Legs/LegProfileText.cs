using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SA.Motion.Numerics;

namespace SA.Motion.Legs
{
    public static class LegProfileText
    {
        public static string Write(in LegParams p)
        {
            var text = new StringBuilder();
            for (var i = 0; i < LegParamsSpec.Count; i++)
            {
                var field = (LegParam)i;
                text.Append(LegParamsSpec.Text(field).Name);
                text.Append(" = ");
                text.Append(Invariant(LegParamsSpec.Get(p, field)));
                text.Append('\n');
            }
            return text.ToString();
        }

        public static LegParams Read(string? text, in LegParams fallback, List<string>? problems)
        {
            if (text == null)
            {
                Report(problems, "text is null");
                return fallback;
            }
            var result = fallback;
            var applied = new bool[LegParamsSpec.Count];
            var lineNumber = 0;
            var start = 0;
            while (start < text.Length)
            {
                var end = start;
                while (end < text.Length && text[end] != '\n' && text[end] != '\r')
                    end++;
                lineNumber++;
                var complaint = ApplyLine(text.Substring(start, end - start), applied, ref result);
                if (complaint != null)
                    Report(problems, "line " + lineNumber.ToString(CultureInfo.InvariantCulture) + ": " + complaint);
                var windowsBreak = end + 1 < text.Length && text[end] == '\r' && text[end + 1] == '\n';
                start = end + (windowsBreak ? 2 : 1);
            }
            return result;
        }

        static string? ApplyLine(string line, bool[] applied, ref LegParams result)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                return null;
            var equals = trimmed.IndexOf('=');
            if (equals <= 0)
                return "expected Name = value";
            var name = trimmed.Substring(0, equals).Trim();
            var index = FieldIndex(name);
            if (index < 0)
                return "unknown name '" + name + "'";
            if (applied[index])
                return "duplicate of an earlier " + name + " line";
            var field = (LegParam)index;
            var value = trimmed.Substring(equals + 1).Trim();
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                return name + " is not a number: '" + value + "'";
            if (!Scalar.IsFinite(number))
                return name + " is not finite: '" + value + "'";
            var bounds = LegParamsSpec.Bounds(field);
            if (number < bounds.Minimum || number > bounds.Maximum)
                return name + " is outside " + Invariant(bounds.Minimum) + " to " + Invariant(bounds.Maximum) + ": '" + value + "'";
            applied[index] = true;
            result = LegParamsSpec.With(result, field, number);
            return null;
        }

        static int FieldIndex(string name)
        {
            for (var i = 0; i < LegParamsSpec.Count; i++)
                if (LegParamsSpec.Text((LegParam)i).Name == name)
                    return i;
            return -1;
        }

        static string Invariant(float value) => value.ToString("R", CultureInfo.InvariantCulture);

        static void Report(List<string>? problems, string message)
        {
            if (problems != null)
                problems.Add(message);
        }
    }
}
