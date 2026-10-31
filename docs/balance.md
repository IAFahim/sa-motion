# Balance controller (B1)

Independent MIT implementation derived from published theory, not from any
engine checkout. Linear inverted pendulum and capture point (Pratt et al.
2006; Koolen et al. 2012) give the foot-placement target. Ankle and hip
strategies (Kuo and Zajac 1993) map capture error to joint action. Ordinary
joint-space proportional-derivative drives with gravity handled by the
harness carry the motion. TargetWorld entries are animation world
orientations held by the harness single-clamp-sum drives.

## Method

Center of mass comes from Humanoid mass fractions. Natural frequency is
square root of gravity over clamped height. Capture point is horizontal
center plus horizontal velocity over frequency. Support center is the mean
of contacting foot points. Margin is capture distance from support minus
support radius. Outer loops shift world-orientation targets: torso upright
correction from lean plus lean rate, foot pitch from capture error, knee
flex from pelvis height error, torso pitch from capture error, and torso
plus half pelvis counter-roll from lateral capture error.
Inner gains copy the harness hold schedule.

## Parameters

| Name | Value | Reason |
|---|---|---|
| Gravity | 9.81 | Harness ground gravity |
| ComHeightMin | 0.5 | Lowest center used for frequency so crouch never explodes gain |
| ComHeightMax | 1.4 | Highest center used for frequency so stretch never stalls correction |
| PelvisStiffness | 2000 | Harness schedule anchors torso corrections on the pelvis |
| TorsoStiffness | 200 | Harness schedule holds the heaviest child without whiplash |
| HeadStiffness | 80 | Harness schedule steadies the head lightly |
| ArmStiffness | 30 | Harness schedule keeps arms near bind cheaply |
| ForearmStiffness | 15 | Harness schedule keeps forearms near bind cheaply |
| HandStiffness | 10 | Harness schedule keeps hands near bind cheaply |
| ThighStiffness | 1000 | Harness schedule carries half the body per leg |
| ShinStiffness | 900 | Harness schedule keeps knees near straight under load |
| FootStiffness | 700 | Harness schedule holds ankle lever against topple gradient |
| PelvisDamping | 15.54 | Critical on coupled transverse inertia at 70 kg |
| TorsoDamping | 4.64 | Critical on coupled transverse inertia at 70 kg |
| HeadDamping | 2.11 | Critical on coupled transverse inertia at 70 kg |
| ArmDamping | 1.01 | Critical on coupled transverse inertia at 70 kg |
| ForearmDamping | 0.41 | Critical on coupled transverse inertia at 70 kg |
| UprightGain | 0.3 | Torso lean proportional correction that stops the 8 s fold |
| UprightDamp | 0.15 | Torso lean rate correction that damps the 3 s sway mode |
| UprightMax | 0.3 | Caps upright target shift inside stable tilt |
| AnkleGain | 2 | Capture error to foot pitch that shifts the center of pressure |
| AnkleVelGain | 0 | Capture velocity to foot pitch, kept off since pitch gain suffices |
| HipGain | 0.3 | Capture error to torso pitch that steadies fore-aft sway |
| HipVelGain | 0.05 | Center velocity to torso pitch that damps fore-aft sway rate |
| HipRollGain | 1 | Lateral capture error to torso counter-roll that rights side pushes |
| HipRollVelGain | 0.2 | Lateral center velocity to counter-roll that damps side sway rate |
| LeanMax | 0.3 | Caps capture lean target shift inside stable tilt |
| StepMargin | 0.05 | Capture must clear support by this before a step starts |
| StepHeight | 0.1 | Swing foot clearance that steps over ground without tripping |
| StepTime | 0.35 | Swing duration that lands inside one capture time constant |
| MaxReach | 0.7 | Horizontal step limit inside 0.86 m leg reach with margin |
| FallToneStiffness | 8 | Falling latch low tone that goes limp without snapping |
| FallToneDamping | 0.8 | Falling latch damping that avoids oscillation at low tone |
| FallToneTorque | 20 | Falling latch torque cap that stays soft on the ground |
