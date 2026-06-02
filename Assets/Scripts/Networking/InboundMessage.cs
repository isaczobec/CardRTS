public readonly struct InboundMessage
{
    public readonly int SenderId;
    public readonly byte[] Data;

    public InboundMessage(int senderId, byte[] data)
    {
        SenderId = senderId;
        Data = data;
    }
}
