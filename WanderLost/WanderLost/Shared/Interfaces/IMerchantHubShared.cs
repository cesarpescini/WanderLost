﻿using WanderLost.Shared.Data;

namespace WanderLost.Shared.Interfaces
{
    public interface IMerchantHubShared
    {
        Task UpdateMerchant(string server, ActiveMerchant merchant);
    }
}
