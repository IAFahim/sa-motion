using System;
using SA.Humanoid;
using SA.Humanoid.Profile;
using SA.Motion.Mechanics;
using SA.Motion.Numerics;
using SA.ReactionControl;

namespace SA.Harness
{
    public sealed class HarnessWorld
    {
        public const int BodyCount = 15;
        public const int SolverIterations = 32;
        public const float Gravity = 9.81f;
        public const float GroundFriction = 0.8f;
        public const float ContactSlop = 0.001f;
        public const float JointBeta = 0f;
        public const float LimitBeta = 0.05f;
        public const float LimitSlop = 0f;
        public const float JointFriction = 0.05f;
        public const float LinearDamping = 0.2f;
        public const float AngularDamping = 2f;
        public const float MaxStep = 0.03333334f;
        public const float ContactBeta = 0f;
        public const float SubstepRate = 480f;
        public const int MaxSubsteps = 32;
        public const float MaxSpeed = 8f;
        public const float MaxAngularSpeed = 30f;
        public const float DefaultMass = 70f;

        readonly RoleBody[] _bodies = new RoleBody[BodyCount];
        readonly Float3[] _principal = new Float3[BodyCount];
        readonly Float3[] _childAnchor = new Float3[BodyCount];
        readonly Float3[] _parentAnchor = new Float3[BodyCount];
        readonly Quaternion[] _bind = new Quaternion[BodyCount];
        readonly JointDrive[] _drives = new JointDrive[BodyCount];
        readonly float[] _contactAccumulator = new float[BodyCount * 3];

        HarnessWorld()
        {
        }

        public ReadOnlySpan<RoleBody> Bodies
        {
            get { return _bodies; }
        }

        public static HarnessWorld Create(float totalMass = DefaultMass)
        {
            if (!Scalar.IsFinite(totalMass) || totalMass <= 0f)
            {
                totalMass = DefaultMass;
            }
            var world = new HarnessWorld();
            for (int i = 0; i < BodyCount; i++)
            {
                var role = (Role)i;
                float mass = HumanProfile.Mass(role, totalMass);
                if (!Scalar.IsFinite(mass) || mass <= 0f)
                {
                    mass = totalMass / BodyCount;
                }
                Float3 center = HarnessTables.Center(role);
                Quaternion rotation = HarnessTables.BaseOrientation(role);
                Float3 principal = HarnessTables.PrincipalInertia(mass, role);
                world._principal[i] = principal;
                var body = new RoleBody
                {
                    Position = center,
                    Rotation = rotation,
                    LinearVelocity = Float3.Zero,
                    AngularVelocity = Float3.Zero,
                    InverseMass = 1f / mass,
                    InverseInertiaWorld = Inertia.InverseToWorld(principal, rotation),
                };
                world._bodies[i] = body;
                world._drives[i] = JointDrive.Passive;
                if (!Roles.IsRoot(role))
                {
                    var parent = Roles.Parent(role);
                    Float3 point = HarnessTables.JointPoint(role);
                    world._childAnchor[i] = ToLocal(point - center, rotation);
                    Float3 parentCenter = HarnessTables.Center(parent);
                    Quaternion parentRotation = HarnessTables.BaseOrientation(parent);
                    world._parentAnchor[i] = ToLocal(point - parentCenter, parentRotation);
                    Quaternion relative = Quaternion.Multiply(
                        Quaternion.InverseUnit(parentRotation), rotation);
                    world._bind[i] = Quaternion.Normalize(relative);
                }
                else
                {
                    world._childAnchor[i] = Float3.Zero;
                    world._parentAnchor[i] = Float3.Zero;
                    world._bind[i] = Quaternion.Identity;
                }
            }
            return world;
        }

        public void Step(float dt)
        {
            if (!Scalar.IsFinite(dt) || dt <= 0f)
            {
                return;
            }
            if (dt > MaxStep)
            {
                dt = MaxStep;
            }
            int divisions = (int)(dt * SubstepRate + 0.5f);
            if (divisions < 1)
            {
                divisions = 1;
            }
            if (divisions > MaxSubsteps)
            {
                divisions = MaxSubsteps;
            }
            float h = dt / divisions;
            for (int substep = 0; substep < divisions; substep++)
            {
                Array.Clear(_contactAccumulator, 0, _contactAccumulator.Length);
                IntegrateForces(h);
                for (int iteration = 0; iteration < SolverIterations; iteration++)
                {
                    SolveJoints(h);
                    SolveLimits(h);
                    SolveContacts(h);
                }
                ProjectContacts();
                ProjectAnchors();
                ProjectLimits();
                IntegratePositions(h);
                RefreshInertias();
            }
        }

