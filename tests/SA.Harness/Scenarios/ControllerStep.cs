using System;
using SA.ReactionControl;

namespace SA.Harness
{
    public delegate void ControllerStep(HarnessWorld world, float dt, Span<JointDrive> drives);
}
