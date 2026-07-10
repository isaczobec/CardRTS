using UnityEngine;

public class TestInput : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
            InputBuffer.EnqueueInput(new SpawnEntityInput());

        if (Input.GetKeyDown(KeyCode.X) && TileSpaceMouse.TryGetPosition(out float tx, out float ty))
            InputBuffer.EnqueueInput(new SpawnTroopInput { X = tx, Y = ty, TroopType = TroopType.BasicMelee });

        if (Input.GetKeyDown(KeyCode.C) && TileSpaceMouse.TryGetPosition(out float cx, out float cy))
            InputBuffer.EnqueueInput(new SpawnTroopInput { X = cx, Y = cy, TroopType = TroopType.BasicRanged });

        if (Input.GetKeyDown(KeyCode.V) && TileSpaceMouse.TryGetPosition(out float vx, out float vy))
            InputBuffer.EnqueueInput(new SpawnTroopInput { X = vx, Y = vy, TroopType = TroopType.Building });

        // Right-click (move / set targets) is handled by SelectionManager.

        float dirX = Input.GetAxisRaw("Horizontal");
        float dirY = Input.GetAxisRaw("Vertical");
        if (dirX != 0f || dirY != 0f)
            InputBuffer.EnqueueInput(new MoveInput { DirX = dirX, DirY = dirY });
    }
}