        void ProjectContacts()
        {
            for (int i = 0; i < BodyCount; i++)
            {
                RoleBody body = _bodies[i];
                Float3 axis = Quaternion.Rotate(body.Rotation, new Float3(0f, 1f, 0f));
                float half = HarnessTables.HalfLength((Role)i);
                float radius = HarnessTables.Radius((Role)i);
                float lift = 0f;
                for (int s = -1; s <= 1; s++)
                {
                    Float3 sample = body.Position + axis * (half * s);
                    float penetration = radius - sample.Y;
                    if (penetration > ContactSlop && penetration - ContactSlop > lift)
                    {
                        lift = penetration - ContactSlop;
                    }
                }
                if (lift > 0f)
                {
                    body.Position = body.Position + new Float3(0f, lift * 0.35f, 0f);
                    _bodies[i] = body;
                }
            }
        }

        public void ApplyDrives(ReadOnlySpan<JointDrive> drives)
        {
            int count = drives.Length < BodyCount ? drives.Length : BodyCount;
            for (int i = 0; i < count; i++)
            {
                _drives[i] = drives[i];
            }
        }

        public void ApplyImpulse(Role role, Float3 impulse, Float3 worldPoint)
        {
            if (!Roles.IsValid(role) || !impulse.IsFinite || !worldPoint.IsFinite)
            {
                return;
            }
            int index = (int)role;
            RoleBody body = _bodies[index];
            if (!Scalar.IsFinite(body.InverseMass) || body.InverseMass <= 0f)
            {
                return;
            }
            body.LinearVelocity = body.LinearVelocity + impulse * body.InverseMass;
            Float3 arm = worldPoint - body.Position;
            if (arm.IsFinite)
            {
                Float3 torque = Float3.Cross(arm, impulse);
                body.AngularVelocity = body.AngularVelocity + body.InverseInertiaWorld.Multiply(torque);
            }
            _bodies[index] = SanitizeBody(body, index);
        }

        public void TranslateAll(Float3 offset)
        {
            if (!offset.IsFinite)
            {
                return;
            }
            for (int i = 0; i < BodyCount; i++)
            {
                RoleBody body = _bodies[i];
                body.Position = body.Position + offset;
                _bodies[i] = body;
            }
        }

        public SupportProbe ProbeGround(Role foot)
        {
            var empty = new SupportProbe { Normal = new Float3(0f, 1f, 0f) };
            if (!Roles.IsValid(foot))
            {
                return empty;
            }
            int index = (int)foot;
            RoleBody body = _bodies[index];
            Float3 axis = Quaternion.Rotate(body.Rotation, new Float3(0f, 1f, 0f));
            float half = BodyHalfLength(foot);
            float radius = BodyRadius(foot);
            Float3 low = body.Position;
            float lowY = low.Y;
            for (int s = -1; s <= 1; s++)
            {
                Float3 sample = body.Position + axis * (half * s);
                if (sample.Y < lowY)
                {
                    lowY = sample.Y;
                    low = sample;
                }
            }
            float distance = lowY - radius;
            var probe = new SupportProbe
            {
                Hit = distance <= 0.06f ? (byte)1 : (byte)0,
                Distance = distance,
                Point = new Float3(low.X, 0f, low.Z),
                Normal = new Float3(0f, 1f, 0f),
                PointVelocity = PointVelocity(body, low),
            };
            return probe;
        }

        public float TotalEnergy()
        {
            float energy = 0f;
            for (int i = 0; i < BodyCount; i++)
            {
                RoleBody body = _bodies[i];
                float mass = body.InverseMass > 0f ? 1f / body.InverseMass : 0f;
                Float3 inertia = Inertia.ToWorld(_principal[i], body.Rotation).Multiply(body.AngularVelocity);
                energy += 0.5f * mass * Float3.Dot(body.LinearVelocity, body.LinearVelocity);
                energy += 0.5f * Float3.Dot(body.AngularVelocity, inertia);
                energy += mass * Gravity * body.Position.Y;
            }
            return energy;
        }

        public float JointAnchorSeparation(Role role)
        {
            if (!Roles.IsValid(role) || Roles.IsRoot(role))
            {
                return 0f;
            }
            int child = (int)role;
            int parent = (int)Roles.Parent(role);
            Float3 worldParent = AnchorWorld(_bodies[parent], _parentAnchor[child]);
            Float3 worldChild = AnchorWorld(_bodies[child], _childAnchor[child]);
            return (worldChild - worldParent).Length;
        }

