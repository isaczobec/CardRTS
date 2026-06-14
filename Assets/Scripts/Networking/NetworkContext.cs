public class NetworkContext
{
    public NetworkRole Role { get; private set; } = NetworkRole.Standalone;

    public bool IsServer     => (Role & NetworkRole.Server) != 0;
    public bool IsClient     => (Role & NetworkRole.Client) != 0;
    public bool IsHost       => Role == NetworkRole.Host;
    public bool IsStandalone => Role == NetworkRole.Standalone;

    internal void AddRole(NetworkRole role)    => Role |= role;
    internal void RemoveRole(NetworkRole role) => Role &= ~role;
}
