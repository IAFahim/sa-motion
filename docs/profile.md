# Humanoid profile

Mass fractions are published-derived (P). Limits, density weights and the
hinge bound are authored starting values (A). No value comes from GTA,
Delirium or any game file.

## Mass source

Fractions are from de Leva P. (1996) Adjustments to Zatsiorsky-Seluyanov's
segment inertia parameters, J. Biomech. 29(9):1223-1230, Table 2, male mean
segment masses, regrouped onto the 15 SA roles:

Pelvis 0.1117 = Lower trunk; Torso 0.3229 = Upper 0.1596 + Middle 0.1633 (P).
Head 0.0694 (P). Per side (P): UpperArm 0.0271, Forearm 0.0162, Hand 0.0061,
Thigh 0.1416, Shin (shank) 0.0433, Foot 0.0137. Fractions sum to 1.
DensityWeight is a uniform authored 1.0 for every role (A).

## Axis convention

Limits are expressed in the joint's own frame. Twist is rotation about the
segment long axis (+Y, pointing distal). Swing1 is flexion/extension about
+X and Swing2 is abduction/adduction (or lateral bend) about +Z, in radians.
TwistMin <= TwistMax; swings lie in 0..pi as maxima from neutral.
The pelvis is the root and its limit is default (all zeros).

## Hinges (bound 0.05 rad, authored)

Elbows (ForearmLeft/Right) and knees (ShinLeft/Right) are hinges: both swing
maxima are <= 0.05 rad and flexion runs on the twist axis (elbow 0..145 deg,
knee 0..150 deg, mirrored sign across L/R). Remaining joints are ball-like
with the authored swing ranges in HumanProfile.cs.

Shoulder twist +-90 deg, swings 170/90 deg; hip twist +-45 deg, swings
120/60 deg; neck twist +-70 deg, swings 60/45 deg; wrist twist +-45 deg,
swings 50/30 deg; ankle twist +-15 deg, swings 50/30 deg; torso twist +-30
deg, swings 45/30 deg (all A, degrees for reading; code stores radians).

Left/right pairs share swing maxima and mirror twist ranges exactly.
