using System.Collections.Generic;
using UnityEngine;

public class TestInput : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
            InputBuffer.EnqueueInput(new SpawnEntityInput());

        if (Input.GetKeyDown(KeyCode.X) && TileSpaceMouse.TryGetPosition(out float tx, out float ty))
            InputBuffer.EnqueueInput(new SpawnTroopInput { X = tx, Y = ty });

        if (Input.GetMouseButtonDown(1) && !Input.GetKey(KeyCode.Space) && SelectionManager.instance != null
            && TileSpaceMouse.TryGetPosition(out float tx1, out float ty1))
        {
            List<MoveTroopInput.EntityDestination> moves = new List<MoveTroopInput.EntityDestination>();
            foreach (ulong entityId in SelectionManager.instance.SelectedEntityIds)
            {
                moves.Add(new MoveTroopInput.EntityDestination
                {
                    EntityId     = entityId,
                    DestinationX = tx1,
                    DestinationY = ty1,
                });
            }
            if (moves.Count > 0)
                InputBuffer.EnqueueInput(new MoveTroopInput { Moves = moves });
        }

        float dirX = Input.GetAxisRaw("Horizontal");
        float dirY = Input.GetAxisRaw("Vertical");
        if (dirX != 0f || dirY != 0f)
            InputBuffer.EnqueueInput(new MoveInput { DirX = dirX, DirY = dirY });
    }
}