        public float JointLimitViolation(Role role)
        {
            if (!Roles.IsValid(role) || Roles.IsRoot(role))
            {
                return 0f;
            }
            int child = (int)role;
            int parent = (int)Roles.Parent(role);
            Quaternion excursion = Excursion(
                _bodies[parent].Rotation, _bodies[child].Rotation, _bind[child]);
            return Violation(role, HumanProfile.Of(role).Limit, excursion);
        }

        public float BodyRadius(Role role)
        {
            return Roles.IsValid(role) ? HarnessTables.Radius(role) : 0f;
        }

        public float BodyHalfLength(Role role)
        {
            return Roles.IsValid(role) ? HarnessTables.HalfLength(role) : 0f;
        }

        public Float3 BodyPrincipalInertia(Role role)
        {
            return Roles.IsValid(role) ? _principal[(int)role] : Float3.Zero;
        }

        void IntegrateForces(float dt)
        {
            for (int i = 0; i < BodyCount; i++)
            {
                RoleBody body = _bodies[i];
                body.LinearVelocity = body.LinearVelocity + new Float3(0f, -Gravity * dt, 0f);
                body.AngularVelocity = Damp(body.AngularVelocity, AngularDamping, dt);
                body.LinearVelocity = Damp(body.LinearVelocity, LinearDamping, dt);
                _bodies[i] = body;
            }
            for (int i = 0; i < BodyCount; i++)
            {
                var role = (Role)i;
                JointDrive drive = _drives[i];
                if (!(drive.MaxTorque > 0f) || !Scalar.IsFinite(drive.MaxTorque))
                {
                    continue;
                }
                var stiff = new DriveGains { Stiffness = drive.Stiffness, Damping = 0f, MaxTorque = float.MaxValue };
                var loose = new DriveGains { Stiffness = 0f, Damping = drive.Damping, MaxTorque = float.MaxValue };
                if (Roles.IsRoot(role))
                {
                    Float3 push = QuaternionDrive.Torque(
                        _bodies[i].Rotation, drive.Target,
                        Float3.Zero, Float3.Zero, stiff);
                    Float3 drag = QuaternionDrive.Torque(
                        _bodies[i].Rotation, _bodies[i].Rotation,
                        _bodies[i].AngularVelocity, drive.TargetAngularVelocity, loose);
                    float scale = ClampScale(push + drag, drive.MaxTorque);
                    float factor = DampFactor(_bodies[i], drive.Damping, dt);
                    Float3 kick = push * (scale * dt) + drag * (scale * dt * factor);
                    RoleBody root = _bodies[i];
                    root.AngularVelocity = root.AngularVelocity
                        + root.InverseInertiaWorld.Multiply(kick);
                    _bodies[i] = root;
                    continue;
                }
                int parent = (int)Roles.Parent(role);
                Float3 relativeSpin = _bodies[i].AngularVelocity - _bodies[parent].AngularVelocity;
                Float3 hold = QuaternionDrive.Torque(
                    _bodies[i].Rotation, drive.Target,
                    Float3.Zero, Float3.Zero, stiff);
                Float3 calm = QuaternionDrive.Torque(
                    _bodies[i].Rotation, _bodies[i].Rotation,
                    relativeSpin, drive.TargetAngularVelocity, loose);
                float share = ClampScale(hold + calm, drive.MaxTorque);
                float blend = DampFactorPair(_bodies[i], _bodies[parent], drive.Damping, dt);
                Float3 impulse = hold * (share * dt) + calm * (share * dt * blend);
                RoleBody child = _bodies[i];
                RoleBody holder = _bodies[parent];
                child.AngularVelocity = child.AngularVelocity
                    + child.InverseInertiaWorld.Multiply(impulse);
                holder.AngularVelocity = holder.AngularVelocity
                    - holder.InverseInertiaWorld.Multiply(impulse);
                _bodies[i] = child;
                _bodies[parent] = holder;
            }
            for (int i = 0; i < BodyCount; i++)
            {
                _bodies[i] = ClampSpeeds(_bodies[i]);
            }
        }

