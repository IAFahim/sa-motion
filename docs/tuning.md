# Controller parameter profile and tuning log

ControllerProfile binds Name with the seven area Params below. (×m) scales with totalMass/70 and (×h) with height/1.75; unmarked fields keep their Standard at any body mass or height.
Zero is a problem only where the Range uses an open lower bound; everywhere else zero is a valid choice.

| Area | Field | Unit | Range | Value | Source | Exercised |
|---|---|---|---|---|---|---|
| Balance | Up, GravityUp | unit vector | finite, \|v\|>0 | (0,1,0) | A:tick basis, world +Y | no |
| Balance | DeltaTime | s | (0, 1] | 0.0167 | A:60 Hz fixed step | no |
| Balance | PelvisHeight | m (×h) | (0, 3] | 0.95 | S:718 | no |
| Balance | LegLength | m (×h) | (0, 3] | 1 | A:reference leg length | no |
| Balance | HipSpread | m (×h) | [0, 1] | 0.11 | S:719 | no |
| Balance | FootWidth | m (×h) | (0, 1] | 0.22 | A:foot separation for support test | no |
| Balance | StepOffset | m (×h) | [0, 1] | 0.05 | S:418 | no |
| Balance | StepLead | s | [0, 2] | 0.2 | S:411 | no |
| Balance | SwingLeadGain | 1 | [0, 10] | 0.1 | S:168 | no |
| Balance | SupportInsideMargin | m | [-1, 0] | -0.18 | S:172 | no |
| Balance | WideStanceRelease, SingleSupportSink, SwingMinDistance | m (×h) | [0, 2] | 0.05 | S:415, S:733, S:664 | no |
| Balance | YawCenter, LeanBias | rad | [-3.2, 3.2] | 0 | S:148, S:544 | no |
| Balance | YawHalfSpan | rad | (0, 3.2] | 0.15 | S:152 | no |
| Balance | StepHeight | m (×h) | [0, 1] | 0.12 | S:458 | no |
| Balance | StepClamp | m (×h) | (0, 2] | 0.6 | S:444 | no |
| Balance | AbortThreshold | 1 | [0, 10] | 0 | A:carried for R2 tick | no |
| Balance | Taper, LowAbort, ContactResponse | flag | false/true | false | A:carried for R2 tick | no |
| Balance | LegStiffness | 1 | [0, 10] | 1 | A:carried for R2 tick | no |
| Balance | MaxStepDelay | count | [1, 600] | 30 | S:166 | no |
| Balance | FootSupportHalfWidth | m (×h) | (0, 1] | 0.09 | S:293 | no |
| Balance | FootSupportHalfLength | m (×h) | (0, 1] | 0.16 | S:293 | no |
| Balance | ReachBias | m (×h) | [0, 1] | 0.05 | S:325 | no |
| Balance | ReachForwardScale, ReachLateralScale | 1 | (0, 5] | 1 | S:328 | no |
| Balance | Epsilon | 1 | (0, 0.01] | 1e-6 | S:339 | no |
| Balance | LiftGain | 1 | [0, 10] | 0.2 | S:449 | no |
| Balance | SoleOffset | m (×h) | [0, 0.5] | 0.06 | S:450 | no |
| Balance | AnkleBackstepBias | m (×h) | [0, 1] | 0.05 | S:784 | no |
| Balance | AirborneLift | m (×h) | [0, 1] | 0.02 | S:467 | no |
| Balance | YawPredictHorizon | s | [0, 2] | 0.06 | S:491 | no |
| Balance | PelvisLeadGain, LateralRespond, LateralLeanGain, LateralScale, ForwardRespond, ForwardLeanGain, ForwardScale | 1 | [0, 10] | 1 | A:unity pass-throughs | no |
| Balance | YawPredictGain, ToeAlignGain, SwingLiftGain | 1 | [0, 10] | 1 | S:491, S:782, S:773 | no |
| Balance | YawValidRange | rad | (0, 3.2] | 3 | S:491 | no |
| Balance | YawBlendRate | 1/tick | [0, 1] | 0.1 | S:517 | no |
| Balance | YawLimit | rad | (0, 3.2] | 1 | S:517 | no |
| Balance | PelvisLeanForwardLimit | rad | (0, 1.6] | 0.4 | S:538 | no |
| Balance | PelvisLeanBackLimit | rad | (0, 1.6] | 0.3 | S:538 | no |
| Balance | LeanAccumulatorRate | 1/tick | [0, 1] | 0.06 | S:552 | no |
| Balance | ToeUprightBand | 1 | [0, 1] | 0.1 | S:554 | no |
| Balance | ToeUprightGain, DropGain | 1 | [0, 20] | 2 | S:554, S:719 | no |
| Balance | MinLeadDistance | m (×h) | [0, 2] | 0.07 | A:lead floor literal | no |
| Balance | MaxStepCorrection | m (×h) | (0, 1] | 0.08 | S:475 | no |
| Balance | SwingBlendBase | 1 | (0, 20] | 2 | S:664 | no |
| Balance | SwingReach | m/s | [0, 10] | 1 | A:swing reach factor | no |
| Balance | SwingAheadHorizon | s | [0, 2] | 0.12 | S:773 | no |
| Balance | SwingLiftBase | m (×h) | [0, 2] | 0.03 | S:773 | no |
| Balance | SwingLiftMin | m (×h) | [0, 2] | 0.05 | S:773 | no |
| Balance | SwingLiftMax | m (×h) | (0, 2] | 0.45 | S:773 | no |
| Balance | ShinAlignGain | 1 | [0, 10] | 0.3 | S:782 | no |
| Balance | ToeSpinFactor | s | [0, 10] | 0.05 | A:toe spin factor | no |
| Balance | AnkleSwingOffset | rad | [-1, 1] | 0.15 | A:replaces leg-length cosine | no |
| Balance | DropCapFraction | 1 | [0, 1] | 0.25 | S:723 | no |
| Balance | LeanClamp | 1 | (0, 1] | 0.06 | A:hip-shift clamp | no |
| Balance | LateralLeanRange, ForwardLeanRange | 1 | (0, 1] | 0.1 | A:lean integrator bounds | no |
| Balance | CentrePreviewTime | s | [0, 2] | 0.15 | A:COM preview time | no |
| Balance | HipHalfTwist | rad | (0, 3.2] | 0.6 | A:human-scale joint travel | no |
| Balance | HipHalfLean1, HipHalfLean2 | rad | (0, 3.2] | 0.7 | A:human-scale joint travel | no |
| Balance | HipMidTwist, HipMidLean1, HipMidLean2 | rad | [-3.2, 3.2] | 0 | A:human-scale joint travel | no |
| Balance | KneeHalfRange | rad | (0, 3.2] | 1.2 | A:human-scale joint travel | no |
| Balance | KneeMid | rad | [-3.2, 3.2] | -1 | A:human-scale joint travel | no |
| Balance | AnkleHalfPitch | rad | (0, 3.2] | 0.5 | A:human-scale joint travel | no |
| Balance | AnkleMidPitch, AnkleMidYaw, SpineMidLean | rad | [-3.2, 3.2] | 0 | A:human-scale joint travel | no |
| Balance | AnkleHalfYaw | rad | (0, 3.2] | 0.4 | A:human-scale joint travel | no |
| Balance | SpineHalfLean | rad | (0, 3.2] | 0.4 | A:human-scale joint travel | no |
| Balance | HipStrength | N m/rad (×m) | (0, 10000] | 150 | A:larger joints get stiffer servos | no |
| Balance | HipDamping | N m s/rad (×m) | [0, 1000] | 15 | A:larger joints get stiffer servos | no |
| Balance | KneeStrength | N m/rad (×m) | (0, 10000] | 120 | A:larger joints get stiffer servos | no |
| Balance | KneeDamping | N m s/rad (×m) | [0, 1000] | 12 | A:larger joints get stiffer servos | no |
| Balance | AnkleStrength | N m/rad (×m) | (0, 10000] | 60 | A:larger joints get stiffer servos | no |
| Balance | AnkleDamping | N m s/rad (×m) | [0, 1000] | 6 | A:larger joints get stiffer servos | no |
| Balance | SpineStrength | N m/rad (×m) | (0, 10000] | 100 | A:larger joints get stiffer servos | no |
| Balance | SpineDamping | N m s/rad (×m) | [0, 1000] | 10 | A:larger joints get stiffer servos | no |
| Balance | MaxTorque | N m (×m) | (0, 10000] | 400 | A:drive clamp | no |
| BodyBalance | BodyStiffness | stiffness (×m) | (0, 100] | 9 | S:131 | no |
| BodyBalance | ElbowGain | 1 | [0, 5] | 0.6 | S:13 | no |
| BodyBalance | Damping | 1 | [0, 5] | 0.7 | S:70 | no |
| BodyBalance | MaxSteps | count | [1, 16] | 3 | S:192 | no |
| BodyBalance | UpperStiffness | stiffness (×m) | (0, 100] | 6 | S:27 | no |
| BodyBalance | SomersaultThreshold | rad | [0, 3.2] | 0.35 | S:17 | no |
| BodyBalance | LeanRateThreshold | rad/s | [0, 20] | 1.2 | S:74 | no |
| BodyBalance | LeanRateThresholdY | rad/s | [0, 20] | 1.5 | S:5 | no |
| BodyBalance | RateGainXZ | 1 | [0, 10] | 0.8 | S:108 | no |
| BodyBalance | ElbowResponseGain | 1 | [0, 10] | 0.9 | S:112 | no |
| BodyBalance | LeanFloor | rad | [0, 1] | 0.05 | S:126 | no |
| BodyBalance | SpineMuscle | stiffness (×m) | (0, 100] | 4 | S:12 | no |
| BodyBalance | ShoulderHipRate | 1 | [0, 10] | 0.4 | S:132 | no |
| BodyBalance | TurnIntervalSpan | s | [0, 30] | 2 | S:114 | no |
| BodyBalance | TurnIntervalBase | s | [0, 30] | 1 | S:110 | no |
| BodyBalance | TurnOffProb | 1 | [0, 1] | 0.6 | S:73 | no |
| BodyBalance | TurnToTargetProb, TurnToVelocityProb | 1 | [0, 1] | 0.1 | S:57, S:24 | no |
| BodyBalance | TurnAwayProb, TurnSideProb, TurnProbFloor | 1 | [0, 1] | 0.05 | S:9, S:182, S:68 | no |
| BodyBalance | TurnSpeedGate | m/s | [0, 30] | 0.4 | S:140 | no |
| BodyBalance | SpeedFloor | m/s | [0, 30] | 0.15 | S:129 | no |
| BodyBalance | TwistGainA | 1 | [0, 10] | 0.5 | S:63 | no |
| BodyBalance | CutSpan | 1 | [0, 10] | 0.6 | S:20 | no |
| BodyBalance | RateCap | rad/s | (0, 30] | 3 | S:29 | no |
| BodyBalance | ElbowMax | rad | (0, 3.2] | 2.4 | S:32 | no |
| BodyBalance | CatchGainX, CatchGainZ | gain (×m) | (0, 100] | 8 | S:216, S:218 | no |
| BodyBalance | CatchGainY | gain (×m) | (0, 100] | 10 | S:217 | no |
| BodyBalance | StepClampLo | 1 | [0, 5] | 0.8 | S:219 | no |
| BodyBalance | StepClampHi | 1 | [0, 5] | 1.2 | S:220 | no |
| BodyBalance | StabiliseDamping | 1 | [0, 5] | 0.9 | S:221 | no |
| BodyBalance | OwnOffsetX, OwnOffsetY, OwnOffsetZ | m (×h) | [-2, 2] | 0.1 | S:222, S:223, S:224 | no |
| BodyBalance | ArmsOutMinLean | rad | [-3.2, 3.2] | -0.15 | S:183 | no |
| BodyBalance | ElbowContactAngle | rad | [0, 3.2] | 1.9 | S:181 | no |
| BodyBalance | DecisionLoseLean | rad | [0, 3.2] | 0.12 | A:packet-added decision layer | no |
| BodyBalance | DecisionFallLean | rad | [0, 3.2] | 0.6 | A:packet-added decision layer | no |
| BodyBalance | DecisionExitFraction | 1 | [0, 1] | 0.5 | A:packet-added decision layer | no |
| BodyBalance | FallHoldTime | s | [0, 30] | 0.8 | A:packet-added decision layer | no |
| BodyBalance | LookProbeRange | m (×h) | (0, 100] | 6 | A:packet-added probe bound | no |
| BodyBalance | MaxDrive | stiffness cap (×m) | (0, 10000] | 400 | A:packet-added output bound | no |
| BodyBalance | UprightCorrect | 1 | [0, 10] | 0.5 | A:packet-added correction | no |
| BodyBalance | TwistCorrect | 1 | [0, 10] | 0.4 | A:packet-added correction | no |
| BodyBalance | TorsoPitchBias | rad | [-3.2, 3.2] | 0.06 | A:collapsed spine bias mean | no |
| BodyBalance | LookVelLead | s | [0, 10] | 1.2 | A:packet-added look lead | no |
| BodyBalance | UseHeadLook, UseArmsOut | flag | [0, 1] | 1 | A:packet-added output gates | no |
| BodyBalance | UseBodyTurn, UsePelvisFrame | flag | [0, 1] | 0 | A:packet-added output gates | no |
| CatchFall | LegsStiffness | N m/rad (×m) | (0, 1000] | 6 | S:32 | no |
| CatchFall | TorsoStiffness | N m/rad (×m) | (0, 1000] | 8 | S:131 | no |
| CatchFall | ArmsStiffness | N m/rad (×m) | (0, 1000] | 12 | S:26 | no |
| CatchFall | ForwardMaxArmOffset | m (×h) | [-2, 2] | 0.35 | S:63 | no |
| CatchFall | BackwardsMinArmOffset | m (×h) | [-2, 2] | -0.25 | S:17 | no |
| CatchFall | SpinReductionGain | 1 | [0, 10] | 0 | A:spin brake off unless caller opts in | no |
| CatchFall | UseHeadLook | flag | [0, 1] | 1 | A:head tracks fall direction by default | no |
| CatchFall | Reach | m (×h) | (0, 5] | 0.7 | S:114 | no |
| CatchFall | ProbeLength | m (×h) | (0, 20] | 2 | S:246 | no |
| CatchFall | MinExtension | m (×h) | [0, 5] | 0.15 | S:110 | no |
| CatchFall | StiffnessFloor | 1 | [0, 1] | 0.15 | S:24 | no |
| CatchFall | GainFloor | 1 | [0, 1] | 0.2 | S:57 | no |
| CatchFall | RelaxRate | 1/s | [0, 30] | 0.5 | A:contact release rate | no |
| CatchFall | RecoverRate | 1/s | [0, 30] | 2 | A:free-arm authority regain rate | no |
| CatchFall | GainRate | 1/s | [0, 30] | 1.5 | S:60 | no |
| CatchFall | ArmCapScale | 1 | (0, 10] | 1 | S:5 | no |
| CatchFall | FallBias | m/s | [0, 10] | 0.5 | S:247 | no |
| CatchFall | SpinTorqueScale | 1 | [0, 20] | 2 | S:57 | no |
| CatchFall | SpinTorqueMax | N m (×m) | (0, 10000] | 60 | A:spin brake cap | no |
| CatchFall | ClavFollowRatio | 1 | [0, 1] | 0.5 | A:shoulder follow folded into upper arm | no |
| CatchFall | ElbowRest | rad | [0, 3.2] | 0.35 | S:70 | no |
| CatchFall | TorqueLimit | N m (×m) | (0, 10000] | 80 | A:arm drive clamp | no |
| CatchFall | SafeMissStiffness | N m/rad (×m) | (0, 1000] | 1 | S:24 | no |
| CatchFall | SpineTwistLimit | rad | (0, 3.2] | 1 | A:safe twist cap | no |
| Brace | BodyStiffness | N m/rad (×m) | (0, 1000] | 40 | S:21 | no |
| Brace | ArmDamping | N m s/rad (×m) | [0, 100] | 6 | S:13 | no |
| Brace | WristStiffness | N m/rad (×m) | (0, 1000] | 30 | A:wrist hold stiffness | no |
| Brace | WristDamping | N m s/rad (×m) | [0, 100] | 5 | A:wrist damping paired with hold | no |
| Brace | MaxTorque | N m (×m) | (0, 10000] | 200 | A:drive clamp | no |
| Brace | BraceDistance | m (×h) | [0, 10] | 0.9 | S:8 | no |
| Brace | MaxReach | m (×h) | (0, 3] | 0.75 | S:127 | no |
| Brace | PredictionTime | s | [0, 5] | 0.35 | S:24 | no |
| Brace | ReachAbsorptionTime | s | [0, 5] | 0.25 | S:227 | no |
| Brace | TargetSpeedLimit | m/s | (0, 30] | 3 | S:27 | no |
| Brace | ApproachFloor, HysteresisBand | m (×h) | [0, 5] | 0.05 | S:73, S:64 | no |
| Brace | ReleaseMargin | m (×h) | [0, 5] | 0.08 | S:57 | no |
| Brace | ContactHoldTime | s | [0, 30] | 0.4 | A:contact hold time | no |
| Brace | SideSpread | m (×h) | [0, 2] | 0.12 | S:57 | no |
| Brace | StaggerTime | s | [0, 5] | 0.15 | S:234 | no |
| Brace | CrouchAmount | m (×h) | [0, 1] | 0.12 | S:57 | no |
| Brace | LeanGain | 1 | [0, 10] | 0.5 | S:9 | no |
| Brace | GuardStiffness | N m/rad (×m) | (0, 1000] | 8 | S:29 | no |
| Brace | GuardDamping | N m s/rad (×m) | [0, 100] | 2 | A:guard damping paired with stiffness | no |
| Drive | Strength | 1/s² | (0, 10000] | 12 | A:human-scale starting value | no |
| Drive | Damping | 1/s | [0, 100] | 2 | A:human-scale starting value | no |
| Drive | MaxTorque | N m (×m) | (0, 10000] | 200 | A:human-scale starting value | no |
| Drive | MinDamping | 1/s | [0, 100] | 0.05 | S:60 | no |
| Drive | StiffnessCap | 1/s² | (0, 100000] | 1000 | A:authored cap | no |
| Drive | LimitMargin | rad | [0, 1] | 0.02 | S:126 | no |
| Drive | SoftPull | 1 | [0, 1] | 0.25 | S:108 | no |
| Coordination | HysteresisMargin | 1 | [0, 100] | 5 | A:arbitration hysteresis | no |
| Coordination | FadeTime | s | [0, 30] | 0.25 | A:mode fade time | no |
| Coordination | MinLegStiffness | N m/rad (×m) | (0, 10000] | 50 | S:32 | no |
| Coordination | MaxLegStiffness | N m/rad (×m) | (0, 10000] | 600 | S:212 | no |
| Coordination | SupportedAnkleResponse | 1 | [0, 20] | 3 | S:27 | no |
| Coordination | SupportedKneeResponse, SwingKneeResponse | 1 | [0, 20] | 2 | S:12, A:swing literal | no |
| Coordination | SwingAnkleResponse | 1 | [0, 20] | 1 | A:swing literal | no |
| GetUp | LyingHeadDrop | m (×h) | [0, 3] | 0.3 | S:24 | no |
| GetUp | UprightHeadRise | m (×h) | [0, 3] | 0.35 | A:upright head-rise gate | no |
| GetUp | PoseDot | 1 | [-1, 1] | 0.5 | A:pose-class cosine gate | no |
| GetUp | MaxAlignOffset | m (×h) | (0, 50] | 5 | A:alignment safety bound | no |
| GetUp | ClearDistance | m (×h) | (0, 50] | 1.5 | A:clearance radius | no |

## Tuning log

Date | Scenario | Area.Field | Before | After | Measured effect | Who
---|---|---|---|---|---|---|
2026-10-31 | none | none | none | none | none | R10 (example: log a row here for every Standard change) |
2026-10-31 | StandTenSeconds | Balance.PelvisHeight | 0.95 | 1.0 | fall 38 -> 62 ticks (0.95 crouches 39 deg from 1.00 start) | R2b
2026-10-31 | StandTenSeconds | Balance.SupportInsideMargin | -0.18 | 0.0 | gate functional with horizontal quad; 62 -> 62 alone, steps gated from t15 to t55 | R2b
2026-10-31 | StandTenSeconds | Balance.AnkleClearance | n/a (new) | 0.0 | runtime-seeded 0.10 by tick; sole-space goals reach ankle | R2b
