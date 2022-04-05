using Microsoft.AspNetCore.SignalR.Client;
using WanderLost.Shared.Data;
using WanderLost.Shared.Interfaces;
using HubClientSourceGenerator;

namespace WanderLost.Client.Services
{
    //Implements client for incoming calls, and server to act as a proxy for outgoing calls
    [AutoHubClient(typeof(IMerchantHubClient))]
    //[AutoHubClient(typeof(IMerchantHubClient))]
    public partial class MerchantHubClient : IMerchantHubServer, IAsyncDisposable
    {
        public HubConnection HubConnection {get; init; }

        public MerchantHubClient(IConfiguration configuration)
        {
            HubConnection = new HubConnectionBuilder()
                .WithUrl(configuration["SocketEndpoint"])
                .WithAutomaticReconnect(new[] {
                    //Stargger reconnections a bit so server doesn't get hammered after a restart
                    TimeSpan.FromSeconds(Random.Shared.Next(5,120)),
                    TimeSpan.FromSeconds(Random.Shared.Next(10,120)),
                    TimeSpan.FromSeconds(Random.Shared.Next(30,120)), 
                    TimeSpan.FromMinutes(5),
                    TimeSpan.FromMinutes(5),
                })
                .Build();
            HubConnection.ServerTimeout = TimeSpan.FromMinutes(2);
            HubConnection.KeepAliveInterval = TimeSpan.FromMinutes(1);
        }

        public async ValueTask DisposeAsync()
        {
            if(HubConnection is not null)
            {
                await HubConnection.DisposeAsync();
            }
            GC.SuppressFinalize(this);
        }

        public async Task SubscribeToServer(string server)
        {
            await HubConnection.SendAsync(nameof(SubscribeToServer), server);
        }

        public async Task UnsubscribeFromServer(string server)
        {
            await HubConnection.SendAsync(nameof(UnsubscribeFromServer), server);
        }

        public async Task UpdateMerchant(string server, ActiveMerchant merchant)
        {
            await HubConnection.SendAsync(nameof(UpdateMerchant), server, merchant);
        }

        public IDisposable OnUpdateMerchant(Action<string, ActiveMerchant> action)
        {
            return HubConnection.On(nameof(UpdateMerchant), action);
        }

        public async Task<IEnumerable<ActiveMerchantGroup>> GetKnownActiveMerchantGroups(string server)
        {
            return await HubConnection.InvokeAsync<IEnumerable<ActiveMerchantGroup>>(nameof(GetKnownActiveMerchantGroups), server);
        }

        public async Task Vote(string server, Guid merchantId, VoteType voteType)
        {
            await HubConnection.SendAsync(nameof(Vote), server, merchantId, voteType);
        }
    }
}
