using UnityEngine;

public class TestInput : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
            InputBuffer.EnqueueInput(new SpawnEntityInput());

        float dirX = Input.GetAxisRaw("Horizontal");
        float dirY = Input.GetAxisRaw("Vertical");
        if (dirX != 0f || dirY != 0f)
            InputBuffer.EnqueueInput(new MoveInput { DirX = dirX, DirY = dirY });
    }
}