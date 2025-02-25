﻿using Microsoft.AspNetCore.Components;
using WanderLost.Client.Services;
using WanderLost.Shared;
using WanderLost.Shared.Data;

namespace WanderLost.Client.Pages
{
    public partial class Merchants : IAsyncDisposable
    {
        [Inject] public ClientSettingsController ClientSettings { get; init; } = default!;
        [Inject] public ClientStaticDataController StaticData { get; init; } = default!;
        [Inject] public MerchantHubClient HubClient { get; init; } = default!;
        [Inject] public ClientNotificationService Notifications { get; init; } = default!;
        [Inject] public NavigationManager NavigationManager { get; init; } = default!;

        private string? _serverRegion;
        private string? ServerRegion
        {
            get { return _serverRegion; }
            set
            {
                if (_serverRegion != value)
                {
                    _serverRegion = value;
                    Server = null;
                    Task.Run(() => ClientSettings.SetRegion(_serverRegion ?? string.Empty));
                }
            }
        }

        private string? _server;
        private string? Server
        {
            get { return _server; }
            set
            {
                if (_server != value)
                {
                    var oldValue = _server;
                    _server = value;
                    Task.Run(() => ClientSettings.SetServer(_server ?? string.Empty));
                    Task.Run(() => ServerChanged(oldValue));
                }
            }
        }

        private List<ActiveMerchantGroup> _activeMerchantGroups = new();
        private readonly Dictionary<Guid, ActiveMerchant> _activeMerchantDictionary = new();
        private Timer? _timer;
        private readonly List<IDisposable> _hubEvents = new();

        protected override async Task OnInitializedAsync()
        {
            await StaticData.Init();
            await ClientSettings.Init();

            _activeMerchantGroups = StaticData.Merchants.Values.Select(m => new ActiveMerchantGroup() { MerchantData = m }).ToList();

            _timer = new Timer(TimerTick, null, 1, 1000);

            ServerRegion = ClientSettings.Region;
            Server = ClientSettings.Server;

            _hubEvents.Add(HubClient.OnUpdateMerchantGroup(async (server, serverMerchantGroup) =>
            {
                if (Server != server) return;
                
                if (_activeMerchantGroups.FirstOrDefault(m => m.MerchantName == serverMerchantGroup.MerchantName) is ActiveMerchantGroup clientGroup)
                {
                    foreach (var merchant in serverMerchantGroup.ActiveMerchants)
                    {
                        if (_activeMerchantDictionary.TryAdd(merchant.Id, merchant))
                        {
                            //Only need to notify/process new merchants
                            clientGroup.ActiveMerchants.Add(merchant);
                            await Notifications.RequestMerchantFoundNotification(clientGroup);
                        }
                    }
                }

                await InvokeAsync(StateHasChanged);
            }));

            _hubEvents.Add(HubClient.OnUpdateVoteTotal(async (merchantId, voteTotal) =>
            {
                if(_activeMerchantDictionary.TryGetValue(merchantId, out var merchant))
                {
                    merchant.Votes = voteTotal;
                }
                await InvokeAsync(StateHasChanged);
            }));

            HubClient.HubConnection.Reconnected += HubConnection_Reconnected;

            if (HubClient.HubConnection.State == Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Disconnected)
            {
                await HubClient.HubConnection.StartAsync();
            }
        }

        private async Task HubConnection_Reconnected(string? arg)
        {
            if(await HubClient.HasNewerClient(Utils.ClientVersion))
            {
                //Force client to reload to match server
                NavigationManager.NavigateTo("", true);
                return;
            }
            await SynchronizeServer();
        }

        public async ValueTask DisposeAsync()
        {
            if (_timer is not null)
            {
                await _timer.DisposeAsync();
            }

            foreach (var hubEvent in _hubEvents)
            {
                hubEvent.Dispose();
            }
            _hubEvents.Clear();

            HubClient.HubConnection.Reconnected -= HubConnection_Reconnected;

            GC.SuppressFinalize(this);
        }

        private async Task ServerChanged(string? oldServer)
        {
            if (!string.IsNullOrWhiteSpace(oldServer))
            {
                await HubClient.UnsubscribeFromServer(oldServer);
            }
            if (!string.IsNullOrWhiteSpace(Server) && !string.IsNullOrWhiteSpace(ServerRegion))
            {
                await HubClient.SubscribeToServer(Server);
                await SynchronizeServer();
            }
        }

        private async Task SynchronizeServer()
        {
            //Sync with the server's current data
            _activeMerchantDictionary.Clear();

            if (!string.IsNullOrWhiteSpace(Server))
            {
                foreach (var serverMerchantGroup in await HubClient.GetKnownActiveMerchantGroups(Server))
                {
                    if (_activeMerchantGroups.FirstOrDefault(mg => mg.MerchantName == serverMerchantGroup.MerchantName) is ActiveMerchantGroup clientGroup)
                    {
                        foreach (var merchant in serverMerchantGroup.ActiveMerchants)
                        {
                            if (_activeMerchantDictionary.TryAdd(merchant.Id, merchant))
                            {
                                //Only need to notify/process new merchants
                                clientGroup.ActiveMerchants.Add(merchant);
                                await Notifications.RequestMerchantFoundNotification(clientGroup);
                            }
                        }
                    }
                }
            }
            StateHasChanged();
        }

        async void TimerTick(object? _)
        {
            await UpdateMerchants();
            await InvokeAsync(StateHasChanged);
        }

        private async Task UpdateMerchants(bool force = false)
        {
            if (string.IsNullOrWhiteSpace(_serverRegion)) return;
            if (_activeMerchantGroups.Count == 0) return;

            bool resort = false;

            foreach (var merchantGroup in _activeMerchantGroups)
            {
                if (force || merchantGroup.AppearanceExpires < DateTimeOffset.UtcNow)
                {
                    merchantGroup.CalculateNextAppearance(StaticData.ServerRegions[_serverRegion].UtcOffset);
                    merchantGroup.ClearInstances();
                    _activeMerchantDictionary.Clear();
                    resort = true;
                }
            }

            //Notify appearance of merchants who are 1 second away from spawning.
            foreach (var merchantGroup in _activeMerchantGroups.Where(x => !x.IsActive && x.NextAppearance < (DateTimeOffset.UtcNow.AddSeconds(1))))
            {
                await Notifications.RequestMerchantSpawnNotification(merchantGroup);
            }

            if (resort)
            {
                _activeMerchantGroups = _activeMerchantGroups.OrderBy(m => m.NextAppearance).ThenBy(m => m.MerchantData.Region).ToList();
            }
        }
    }
}