        void SolveJoints(float dt)
        {
            for (int i = 0; i < BodyCount; i++)
            {
                var role = (Role)i;
                if (Roles.IsRoot(role))
                {
                    continue;
                }
                int parent = (int)Roles.Parent(role);
                RoleBody child = _bodies[i];
                RoleBody holder = _bodies[parent];
                Float3 armChild = Quaternion.Rotate(child.Rotation, _childAnchor[i]);
                Float3 armParent = Quaternion.Rotate(holder.Rotation, _parentAnchor[i]);
                Float3 anchorChild = child.Position + armChild;
                Float3 anchorParent = holder.Position + armParent;
                Float3 error = anchorChild - anchorParent;
                Float3 bias = error * (JointBeta / dt);
                float biasLength = bias.Length;
                float maxBias = 2f;
                if (biasLength > maxBias)
                {
                    bias = bias * (maxBias / biasLength);
                }
                Float3 pointChild = child.LinearVelocity + Float3.Cross(child.AngularVelocity, armChild);
                Float3 pointParent = holder.LinearVelocity + Float3.Cross(holder.AngularVelocity, armParent);
                Float3 delta = pointChild - pointParent + bias;
                Float3x3 stiffness = JointStiffness(child, holder, armChild, armParent);
                Float3 impulse = Solve(stiffness, Float3.Zero - delta);
                child.LinearVelocity = child.LinearVelocity + impulse * child.InverseMass;
                holder.LinearVelocity = holder.LinearVelocity - impulse * holder.InverseMass;
                child.AngularVelocity = child.AngularVelocity
                    + child.InverseInertiaWorld.Multiply(Float3.Cross(armChild, impulse));
                holder.AngularVelocity = holder.AngularVelocity
                    - holder.InverseInertiaWorld.Multiply(Float3.Cross(armParent, impulse));
                Float3 spin = child.AngularVelocity - holder.AngularVelocity;
                float spinLength = spin.Length;
                if (spinLength > 1e-9f)
                {
                    Float3 spinAxis = spin / spinLength;
                    float response = Float3.Dot(spinAxis, child.InverseInertiaWorld.Multiply(spinAxis))
                        + Float3.Dot(spinAxis, holder.InverseInertiaWorld.Multiply(spinAxis));
                    if (response > 1e-12f)
                    {
                        Float3 grip = spinAxis * (-JointFriction * spinLength / response);
                        child.AngularVelocity = child.AngularVelocity + child.InverseInertiaWorld.Multiply(grip);
                        holder.AngularVelocity = holder.AngularVelocity - holder.InverseInertiaWorld.Multiply(grip);
                    }
                }
                _bodies[i] = child;
                _bodies[parent] = holder;
            }
        }

        void ProjectAnchors()
        {
            for (int i = 0; i < BodyCount; i++)
            {
                var role = (Role)i;
                if (Roles.IsRoot(role))
                {
                    continue;
                }
                int parent = (int)Roles.Parent(role);
                RoleBody child = _bodies[i];
                RoleBody holder = _bodies[parent];
                float total = child.InverseMass + holder.InverseMass;
                if (!(total > 1e-12f))
                {
                    continue;
                }
                Float3 worldParent = AnchorWorld(holder, _parentAnchor[i]);
                Float3 worldChild = AnchorWorld(child, _childAnchor[i]);
                Float3 error = (worldChild - worldParent) * 0.5f;
                child.Position = child.Position - error * (child.InverseMass / total);
                holder.Position = holder.Position + error * (holder.InverseMass / total);
                _bodies[i] = child;
                _bodies[parent] = holder;
            }
        }

        void SolveLimits(float dt)
        {
            for (int i = 0; i < BodyCount; i++)
            {
                var role = (Role)i;
                if (Roles.IsRoot(role))
                {
                    continue;
                }
                int parent = (int)Roles.Parent(role);
                RoleBody child = _bodies[i];
                RoleBody holder = _bodies[parent];
                Quaternion excursion = Excursion(holder.Rotation, child.Rotation, _bind[i]);
                JointLimit limit = HumanProfile.Of(role).Limit;
                Float3 vector = RotationVector(excursion);
                float twist = Float3.Dot(vector, TwistLocal(role));
                float swingA = Float3.Dot(vector, SwingALocal(role));
                float swingB = Float3.Dot(vector, SwingBLocal(role));
                Float3 twistWorld = Quaternion.Rotate(holder.Rotation, TwistLocal(role));
                Float3 swingAWorld = Quaternion.Rotate(holder.Rotation, SwingALocal(role));
                Float3 swingBWorld = Quaternion.Rotate(holder.Rotation, SwingBLocal(role));
                float corrected = 0f;
                corrected += LimitAxis(ref child, ref holder, twistWorld, twist - limit.TwistMax, dt);
                corrected += LimitAxis(ref child, ref holder, Float3.Zero - twistWorld, limit.TwistMin - twist, dt);
                corrected += LimitAxis(ref child, ref holder, swingAWorld, swingA - limit.Swing1Max, dt);
                corrected += LimitAxis(ref child, ref holder, Float3.Zero - swingAWorld, -limit.Swing1Max - swingA, dt);
                corrected += LimitAxis(ref child, ref holder, swingBWorld, swingB - limit.Swing2Max, dt);
                corrected += LimitAxis(ref child, ref holder, Float3.Zero - swingBWorld, -limit.Swing2Max - swingB, dt);
                if (corrected > 0f)
                {
                    _bodies[i] = child;
                    _bodies[parent] = holder;
                }
            }
        }

