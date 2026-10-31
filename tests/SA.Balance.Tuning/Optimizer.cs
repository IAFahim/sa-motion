using SA.Balance.Evaluation;

namespace SA.Balance.Tuning;

struct TuneResult
{
    public double[] Genes;
    public float Fast;
    public int Evals;
}

static class Optimizer
{
    public static float Objective(BalanceParams p)
    {
        return Evaluator.RunFast(() => new TunedController(p));
    }

    public static TuneResult Evolve(double[] seed, int generations, int lambda, ulong rngSeed, double sigma0, double sigma1, Action<string> log, Func<int, double[], float, bool>? observe)
    {
        var rng = new XorShift(rngSeed);
        var parent = (double[])seed.Clone();
        var parentScore = Objective(ParamSpace.Decode(parent));
        var evals = 1;
        log($"gen -1 best {parentScore:F3} evals {evals}");
        for (var g = 0; g < generations; g++)
        {
            var t = generations <= 1 ? 1.0 : (double)g / (generations - 1);
            var sigma = sigma0 + (sigma1 - sigma0) * t;
            var best = parent;
            var bestScore = parentScore;
            for (var k = 0; k < lambda; k++)
            {
                var child = (double[])parent.Clone();
                for (var i = 0; i < ParamSpace.Count; i++)
                {
                    child[i] += sigma * rng.NextGaussian();
                    if (child[i] < 0) child[i] = 0;
                    if (child[i] > 1) child[i] = 1;
                }
                var score = Objective(ParamSpace.Decode(child));
                evals++;
                if (score > bestScore)
                {
                    best = child;
                    bestScore = score;
                }
            }
            parent = best;
            parentScore = bestScore;
            log($"gen {g} best {parentScore:F3} sigma {sigma:F4} evals {evals}");
            if (observe != null && !observe(g, (double[])parent.Clone(), parentScore))
                break;
        }
        return new TuneResult { Genes = parent, Fast = parentScore, Evals = evals };
    }

    public static TuneResult Refine(double[] seed, float seedScore, double step, int sweeps, Action<string> log)
    {
        var best = (double[])seed.Clone();
        var bestScore = seedScore;
        var evals = 0;
        for (var s = 0; s < sweeps; s++)
        {
            var improved = false;
            for (var i = 0; i < ParamSpace.Count; i++)
            {
                foreach (var sign in new[] { 1.0, -1.0 })
                {
                    var trial = (double[])best.Clone();
                    trial[i] += sign * step;
                    if (trial[i] < 0 || trial[i] > 1)
                        continue;
                    var score = Objective(ParamSpace.Decode(trial));
                    evals++;
                    if (score > bestScore)
                    {
                        best = trial;
                        bestScore = score;
                        improved = true;
                    }
                }
            }
            log($"refine sweep {s} best {bestScore:F3} evals {evals}");
            if (!improved)
                break;
        }
        return new TuneResult { Genes = best, Fast = bestScore, Evals = evals };
    }

    public struct XorShift
    {
        ulong state;

        public XorShift(ulong seed)
        {
            state = seed != 0 ? seed : 0x9E3779B97F4A7C15UL;
        }

        ulong Next()
        {
            var x = state;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            state = x;
            return x * 0x2545F4914F6CDD1DUL;
        }

        public double NextDouble()
        {
            return (Next() >> 11) * (1.0 / 9007199254740992.0);
        }

        public double NextGaussian()
        {
            var u1 = NextDouble();
            var u2 = NextDouble();
            if (u1 <= 0) u1 = double.Epsilon;
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }
}
