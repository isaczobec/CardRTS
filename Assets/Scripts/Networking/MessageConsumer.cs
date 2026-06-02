using System.Text;
using UnityEngine;

public class MessageConsumer : MonoBehaviour
{
    [SerializeField] float _tickInterval = 0.5f;

    float _timer;

    void Update()
    {
        _timer += Time.deltaTime;
        if (_timer < _tickInterval) return;
        _timer = 0f;

        if (NetworkManager.instance == null) return;

        var queue = NetworkManager.instance.InboundQueue;
        while (queue.Count > 0)
        {
            var msg = queue.Dequeue();
            if (msg.Data.Length == 0) continue;

            var type = (MessageType)msg.Data[0];
            switch (type)
            {
                case MessageType.TestHello:
                    HandleTestHello(msg);
                    break;
                default:
                    DevConsole.LogWarning($"[Net] Unhandled message type: {(byte)type}");
                    break;
            }
        }
    }

    void HandleTestHello(InboundMessage msg)
    {
        string text = Encoding.UTF8.GetString(msg.Data, 1, msg.Data.Length - 1);
        string sender = msg.SenderId == 0 ? "server" : $"client {msg.SenderId}";
        DevConsole.LogInfo($"[Net] Hello from {sender}: \"{text}\"");

        // Only relay if this arrived from a client — if SenderId is 0 it already came from the server
        if (NetworkManager.instance.IsServer && msg.SenderId != 0)
            NetworkManager.instance.SendToAll(msg.Data);
    }
}