        void ProjectLimits()
        {
            for (int i = 0; i < BodyCount; i++)
            {
                var role = (Role)i;
                if (Roles.IsRoot(role))
                {
                    continue;
                }
                int parent = (int)Roles.Parent(role);
                Quaternion excursion = Excursion(_bodies[parent].Rotation, _bodies[i].Rotation, _bind[i]);
                Quaternion clamped = ClampPartial(role, HumanProfile.Of(role).Limit, excursion);
                RoleBody child = _bodies[i];
                child.Rotation = Quaternion.Normalize(Quaternion.Multiply(
                    SanitizeRotation(_bodies[parent].Rotation),
                    Quaternion.Multiply(_bind[i], clamped)));
                _bodies[i] = child;
            }
        }

        float LimitAxis(ref RoleBody child, ref RoleBody holder, Float3 outward, float excess, float dt)
        {
            if (!(excess > LimitSlop))
            {
                return 0f;
            }
            if (outward.LengthSquared < 1e-12f)
            {
                return 0f;
            }
            Float3 unit = outward / outward.Length;
            Float3 relative = child.AngularVelocity - holder.AngularVelocity;
            float closing = Float3.Dot(relative, unit);
            float drive = LimitBeta / dt * excess;
            float maxDrive = 4f;
            if (drive > maxDrive)
            {
                drive = maxDrive;
            }
            if (closing + drive <= 0f)
            {
                return 0f;
            }
            float diagonal = Float3.Dot(unit, child.InverseInertiaWorld.Multiply(unit))
                + Float3.Dot(unit, holder.InverseInertiaWorld.Multiply(unit));
            if (!(diagonal > 1e-12f))
            {
                return 0f;
            }
            float lambda = -(closing + drive) / diagonal;
            Float3 impulse = unit * lambda;
            child.AngularVelocity = child.AngularVelocity + child.InverseInertiaWorld.Multiply(impulse);
            holder.AngularVelocity = holder.AngularVelocity - holder.InverseInertiaWorld.Multiply(impulse);
            return 1f;
        }

        static Float3 TwistLocal(Role role) => role switch
        {
            Role.ForearmLeft => new Float3(-1f, 0f, 0f),
            Role.ForearmRight => new Float3(1f, 0f, 0f),
            Role.ShinLeft => new Float3(1f, 0f, 0f),
            Role.ShinRight => new Float3(-1f, 0f, 0f),
            _ => new Float3(0f, 1f, 0f),
        };

        static Float3 SwingALocal(Role role) => role switch
        {
            Role.ForearmLeft => new Float3(0f, 1f, 0f),
            Role.ForearmRight => new Float3(0f, 1f, 0f),
            Role.ShinLeft => new Float3(0f, 1f, 0f),
            Role.ShinRight => new Float3(0f, 1f, 0f),
            _ => new Float3(1f, 0f, 0f),
        };

        static Float3 SwingBLocal(Role role)
        {
            return new Float3(0f, 0f, 1f);
        }

        static Float3 Solve(Float3x3 matrix, Float3 target)
        {
            Float3 row0 = matrix.C0;
            Float3 row1 = matrix.C1;
            Float3 row2 = matrix.C2;
            float determinant = Float3.Dot(row0, Float3.Cross(row1, row2));
            if (!Scalar.IsFinite(determinant) || Scalar.Abs(determinant) < 1e-12f)
            {
                return Float3.Zero;
            }
            return AdjointTimes(matrix, target) / determinant;
        }

