# Desktop game movement

**F2** enters/exits game control; **Esc** releases it. WASD moves relative to the game camera, Shift runs, Space jumps, and Ctrl/C crouches. VR continues to prioritize its tracking sources.

In **Movimento**, tune speed, run multiplier, jump height, acceleration, braking and **Ritmo dos passos** (step rate). Start with the defaults. Step cadence adapts to selected movement speed and avatar height; the rate slider provides additional adjustment for different rigs. The motor publishes actual collision-constrained velocity, so pressing against a wall does not sustain the running gait.

The desktop controller blends idle/walk/run, crouch movement, jump ascent, falling and landing. An official Unity running-jump pose is blended with the standing jump according to momentum. Landing recovery releases into movement quickly rather than holding a stationary landing clip while the character slides forward. Short jump buffering (120 ms) and ledge grace (100 ms) make input near landing/edges more forgiving; they do not enable an extra air jump.

Crouch remains locally authored using a reference Humanoid rig. Its cycle, stride and arm opposition are matched more closely to movement. Muscle-only crouch clips do not have foot IK goals and must not use the imported-clip foot IK option: absent goals can pull the feet up into the body. This is not a terrain foot-placement solver. Animation fidelity and residual sliding depend on proportions and avatar setup.

The game controller temporarily replaces the Base layer and pauses other body pose layers; it preserves FX and restores the imported Base when game control ends. Prefab yaw offsets are compensated when turning toward movement. File formats and existing tracking profiles are unchanged.

## Validation

`--locomotion-test` exercises start/stop acceleration, custom walking speed/cadence, direction reversal, walk/run/crouch, grounded foot height, jump/fall/landing, moving jump recovery, a rotated avatar root, ledge grace, buffered jump and wall blocking. `--default-locomotion` also checks an avatar without an imported Base. `--locomotion-photos DIRECTORY` captures private GPU-rendered inspection images; these are intentionally not published with the source.

For a manual check, walk and stop, switch to running, reverse direction, crouch while walking, jump while running, and then release game control. Check the pose from the side as well as the follow camera. Test with your own avatar; a passing synthetic-input test does not establish every rig's appearance or hardware tracking accuracy.
