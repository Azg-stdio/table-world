# The chalker model: what an artist's model plugs into

For the artist making the final chalker, and for whoever wires the model in. The chalker on screen today is built
from simple shapes in code ([Chalker.cs](../../unity/Assets/_Project/Scripts/View/Chalkers/Chalker.cs)). Nothing
outside that file knows about the shapes. The pose, the face and the emotes drive named points, and a model that binds
its bones to the same points replaces the shapes without touching the rules, the network or the emotes.

## Size and placement

Units are metres. The chalker's origin is on the edge of the table, at table-top height, facing the table (+Z forward,
+Y up). The room's floor is 0.76 m below the table top.

| Measure | Value | Where it comes from |
|---|---|---|
| Chair seat (cushion top) | 0.30 m below the table top | `ChalkerChair.SeatTop` |
| Distance from the table's edge | 0.22 m | `ChalkersView.SeatDistance` |
| Torso pivot above the origin | 0.14 m | `Chalker.TorsoHeight` |
| Eyes above the origin | 0.53 m | `Chalker.EyeHeight` (the camera from your seat sits here) |
| Shoulders, upper arm, forearm | 0.06 m above the torso pivot, 0.17 m, 0.17 m | arm lengths that reach the table at rest |
| Knees forward of the hips | 0.24 m | `Chalker.KneeForward` |

## The named points

`Chalker.Points` maps each name to a transform. The model's bone of the same name follows it, or the model is
parented to it.

| Point | Moves with | Driven by |
|---|---|---|
| `root` | the seat | where the seat's chair is, facing the table |
| `torso` | lean, sway, bob, asleep | emotes (`BodyPose.Lean`, `Sway`, `Bob`), eyes-closed phases |
| `neck` | yaw and pitch toward what the player looks at | presence gaze |
| `head` | nods, shakes, tilts, the head dropping asleep | emotes (`BodyPose.Head`), eyes-closed phases |
| `face` | the chalkboard | a 256 × 256 texture redrawn when the expression changes (`ChalkerFace`) |
| `mouth` | the voice's position | voice playback comes from here |
| `leftShoulder`, `rightShoulder` | arm raise and forward swing | emotes, rest pose (`ChalkerEmotes.Rest`: forearms on the table) |
| `leftElbow`, `rightElbow` | elbow bend | emotes, rest pose |
| `leftHand`, `rightHand` | the hands | follow the elbows; tests check the right hand reaches the table |

Rotations are local Euler angles on those transforms, so a rig whose bones share the axes (+Z forward, +Y up, arms
hanging down −Y at zero) works as it is.

## The face

The chalkboard is a texture, not geometry: eyes (each one open, closed or winking on its own), brows, mouth, pupils
that follow the pointer, and extras (sparkles, hearts, dots) are drawn into it from code. The model needs one material
slot for the board, UV-mapped to the whole 0–1 square, in which the code sets `_BaseMap` to the face texture. The board
should read well at the size it has today: about 0.27 × 0.23 m.

## What stays in code

Personalities, emotes, blinking, winks, sleeping and the mouth following the voice are code. They change the points
and the face texture, so they keep working on any model bound to the points. A new emote is a new `BodyPose` and
`FaceShape` in [ChalkerEmotes.cs](../../unity/Assets/_Project/Scripts/View/Chalkers/ChalkerEmotes.cs), whatever the model.

Try any change in the ChalkerLab scene (`Table World/Build Chalker Lab Scene`): one chalker that talks with your
microphone and looks at the mouse. Keys 1–0 and − play emotes, H holds one, W winks and Z closes the eyes.