        void SolveContacts(float dt)
        {
            Float3 up = new Float3(0f, 1f, 0f);
            for (int i = 0; i < BodyCount; i++)
            {
                RoleBody body = _bodies[i];
                Float3 axis = Quaternion.Rotate(body.Rotation, new Float3(0f, 1f, 0f));
                float half = HarnessTables.HalfLength((Role)i);
                float radius = HarnessTables.Radius((Role)i);
                for (int s = -1; s <= 1; s++)
                {
                    int slot = i * 3 + (s + 1);
                    Float3 sample = body.Position + axis * (half * s);
                    float penetration = radius - sample.Y;
                    if (!(penetration > 0f))
                    {
                        _contactAccumulator[slot] = 0f;
                        continue;
                    }
                    Float3 arm = sample - body.Position;
                    Float3 point = body.LinearVelocity + Float3.Cross(body.AngularVelocity, arm);
                    float bias = ContactBeta / dt * (penetration > ContactSlop ? penetration - ContactSlop : 0f);
                    float diagonal = body.InverseMass + up.Dot(body, arm);
                    if (!(diagonal > 1e-12f))
                    {
                        continue;
                    }
                    float push = (bias - point.Y) / diagonal;
                    float kept = _contactAccumulator[slot];
                    float next = kept + push > 0f ? kept + push : 0f;
                    float applied = next - kept;
                    _contactAccumulator[slot] = next;
                    body.LinearVelocity = body.LinearVelocity + up * (applied * body.InverseMass);
                    body.AngularVelocity = body.AngularVelocity
                        + body.InverseInertiaWorld.Multiply(Float3.Cross(arm, up * applied));
                    Float3 after = body.LinearVelocity + Float3.Cross(body.AngularVelocity, arm);
                    Float3 slide = new Float3(after.X, 0f, after.Z);
                    float speed = slide.Length;
                    if (speed > 1e-9f)
                    {
                        Float3 direction = slide / speed;
                        float grip = body.InverseMass + direction.Dot(body, arm);
                        if (grip > 1e-12f)
                        {
                            float friction = -speed / grip;
                            float cap = GroundFriction * next;
                            if (friction > cap)
                            {
                                friction = cap;
                            }
                            if (friction < -cap)
                            {
                                friction = -cap;
                            }
                            body.LinearVelocity = body.LinearVelocity + direction * (friction * body.InverseMass);
                            body.AngularVelocity = body.AngularVelocity
                                + body.InverseInertiaWorld.Multiply(Float3.Cross(arm, direction * friction));
                        }
                    }
                }
                _bodies[i] = body;
            }
        }

        void IntegratePositions(float dt)
        {
            for (int i = 0; i < BodyCount; i++)
            {
                RoleBody body = ClampSpeeds(_bodies[i]);
                body.Position = body.Position + body.LinearVelocity * dt;
                float spin = body.AngularVelocity.Length;
                if (spin > 1e-9f && Scalar.IsFinite(spin))
                {
                    Quaternion turn = Quaternion.AxisAngle(body.AngularVelocity / spin, spin * dt);
                    body.Rotation = Quaternion.Normalize(Quaternion.Multiply(turn, body.Rotation));
                }
                else
                {
                    body.Rotation = Quaternion.Normalize(body.Rotation);
                }
                _bodies[i] = SanitizeBody(body, i);
            }
        }

        void RefreshInertias()
        {
            for (int i = 0; i < BodyCount; i++)
            {
                RoleBody body = _bodies[i];
                body.InverseInertiaWorld = Inertia.InverseToWorld(_principal[i], body.Rotation);
                _bodies[i] = body;
            }
        }

        static Quaternion ClampPartial(Role role, JointLimit limit, Quaternion excursion)
        {
            Quaternion full = ClampExcursion(role, limit, excursion);
            Quaternion span = Quaternion.Normalize(Quaternion.Multiply(full, Quaternion.InverseUnit(Quaternion.Normalize(excursion))));
            if (span.W < 0f)
            {
                span = -span;
            }
            Float3 vector = RotationVector(span);
            float angle = vector.Length * 0.1f;
            if (angle < 1e-9f || !Scalar.IsFinite(angle))
            {
                return Quaternion.Normalize(excursion);
            }
            Quaternion step = Quaternion.AxisAngle(vector / vector.Length, angle);
            return Quaternion.Normalize(Quaternion.Multiply(step, Quaternion.Normalize(excursion)));
        }

