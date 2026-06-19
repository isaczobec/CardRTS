using UnityEngine;

public class TestInput : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            var input = new SpawnEntityInput();
            InputBuffer.EnqueueInput(input);
            Debug.Log($"Enqueued SpawnEntityInput for tick {TickManager.instance.Tick}");
        }
    }
}