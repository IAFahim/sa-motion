# Contract: JointDrive target convention

`JointDrive.Target` is the desired world orientation of the driven body.
`TargetAngularVelocity` is that body's desired world angular velocity.
`QuaternionDrive.Torque(current, target, ...)` derives the error as
`target * inverse(current)`, so a world target drives the body to it.

Worked example: shin body at `Rs`, knee joint frame at `Rk`.
Flexion error `e = desired - actual` (rad, negative when bent) rotates
the leg the right way as `Rt = Rk * AxisAngle(+X, -e) * Relative * Rs`,
because `Rk` is `RotX(90)` here and `+X` flexion moves the foot backward.
The pre-R2b `WriteLegDrives` knee term used `+e` and flexed backwards.

Known dissenters: `Drive.ToJointDrive` emits a joint-local rotation with
no parent frame composed, so its caller must compose the world target.
`StanceDrive.Calibrate` takes a world pose (consistent).
`BlendWeights.BlendDrive` interpolates world targets (consistent).