        static Quaternion ClampExcursion(Role role, JointLimit limit, Quaternion excursion)
        {
            Float3 vector = RotationVector(excursion);
            float twist = ClampAxis(Float3.Dot(vector, TwistLocal(role)), limit.TwistMin, limit.TwistMax);
            float swingA = ClampAxis(Float3.Dot(vector, SwingALocal(role)), -limit.Swing1Max, limit.Swing1Max);
            float swingB = ClampAxis(Float3.Dot(vector, SwingBLocal(role)), -limit.Swing2Max, limit.Swing2Max);
            Float3 rebuilt = TwistLocal(role) * twist + SwingALocal(role) * swingA + SwingBLocal(role) * swingB;
            float angle = rebuilt.Length;
            if (angle < 1e-9f || !Scalar.IsFinite(angle))
            {
                return Quaternion.Identity;
            }
            return Quaternion.AxisAngle(rebuilt / angle, angle);
        }

        static float ClampAxis(float value, float minimum, float maximum)
        {
            if (value < minimum)
            {
                return minimum;
            }
            if (value > maximum)
            {
                return maximum;
            }
            return value;
        }

        static Quaternion Excursion(Quaternion parentRotation, Quaternion childRotation, Quaternion bind)
        {
            Quaternion relative = Quaternion.Multiply(
                Quaternion.InverseUnit(SanitizeRotation(parentRotation)),
                SanitizeRotation(childRotation));
            Quaternion excursion = Quaternion.Multiply(Quaternion.InverseUnit(bind), relative);
            if (excursion.W < 0f)
            {
                excursion = -excursion;
            }
            return Quaternion.Normalize(excursion);
        }

        static Float3 RotationVector(Quaternion excursion)
        {
            Float3 vector = excursion.Vector;
            float length = vector.Length;
            if (length < 1e-9f)
            {
                return Float3.Zero;
            }
            float angle = 2f * (float)Math.Atan2(length, excursion.W > 0f ? excursion.W : 0f);
            return vector * (angle / length);
        }

        static float Violation(Role role, JointLimit limit, Quaternion excursion)
        {
            Float3 axis = RotationVector(excursion);
            float twist = Float3.Dot(axis, TwistLocal(role));
            float swingA = Float3.Dot(axis, SwingALocal(role));
            float swingB = Float3.Dot(axis, SwingBLocal(role));
            float over = 0f;
            over = Scalar.Max(over, twist - limit.TwistMax);
            over = Scalar.Max(over, limit.TwistMin - twist);
            over = Scalar.Max(over, swingA - limit.Swing1Max);
            over = Scalar.Max(over, -limit.Swing1Max - swingA);
            over = Scalar.Max(over, swingB - limit.Swing2Max);
            over = Scalar.Max(over, -limit.Swing2Max - swingB);
            return over > 0f ? over : 0f;
        }

        static Float3x3 JointStiffness(RoleBody child, RoleBody holder, Float3 armChild, Float3 armParent)
        {
            float total = child.InverseMass + holder.InverseMass;
            Float3 row0 = new Float3(total, 0f, 0f)
                - Float3.Cross(armChild, child.InverseInertiaWorld.Multiply(Float3.Cross(armChild, new Float3(1f, 0f, 0f))))
                - Float3.Cross(armParent, holder.InverseInertiaWorld.Multiply(Float3.Cross(armParent, new Float3(1f, 0f, 0f))));
            Float3 row1 = new Float3(0f, total, 0f)
                - Float3.Cross(armChild, child.InverseInertiaWorld.Multiply(Float3.Cross(armChild, new Float3(0f, 1f, 0f))))
                - Float3.Cross(armParent, holder.InverseInertiaWorld.Multiply(Float3.Cross(armParent, new Float3(0f, 1f, 0f))));
            Float3 row2 = new Float3(0f, 0f, total)
                - Float3.Cross(armChild, child.InverseInertiaWorld.Multiply(Float3.Cross(armChild, new Float3(0f, 0f, 1f))))
                - Float3.Cross(armParent, holder.InverseInertiaWorld.Multiply(Float3.Cross(armParent, new Float3(0f, 0f, 1f))));
            return new Float3x3(row0, row1, row2);
        }

        static Float3 AdjointTimes(Float3x3 matrix, Float3 target)
        {
            Float3 row0 = matrix.C0;
            Float3 row1 = matrix.C1;
            Float3 row2 = matrix.C2;
            Float3 col0 = Float3.Cross(row1, row2);
            Float3 col1 = Float3.Cross(row2, row0);
            Float3 col2 = Float3.Cross(row0, row1);
            return new Float3(
                col0.X * target.X + col1.X * target.Y + col2.X * target.Z,
                col0.Y * target.X + col1.Y * target.Y + col2.Y * target.Z,
                col0.Z * target.X + col1.Z * target.Y + col2.Z * target.Z);
        }

