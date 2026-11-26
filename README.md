# Match-3 VFX Portfolio

A Unity portfolio project demonstrating real-time visual effects for a match-3 board. The playable scene shows how each effect reads at game scale, from the first flash to the end of a booster chain.

[Download the showreel](https://github.com/mihanchik42/vfx_portofolio/releases/tag/showreel)

## Effects

| Effect | In-game trigger |
| --- | --- |
| Match pop | Match three or more tiles to see color-matched particles. |
| Bomb | Create an intersecting match. Activating the bomb clears the surrounding 3×3 area. |
| Paper plane | Match a 2×2 square. On takeoff, the plane clears the four adjacent cells and flies to another target. |
| Line rocket | Match four tiles horizontally or vertically. The rocket splits and clears the row or column in both directions. |
| Booster chain | A booster hit by another booster activates in turn. |

The match pop and bomb use **Unity Visual Effect Graph**. The paper plane and line rocket use animated sprites and layered **Trail Renderers**. The board and booster rules are implemented in C#.

## Run the demo

1. Install **Unity 6000.3.10f1** and add this folder in Unity Hub.
2. Open `Assets/Scenes/Match3Demo.unity` and enter Play Mode.
3. Drag between adjacent tiles to swap them. Click a booster to activate it.

| Key | Demo shortcut |
| --- | --- |
| `T` | Toggle slow motion. |
| `O` | Set up the 2×2 paper-plane match. |
| `N` | Place a bomb under the pointer. |
| `H` / `V` | Place a horizontal or vertical line rocket under the pointer. |
