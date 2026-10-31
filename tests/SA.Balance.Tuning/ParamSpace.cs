namespace SA.Balance.Tuning;

static class ParamSpace
{
    public const int Count = 18;

    public static string Name(int index)
    {
        switch (index)
        {
            case 0: return "UprightGain";
            case 1: return "UprightDamp";
            case 2: return "UprightMax";
            case 3: return "AnkleGain";
            case 4: return "HipGain";
            case 5: return "HipVelGain";
            case 6: return "HipRollGain";
            case 7: return "HipRollVelGain";
            case 8: return "LeanMax";
            case 9: return "StepMargin";
            case 10: return "StepTime";
            case 11: return "StepLead";
            case 12: return "MaxReach";
            case 13: return "FallReach";
            case 14: return "FallSpeed";
            case 15: return "SupportRadius";
            case 16: return "StanceBoost";
            case 17: return "StepHeight";
            default: return "Unknown";
        }
    }

    public static float Min(int index)
    {
        switch (index)
        {
            case 0: return 0.05f;
            case 1: return 0f;
            case 2: return 0.1f;
            case 3: return 0.5f;
            case 4: return 0f;
            case 5: return 0f;
            case 6: return 0.2f;
            case 7: return 0f;
            case 8: return 0.1f;
            case 9: return 0.02f;
            case 10: return 0.12f;
            case 11: return 0f;
            case 12: return 0.4f;
            case 13: return 0.8f;
            case 14: return 1.5f;
            case 15: return 0.05f;
            case 16: return 1f;
            case 17: return 0.05f;
            default: return 0f;
        }
    }

    public static float Max(int index)
    {
        switch (index)
        {
            case 0: return 1f;
            case 1: return 0.5f;
            case 2: return 0.6f;
            case 3: return 5f;
            case 4: return 1f;
            case 5: return 0.3f;
            case 6: return 2.5f;
            case 7: return 0.6f;
            case 8: return 0.6f;
            case 9: return 0.25f;
            case 10: return 0.35f;
            case 11: return 1f;
            case 12: return 0.9f;
            case 13: return 2.5f;
            case 14: return 4.5f;
            case 15: return 0.35f;
            case 16: return 2.5f;
            case 17: return 0.2f;
            default: return 1f;
        }
    }

    public static float Get(in BalanceParams p, int index)
    {
        switch (index)
        {
            case 0: return p.UprightGain;
            case 1: return p.UprightDamp;
            case 2: return p.UprightMax;
            case 3: return p.AnkleGain;
            case 4: return p.HipGain;
            case 5: return p.HipVelGain;
            case 6: return p.HipRollGain;
            case 7: return p.HipRollVelGain;
            case 8: return p.LeanMax;
            case 9: return p.StepMargin;
            case 10: return p.StepTime;
            case 11: return p.StepLead;
            case 12: return p.MaxReach;
            case 13: return p.FallReach;
            case 14: return p.FallSpeed;
            case 15: return p.SupportRadius;
            case 16: return p.StanceBoost;
            case 17: return p.StepHeight;
            default: return 0f;
        }
    }

    public static BalanceParams Set(in BalanceParams p, int index, float value)
    {
        var q = p;
        switch (index)
        {
            case 0: q.UprightGain = value; break;
            case 1: q.UprightDamp = value; break;
            case 2: q.UprightMax = value; break;
            case 3: q.AnkleGain = value; break;
            case 4: q.HipGain = value; break;
            case 5: q.HipVelGain = value; break;
            case 6: q.HipRollGain = value; break;
            case 7: q.HipRollVelGain = value; break;
            case 8: q.LeanMax = value; break;
            case 9: q.StepMargin = value; break;
            case 10: q.StepTime = value; break;
            case 11: q.StepLead = value; break;
            case 12: q.MaxReach = value; break;
            case 13: q.FallReach = value; break;
            case 14: q.FallSpeed = value; break;
            case 15: q.SupportRadius = value; break;
            case 16: q.StanceBoost = value; break;
            case 17: q.StepHeight = value; break;
        }
        return q;
    }

    public static double[] Encode(in BalanceParams p)
    {
        var genes = new double[Count];
        for (var i = 0; i < Count; i++)
        {
            var span = Max(i) - Min(i);
            var t = span <= 0f ? 0.5 : (Get(in p, i) - Min(i)) / span;
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            genes[i] = t;
        }
        return genes;
    }

    public static BalanceParams Decode(double[] genes)
    {
        var p = BalanceParams.Standard;
        for (var i = 0; i < Count; i++)
        {
            var t = genes[i];
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            p = Set(in p, i, Min(i) + (float)t * (Max(i) - Min(i)));
        }
        return p;
    }
}
