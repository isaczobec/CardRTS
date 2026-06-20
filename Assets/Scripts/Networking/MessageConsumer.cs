using System.Text;
using UnityEngine;

public class MessageConsumer : MonoBehaviour
{
    void Update()
    {
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
                case MessageType.GameStart:
                    NetworkManager.instance.OnGameStart(msg.Data);
                    break;
                case MessageType.ClientReady:
                    if (NetworkManager.instance.IsServer)
                        NetworkManager.instance.NotifyClientReady();
                    break;
                case MessageType.SimulationDelta:
                    NetworkManager.instance.OnSimulationDelta(msg.Data);
                    break;
                case MessageType.GameReady:
                    if (NetworkManager.instance.IsClient)
                        NetworkManager.instance.OnGameReady();
                    break;
                case MessageType.PlayerIdAssigned:
                    if (NetworkManager.instance.IsClient)
                    {
                        ushort playerId = System.BitConverter.ToUInt16(msg.Data, 1);
                        NetworkManager.instance.OnPlayerIdAssigned(playerId);
                    }
                    break;
                case MessageType.ClientInput:
                    if (NetworkManager.instance.IsServer)
                        NetworkManager.instance.OnClientInput(msg);
                    break;
                case MessageType.ClientTickInput:
                    if (NetworkManager.instance.IsServer)
                        NetworkManager.instance.OnClientTickInput(msg);
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

        if (NetworkManager.instance.IsServer && msg.SenderId != 0)
            NetworkManager.instance.SendToAll(msg.Data);
    }
}
