# Shared-animation v1 golden fixtures

These synthetic fixtures freeze bytes for independent readers. They are not imported Humanoid content and do not establish Unity retargeting quality.

The three-bone family uses compatibility words `(1,2,3,4)` in the specified little-endian AssetId layout, parents `[-1,0,1]`, local reference positions `[(0,0,0),(0,1,0),(1,0,0)]`, identity rotations and unit scale. There are three samples at 0, 0.5 and 1 seconds. Hips Y is `1 + 0.05*i` and the tip rotates `0.3*i` radians about Z. The root trajectory is identity. The motion is one-shot and has no additive reference or contacts.

`three-bone-raw.saclip` uses codec 0; `three-bone-packed.saclip` uses codec 1. `three-bone-target.saret` has target words `(10,0,0,0)`, the same topology, double local translations, identity calibration rotations, and hip-height ratio 2.

File SHA-256 values, including the populated internal digest field:

| File | Bytes | SHA-256 |
|---|---|---|
| three-bone-packed.saclip | 756 | a85a73b5f115cbdf478ef10b1f995d8f3016a8a3fc4ef44c5a5eee79abf59ef8 |
| three-bone-raw.saclip | 772 | 7ba9609c2f715bf1445ca634c8d1c751817cfdfbddfbed4660e790c1bf071a2a |
| three-bone-target.saret | 608 | dca879c5183cf898ab1bb18ba1090138f5658301de65144e69d752747027ccc0 |

The internal SACA digest instead hashes the complete file with bytes 40..71 zeroed. Keep these fixtures intact; a wire-format change needs an explicit version/compatibility decision.
