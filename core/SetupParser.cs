// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — iRacing .sto Setup File Parser
// Reads setup values from iRacing's proprietary text format.
// Gracefully handles unknown values — stores raw if can't parse.
// ═══════════════════════════════════════════════════════════════

using ChiefLogger.Data;

namespace ChiefLogger.Core;

public class SetupParser
{
    // Known section headers in .sto files
    private static readonly string[] TIRE_SECTIONS = { "LeftFront", "RightFront", "LeftRear", "RightRear", "LF", "RF", "LR", "RR" };
    private static readonly string[] CHASSIS_SECTIONS = { "Chassis", "Front", "Rear", "LeftFront", "RightFront", "LeftRear", "RightRear" };

    // ═══ PARSE ═══

    public ParsedSetupValues Parse(string content)
    {
        var values = new ParsedSetupValues();
        values.RawSections = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(content))
            return values;

        try
        {
            var lines = content.Split('\n').Select(l => l.Trim()).ToArray();
            var currentSection = "";
            var currentSubSection = "";

            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line) || line.StartsWith("//"))
                    continue;

                // Section headers: [SectionName] or just SectionName:
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    currentSection = line[1..^1].Trim();
                    currentSubSection = "";
                    continue;
                }

                if (line.EndsWith(":") && !line.Contains("="))
                {
                    currentSubSection = line[..^1].Trim();
                    continue;
                }

                // Key=Value or Key: Value pairs
                var (key, val) = ParseKeyValue(line);
                if (key == null) continue;

                var fullKey = string.IsNullOrEmpty(currentSubSection)
                    ? $"{currentSection}.{key}"
                    : $"{currentSection}.{currentSubSection}.{key}";

                // Store raw
                values.RawSections[fullKey] = val;

                // Try to map to known fields
                MapValue(values, currentSection, currentSubSection, key, val);
            }
        }
        catch
        {
            // If parsing fails entirely, raw content is still stored
        }

        return values;
    }

    // ═══ KEY/VALUE PARSING ═══

    private static (string? key, string val) ParseKeyValue(string line)
    {
        // Format: "Key=Value" or "Key: Value"
        int eqIdx = line.IndexOf('=');
        int colonIdx = line.IndexOf(':');

        int splitIdx;
        if (eqIdx >= 0 && (colonIdx < 0 || eqIdx < colonIdx))
            splitIdx = eqIdx;
        else if (colonIdx >= 0)
            splitIdx = colonIdx;
        else
            return (null, "");

        var key = line[..splitIdx].Trim();
        var val = line[(splitIdx + 1)..].Trim().Trim('"');
        return (key, val);
    }

    // ═══ VALUE MAPPING ═══

    private void MapValue(ParsedSetupValues v, string section, string sub, string key, string val)
    {
        var lSection = section.ToLowerInvariant();
        var lSub = sub.ToLowerInvariant();
        var lKey = key.ToLowerInvariant();

        // ═══ TIRE PRESSURES ═══
        if (lKey.Contains("pressure") || lKey.Contains("coldpressure") || lKey.Contains("cold pressure"))
        {
            var psi = ParseFloat(val);
            if (psi.HasValue)
            {
                if (IsCorner(lSection, lSub, "lf", "leftfront", "left front"))
                    v.LFPressure = psi;
                else if (IsCorner(lSection, lSub, "rf", "rightfront", "right front"))
                    v.RFPressure = psi;
                else if (IsCorner(lSection, lSub, "lr", "leftrear", "left rear"))
                    v.LRPressure = psi;
                else if (IsCorner(lSection, lSub, "rr", "rightrear", "right rear"))
                    v.RRPressure = psi;
            }
        }

        // ═══ SPRINGS ═══
        if (lKey.Contains("spring") && !lKey.Contains("perch") && !lKey.Contains("rubber"))
        {
            if (IsCorner(lSection, lSub, "lf", "leftfront", "left front"))
                v.LFSpring = val;
            else if (IsCorner(lSection, lSub, "rf", "rightfront", "right front"))
                v.RFSpring = val;
            else if (IsCorner(lSection, lSub, "lr", "leftrear", "left rear"))
                v.LRSpring = val;
            else if (IsCorner(lSection, lSub, "rr", "rightrear", "right rear"))
                v.RRSpring = val;
        }

        // ═══ RIDE HEIGHT ═══
        if (lKey.Contains("rideheight") || lKey.Contains("ride height"))
        {
            if (lSection.Contains("front") || lSub.Contains("front"))
                v.FrontRideHeight = val;
            else if (lSection.Contains("rear") || lSub.Contains("rear"))
                v.RearRideHeight = val;
        }

        // ═══ ARB / ANTI-ROLL BAR / SWAY BAR ═══
        if (lKey.Contains("arb") || lKey.Contains("antiroll") || lKey.Contains("anti roll") ||
            lKey.Contains("swaybar") || lKey.Contains("sway bar"))
        {
            if (lSection.Contains("front") || lSub.Contains("front"))
                v.FrontARB = val;
            else if (lSection.Contains("rear") || lSub.Contains("rear"))
                v.RearARB = val;
        }

        // ═══ BRAKE BIAS ═══
        if (lKey.Contains("brakebias") || lKey.Contains("brake bias") || (lKey.Contains("bias") && lSection.Contains("brake")))
        {
            v.BrakeBias = ParseFloat(val);
        }

        // ═══ STEERING ═══
        if (lKey.Contains("steeringratio") || lKey.Contains("steering ratio"))
        {
            v.SteeringRatio = val;
        }

        // ═══ AERO / WINGS ═══
        if (lKey.Contains("wing") || lKey.Contains("spoiler") || lKey.Contains("gurney") || lKey.Contains("aero"))
        {
            if (lSection.Contains("front") || lSub.Contains("front") || lKey.Contains("front"))
                v.FrontWing = val;
            else if (lSection.Contains("rear") || lSub.Contains("rear") || lKey.Contains("rear") || lKey.Contains("spoiler"))
                v.RearWing = val;
        }
        if (lKey.Contains("spoiler"))
            v.RearSpoiler = val;

        // ═══ DAMPERS / SHOCKS ═══
        if (lKey.Contains("rebound") || lKey.Contains("reboundslow") || lKey.Contains("rebound slow"))
        {
            if (IsCorner(lSection, lSub, "lf", "leftfront", "left front")) v.LFRebound = val;
            else if (IsCorner(lSection, lSub, "rf", "rightfront", "right front")) v.RFRebound = val;
            else if (IsCorner(lSection, lSub, "lr", "leftrear", "left rear")) v.LRRebound = val;
            else if (IsCorner(lSection, lSub, "rr", "rightrear", "right rear")) v.RRRebound = val;
        }
        if (lKey.Contains("compression") || lKey.Contains("bump") || lKey.Contains("bumpslow"))
        {
            if (IsCorner(lSection, lSub, "lf", "leftfront", "left front")) v.LFCompression = val;
            else if (IsCorner(lSection, lSub, "rf", "rightfront", "right front")) v.RFCompression = val;
            else if (IsCorner(lSection, lSub, "lr", "leftrear", "left rear")) v.LRCompression = val;
            else if (IsCorner(lSection, lSub, "rr", "rightrear", "right rear")) v.RRCompression = val;
        }

        // ═══ DIFFERENTIAL ═══
        if (lKey.Contains("diffpreload") || lKey.Contains("diff preload") || lKey.Contains("preload"))
            v.DiffPreload = val;
        if (lKey.Contains("diffentry") || (lKey.Contains("entry") && lSection.Contains("diff")))
            v.DiffEntry = val;
        if (lKey.Contains("diffmiddle") || (lKey.Contains("middle") && lSection.Contains("diff")))
            v.DiffMiddle = val;
        if (lKey.Contains("diffexit") || (lKey.Contains("exit") && lSection.Contains("diff")))
            v.DiffExit = val;

        // ═══ GEARING ═══
        if (lKey.Contains("finaldrive") || lKey.Contains("final drive") || lKey.Contains("finalgear"))
            v.FinalDrive = val;
        if (lKey.Contains("gear") && !lKey.Contains("final"))
        {
            v.GearRatios ??= new List<string>();
            v.GearRatios.Add($"{key}: {val}");
        }

        // ═══ CROSS WEIGHT ═══
        if (lKey.Contains("crossweight") || lKey.Contains("cross weight") || lKey.Contains("wedge"))
            v.CrossWeight = val;

        // ═══ CAMBER / TOE ═══
        if (lKey.Contains("camber"))
        {
            if (IsCorner(lSection, lSub, "lf", "leftfront", "left front")) v.LFCamber = val;
            else if (IsCorner(lSection, lSub, "rf", "rightfront", "right front")) v.RFCamber = val;
            else if (IsCorner(lSection, lSub, "lr", "leftrear", "left rear")) v.LRCamber = val;
            else if (IsCorner(lSection, lSub, "rr", "rightrear", "right rear")) v.RRCamber = val;
        }
        if (lKey.Contains("toe"))
        {
            if (lSection.Contains("front") || lSub.Contains("front"))
                v.FrontToe = val;
            else if (lSection.Contains("rear") || lSub.Contains("rear"))
                v.RearToe = val;
        }
    }

    // ═══ CORNER MATCHING ═══

    private static bool IsCorner(string section, string sub, params string[] markers)
    {
        var combined = (section + " " + sub).ToLowerInvariant();
        return markers.Any(m => combined.Contains(m));
    }

    private static float? ParseFloat(string val)
    {
        // Strip units: "26.0 psi" → "26.0"
        var numStr = new string(val.TakeWhile(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
        return float.TryParse(numStr, out float f) ? f : null;
    }
}
