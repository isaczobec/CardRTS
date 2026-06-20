public readonly struct InboundMessage
{
    public readonly ushort SenderId;
    public readonly byte[] Data;

    public InboundMessage(ushort senderId, byte[] data)
    {
        SenderId = senderId;
        Data = data;
    }
}