        static RoleBody ClampSpeeds(RoleBody body)
        {
            float speed = body.LinearVelocity.Length;
            if (speed > MaxSpeed && Scalar.IsFinite(speed))
            {
                body.LinearVelocity = body.LinearVelocity * (MaxSpeed / speed);
            }
            if (!body.LinearVelocity.IsFinite)
            {
                body.LinearVelocity = Float3.Zero;
            }
            float spin = body.AngularVelocity.Length;
            if (spin > MaxAngularSpeed && Scalar.IsFinite(spin))
            {
                body.AngularVelocity = body.AngularVelocity * (MaxAngularSpeed / spin);
            }
            if (!body.AngularVelocity.IsFinite)
            {
                body.AngularVelocity = Float3.Zero;
            }
            return body;
        }

        static RoleBody SanitizeBody(RoleBody body, int index)
        {
            if (!body.Position.IsFinite)
            {
                body.Position = HarnessTables.Center((Role)index);
            }
            if (!body.Rotation.IsValidRotation)
            {
                body.Rotation = Quaternion.Identity;
            }
            body.Rotation = Quaternion.Normalize(body.Rotation);
            if (!body.LinearVelocity.IsFinite)
            {
                body.LinearVelocity = Float3.Zero;
            }
            if (!body.AngularVelocity.IsFinite)
            {
                body.AngularVelocity = Float3.Zero;
            }
            return body;
        }

        static float ClampScale(Float3 total, float maximum)
        {
            float length = total.Length;
            if (!(length > 1e-9f) || !(maximum > 0f) || !Scalar.IsFinite(maximum))
            {
                return 1f;
            }
            if (length <= maximum)
            {
                return 1f;
            }
            return maximum / length;
        }

        static float DampFactor(RoleBody body, float damping, float dt)
        {
            if (!(damping > 0f) || !Scalar.IsFinite(damping) || !(dt > 0f))
            {
                return 1f;
            }
            float wide = ResponseMax(body);
            float pull = damping * dt * wide;
            if (!(pull > 0f) || !Scalar.IsFinite(pull))
            {
                return 1f;
            }
            return 1f / (1f + pull);
        }

        static float DampFactorPair(RoleBody child, RoleBody holder, float damping, float dt)
        {
            if (!(damping > 0f) || !Scalar.IsFinite(damping) || !(dt > 0f))
            {
                return 1f;
            }
            float wide = ResponseMax(child) + ResponseMax(holder);
            float pull = damping * dt * wide;
            if (!(pull > 0f) || !Scalar.IsFinite(pull))
            {
                return 1f;
            }
            return 1f / (1f + pull);
        }

        static float ResponseMax(RoleBody body)
        {
            float xx = Float3.Dot(new Float3(1f, 0f, 0f), body.InverseInertiaWorld.Multiply(new Float3(1f, 0f, 0f)));
            float yy = Float3.Dot(new Float3(0f, 1f, 0f), body.InverseInertiaWorld.Multiply(new Float3(0f, 1f, 0f)));
            float zz = Float3.Dot(new Float3(0f, 0f, 1f), body.InverseInertiaWorld.Multiply(new Float3(0f, 0f, 1f)));
            float wide = Scalar.Max(xx, Scalar.Max(yy, zz));
            return wide > 0f ? wide : 0f;
        }

        static Float3 Damp(Float3 value, float rate, float dt)
        {
            if (!value.IsFinite)
            {
                return Float3.Zero;
            }
            float keep = 1f - rate * dt;
            if (keep < 0f)
            {
                keep = 0f;
            }
            return value * keep;
        }

        static Float3 AnchorWorld(RoleBody body, Float3 local)
        {
            return body.Position + Quaternion.Rotate(body.Rotation, local);
        }

        static Float3 PointVelocity(RoleBody body, Float3 worldPoint)
        {
            return body.LinearVelocity + Float3.Cross(body.AngularVelocity, worldPoint - body.Position);
        }

        static Float3 ToLocal(Float3 offset, Quaternion rotation)
        {
            return Quaternion.Rotate(Quaternion.InverseUnit(rotation), offset);
        }

        static Quaternion SanitizeRotation(Quaternion rotation)
        {
            return rotation.IsValidRotation ? Quaternion.Normalize(rotation) : Quaternion.Identity;
        }
    }

    static class HarnessMath
    {
        public static float Dot(this Float3 direction, RoleBody body, Float3 arm)
        {
            Float3 pushed = body.InverseInertiaWorld.Multiply(Float3.Cross(arm, direction));
            return Float3.Dot(direction, Float3.Cross(pushed, arm));
        }
    }
}
