using System.Threading.Tasks;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

// Thin wrapper around Unity Gaming Services sign-in + Relay allocation calls, kept separate
// from NetworkManager so the lockstep netcode itself doesn't need to know about UGS beyond
// the RelayServerData it hands back. Driven by devconsole `net-start-server -relay` /
// `net-connect -relay` (see NetworkManager.RegisterCommands).
public static class RelayNetworkService
{
    static Task _signInTask;

    public static async Task EnsureSignedInAsync()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
            await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            // Two commands issued back to back (e.g. a retried command) would otherwise both
            // call SignInAnonymouslyAsync concurrently; share one in-flight task instead.
            _signInTask ??= AuthenticationService.Instance.SignInAnonymouslyAsync();
            await _signInTask;
            _signInTask = null;
        }
    }

    // Host side: allocate a Relay server slot and mint a join code for it. connectionType
    // "dtls" encrypts the relayed traffic; region is a Unity region id (e.g. "europe-west3")
    // or null to let Relay pick the lowest-latency region for the host.
    public static async Task<(string joinCode, RelayServerData serverData)> CreateHostAllocationAsync(int maxConnections, string region)
    {
        await EnsureSignedInAsync();
        Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections, region);
        string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
        return (joinCode, new RelayServerData(allocation, "dtls"));
    }

    // Client side: resolve a join code (from CreateHostAllocationAsync) to the same Relay
    // allocation the host is bound to.
    public static async Task<RelayServerData> JoinAllocationAsync(string joinCode)
    {
        await EnsureSignedInAsync();
        JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
        return new RelayServerData(allocation, "dtls");
    }
}
