using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SA.Motion.Legs;
using SA.Motion.Numerics;

#if SA_LIBRARY_NETSTANDARD
const string LibraryBuild = "netstandard2.1";
#else
const string LibraryBuild = "net10.0";
#endif

const int Characters = 10_000;
const int LegsPerCharacter = 2;
const int SettleRounds = 10;
const int Rounds = 40;
const double JitWarmupSeconds = 4.0;

var parameters = LegParams.Standard;

var jitWarmup = new Population(Characters, LegsPerCharacter);
var warmupClock = Stopwatch.StartNew();
for (var frame = 0; warmupClock.Elapsed.TotalSeconds < JitWarmupSeconds; frame++)
{
    jitWarmup.Drive(frame);
    jitWarmup.Solve(parameters);
}

var population = new Population(Characters, LegsPerCharacter);
var nanosecondsPerLeg = new double[Rounds];
var checksum = 0.0;
for (var round = 0; round < SettleRounds + Rounds; round++)
{
    population.Drive(round);
    var ticks = population.Solve(parameters);
    checksum += population.Digest();
    if (round >= SettleRounds)
        nanosecondsPerLeg[round - SettleRounds] = ticks * 1e9 / ((double)Stopwatch.Frequency * population.Legs);
}
Array.Sort(nanosecondsPerLeg);
Console.WriteLine($"runtime: {RuntimeInformation.FrameworkDescription}, library build: {LibraryBuild}");
Console.WriteLine($"leg solver: {Characters} characters x {LegsPerCharacter} legs, LegParams.Standard, flat ground, {Rounds} rounds after {SettleRounds} settling rounds and {JitWarmupSeconds:F0} s of JIT warmup");
Console.WriteLine($"best round:   {nanosecondsPerLeg[0]:F1} ns per leg");
Console.WriteLine($"median round: {(nanosecondsPerLeg[Rounds / 2 - 1] + nanosecondsPerLeg[Rounds / 2]) * 0.5:F1} ns per leg");
Console.WriteLine($"planted legs in the last round: {population.PlantedFraction():P1}, plants or releases: {population.ChangedFraction():P1}");
Console.WriteLine($"checksum: {checksum:F4}");

sealed class Population
{
    const float Frame = 1f / 60f;
    const double CyclesPerSecond = 1.1;
    const double CharacterPhaseStep = 0.6180339887;
    const float StrideLength = 0.2f;
    const float SwingLift = 0.1f;
    const float HipHeight = 0.95f;
    const float AnkleHeight = 0.09f;
    const float BoneLength = 0.4464f;

    static readonly Float3 Up = new Float3(0f, 1f, 0f);
    static readonly Float3 Forward = new Float3(0f, 0f, 1f);
    static readonly Float3 Side = new Float3(1f, 0f, 0f);

    readonly int characters;
    readonly int legsPerCharacter;
    readonly LegRigState[] rigs;
    readonly LegState[] states;
    readonly LegInput[] inputs;
    readonly FootShape[] shapes;
    readonly LegPose[] poses;

    public Population(int characters, int legsPerCharacter)
    {
        this.characters = characters;
        this.legsPerCharacter = legsPerCharacter;
        Legs = characters * legsPerCharacter;
        rigs = new LegRigState[characters];
        states = new LegState[Legs];
        inputs = new LegInput[Legs];
        shapes = new FootShape[Legs];
        poses = new LegPose[Legs];
        for (var i = 0; i < Legs; i++)
            shapes[i] = new FootShape { Heel = new Float3(0f, -AnkleHeight, -0.05f), Ball = new Float3(0f, -AnkleHeight, 0.14f) };
    }

    public int Legs { get; }

    public void Drive(int frame)
    {
        for (var character = 0; character < characters; character++)
            for (var leg = 0; leg < legsPerCharacter; leg++)
                inputs[character * legsPerCharacter + leg] = Stride(frame, character, leg);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public long Solve(in LegParams parameters)
    {
        var start = Stopwatch.GetTimestamp();
        for (var character = 0; character < characters; character++)
        {
            var first = character * legsPerCharacter;
            LegRig.Step(
                parameters,
                1f,
                1f,
                ref rigs[character],
                states.AsSpan(first, legsPerCharacter),
                inputs.AsSpan(first, legsPerCharacter),
                shapes.AsSpan(first, legsPerCharacter),
                Float3.Zero,
                Up,
                Forward,
                Frame,
                poses.AsSpan(first, legsPerCharacter));
        }
        return Stopwatch.GetTimestamp() - start;
    }

    public double Digest()
    {
        var sum = 0.0;
        for (var i = 0; i < Legs; i++)
        {
            var pose = poses[i];
            sum += pose.Ankle.X + pose.Ankle.Y + pose.Ankle.Z + pose.Contact.X + pose.Contact.Y + pose.Contact.Z + pose.Stretch;
            sum += pose.Foot.X + pose.Foot.Y + pose.Foot.Z + pose.Foot.W + pose.Upper.X + pose.Upper.W + pose.Lower.X + pose.Lower.W;
        }
        return sum;
    }

    public double PlantedFraction()
    {
        var planted = 0;
        for (var i = 0; i < Legs; i++)
            planted += states[i].Planted;
        return (double)planted / Legs;
    }

    public double ChangedFraction()
    {
        var changed = 0;
        for (var i = 0; i < Legs; i++)
            changed += states[i].Changed;
        return (double)changed / Legs;
    }

    static LegInput Stride(int frame, int character, int leg)
    {
        var cycle = frame * CyclesPerSecond / 60.0 + character * CharacterPhaseStep + leg * 0.5;
        var phase = (float)((cycle - Math.Floor(cycle)) * 2.0 * Math.PI);
        var swing = MathF.Max(0f, MathF.Cos(phase));
        var side = leg == 0 ? -0.1f : 0.1f;
        var hip = new Float3(side, HipHeight, 0f);
        var ankle = new Float3(side, AnkleHeight + SwingLift * swing, StrideLength * MathF.Sin(phase));
        var toAnkle = ankle - hip;
        var distance = toAnkle.Length;
        var direction = toAnkle / distance;
        var bend = Forward - direction * Float3.Dot(Forward, direction);
        bend /= bend.Length;
        var along = distance * 0.5f;
        var knee = hip + direction * along + bend * MathF.Sqrt(BoneLength * BoneLength - along * along);
        var pitch = 0.35f * MathF.Sin(phase);
        var yaw = 0.15f * MathF.Sin(phase * 0.5f);
        return new LegInput
        {
            Hip = hip,
            Knee = knee,
            Ankle = ankle,
            Upper = Quaternion.AxisAngle(Side, 0.4f * MathF.Sin(phase)),
            Lower = Quaternion.AxisAngle(Side, -0.6f * MathF.Cos(phase)),
            Foot = Quaternion.Multiply(Quaternion.AxisAngle(Up, yaw), Quaternion.AxisAngle(Side, -pitch)),
            Ground = new GroundHit { Hit = 1, Point = new Float3(ankle.X, 0f, ankle.Z), Normal = Up }
        };
    }
}
