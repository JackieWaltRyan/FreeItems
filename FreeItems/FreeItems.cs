using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Web.Responses;

namespace FreeItems;

internal sealed class FreeItems : IGitHubPluginUpdates, IBotModules {
    public string Name => nameof(FreeItems);
    public string RepositoryName => "JackieWaltRyan/FreeItems";
    public Version Version => typeof(FreeItems).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version));

    public Dictionary<string, bool> PointStoreEnable = new();
    public Dictionary<string, bool> RecommendationsEnable = new();

    public Dictionary<string, uint> FreeItemsTimeout = new();

    public Dictionary<string, Timer> PointStoreTimers = new();
    public Dictionary<string, Timer> RecommendationsTimers = new();

    public Task OnLoaded() => Task.CompletedTask;

    public async Task OnBotInitModules(Bot bot, IReadOnlyDictionary<string, JsonElement>? additionalConfigProperties = null) {
        if (additionalConfigProperties != null) {
            PointStoreEnable[bot.BotName] = false;
            RecommendationsEnable[bot.BotName] = false;

            FreeItemsTimeout[bot.BotName] = 6;

            if (PointStoreTimers.TryGetValue(bot.BotName, out Timer? pointstoretimer)) {
                await pointstoretimer.DisposeAsync().ConfigureAwait(false);

                bot.ArchiLogger.LogGenericInfo("ClaimPointStoreItems Dispose.");
            }

            if (RecommendationsTimers.TryGetValue(bot.BotName, out Timer? recommendationstimer)) {
                await recommendationstimer.DisposeAsync().ConfigureAwait(false);

                bot.ArchiLogger.LogGenericInfo("ClaimRecommendationsItems Dispose.");
            }

            PointStoreTimers[bot.BotName] = new Timer(async e => await ClaimPointStoreItems(bot).ConfigureAwait(false), null, Timeout.Infinite, Timeout.Infinite);
            RecommendationsTimers[bot.BotName] = new Timer(async e => await ClaimRecommendationsItems(bot).ConfigureAwait(false), null, Timeout.Infinite, Timeout.Infinite);

            foreach (KeyValuePair<string, JsonElement> configProperty in additionalConfigProperties) {
                switch (configProperty.Key) {
                    case "FreePointStoreItems" when configProperty.Value.ValueKind is JsonValueKind.True or JsonValueKind.False: {
                        bool isEnabled = configProperty.Value.GetBoolean();

                        bot.ArchiLogger.LogGenericInfo($"FreePointStoreItems: {isEnabled}");

                        PointStoreEnable[bot.BotName] = isEnabled;

                        break;
                    }

                    case "FreeRecommendationsItems" when configProperty.Value.ValueKind is JsonValueKind.True or JsonValueKind.False: {
                        bool isEnabled = configProperty.Value.GetBoolean();

                        bot.ArchiLogger.LogGenericInfo($"FreeRecommendationsItems: {isEnabled}");

                        RecommendationsEnable[bot.BotName] = isEnabled;

                        break;
                    }

                    case "FreeItemsTimeout" when configProperty.Value.ValueKind == JsonValueKind.Number: {
                        FreeItemsTimeout[bot.BotName] = configProperty.Value.ToJsonObject<uint>();

                        break;
                    }
                }
            }

            if (PointStoreEnable[bot.BotName]) {
                PointStoreTimers[bot.BotName].Change(1, -1);
            }

            if (RecommendationsEnable[bot.BotName]) {
                RecommendationsTimers[bot.BotName].Change(1, -1);
            }
        }
    }

    public async Task<List<uint>> LoadPointStoreItems(Bot bot, int count = 0, string? cursor = null) {
        try {
            List<uint> pointList = [];

            if (!bot.IsConnectedAndLoggedOn || !PointStoreTimers.ContainsKey(bot.BotName)) {
                return pointList;
            }

            string url = $"https://api.steampowered.com/ILoyaltyRewardsService/QueryRewardItems/v1/?access_token={bot.AccessToken}&count=1000";

            if (cursor != null) {
                url += $"&cursor={cursor}";
            }

            ObjectResponse<QueryRewardItemsResponse>? rawResponse = await bot.ArchiWebHandler.UrlGetToJsonObjectWithSession<QueryRewardItemsResponse>(new Uri(url)).ConfigureAwait(false);

            QueryRewardItemsResponse.ResponseData? response = rawResponse?.Content?.Response;

            if (response != null) {
                if (response.Definitions != null) {
                    count += response.Definitions.Count;

                    bot.ArchiLogger.LogGenericInfo($"Load all points: {count}/{response.TotalCount}");

                    foreach (QueryRewardItemsResponse.ResponseData.RewardItemData item in response.Definitions) {
                        if (item.PointCost == 0) {
                            pointList.Add(item.DefId);
                        }
                    }

                    if (count >= response.TotalCount) {
                        return pointList;
                    }

                    List<uint> newPointList = await LoadPointStoreItems(bot, count, response.Cursor).ConfigureAwait(false);

                    pointList.AddRange(newPointList);
                } else {
                    await Task.Delay(3000).ConfigureAwait(false);

                    await LoadPointStoreItems(bot, count, cursor).ConfigureAwait(false);
                }
            } else {
                await Task.Delay(3000).ConfigureAwait(false);

                await LoadPointStoreItems(bot, count, cursor).ConfigureAwait(false);
            }

            return pointList;
        } catch {
            await Task.Delay(3000).ConfigureAwait(false);

            return await LoadPointStoreItems(bot, count, cursor).ConfigureAwait(false);
        }
    }

    public async Task ClaimPointStoreItems(Bot bot) {
        if (bot.IsConnectedAndLoggedOn) {
            List<uint> freePoints = await LoadPointStoreItems(bot).ConfigureAwait(false);

            bot.ArchiLogger.LogGenericInfo($"Free points found: {freePoints.Count}");

            if (freePoints.Count > 0) {
                foreach (uint pointId in freePoints) {
                    ObjectResponse<JsonElement>? rawResponse = await bot.ArchiWebHandler.UrlPostToJsonObjectWithSession<JsonElement>(
                        new Uri("https://api.steampowered.com/ILoyaltyRewardsService/RedeemPoints/v1/"), data: new Dictionary<string, string>(3) {
                            { "access_token", bot.AccessToken ?? string.Empty },
                            { "defid", $"{pointId}" }
                        }
                    ).ConfigureAwait(false);

                    JsonElement? response = rawResponse?.Content;

                    bot.ArchiLogger.LogGenericInfo(response.ToJsonText());
                }
            }

            bot.ArchiLogger.LogGenericInfo($"Status: QueueIsEmpty | Next run: {DateTime.Now.AddHours(FreeItemsTimeout[bot.BotName]):T}");

            PointStoreTimers[bot.BotName].Change(TimeSpan.FromHours(FreeItemsTimeout[bot.BotName]), TimeSpan.FromMilliseconds(-1));
        } else {
            bot.ArchiLogger.LogGenericInfo($"Status: BotNotConnected | Next run: {DateTime.Now.AddMinutes(1):T}");

            PointStoreTimers[bot.BotName].Change(TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(-1));
        }
    }

    public async Task ClaimRecommendationsItems(Bot bot) {
        if (bot.IsConnectedAndLoggedOn) {
            ObjectResponse<GetDiscoveryQueueResponse>? rawResponse = await bot.ArchiWebHandler.UrlGetToJsonObjectWithSession<GetDiscoveryQueueResponse>(new Uri($"https://api.steampowered.com/IStoreService/GetDiscoveryQueue/v1/?access_token={bot.AccessToken}&queue_type=0&country_code=EU&rebuild_queue=true&ignore_user_preferences=true")).ConfigureAwait(false);

            List<int>? games = rawResponse?.Content?.Response?.AppIds;

            if ((games != null) && (games.Count > 0)) {
                int count = 12;

                ObjectResponse<JsonElement>? response = await bot.ArchiWebHandler.UrlPostToJsonObjectWithSession<JsonElement>(
                    new Uri("https://api.steampowered.com/IStoreService/SkipDiscoveryQueueItem/v1/"), data: new Dictionary<string, string>(3) {
                        { "access_token", bot.AccessToken ?? string.Empty },
                        { "appid", $"{games[0]}" }
                    }
                ).ConfigureAwait(false);

                count -= 1;

                if (response?.StatusCode == HttpStatusCode.OK) {
                    bot.ArchiLogger.LogGenericInfo($"ID: {games[0]} | Status: OK | Queue: {count}");
                } else {
                    bot.ArchiLogger.LogGenericInfo($"ID: {games[0]} | Status: Error | Queue: {count} | Next run: {DateTime.Now.AddMinutes(1):T}");

                    RecommendationsTimers[bot.BotName].Change(TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(-1));

                    return;
                }

                bot.ArchiLogger.LogGenericInfo($"Status: QueueIsEmpty | Next run: {DateTime.Now.AddHours(FreeItemsTimeout[bot.BotName]):T}");

                RecommendationsTimers[bot.BotName].Change(TimeSpan.FromHours(FreeItemsTimeout[bot.BotName]), TimeSpan.FromMilliseconds(-1));

                return;
            }

            bot.ArchiLogger.LogGenericInfo($"Status: Error | Next run: {DateTime.Now.AddMinutes(1):T}");
        } else {
            bot.ArchiLogger.LogGenericInfo($"Status: BotNotConnected | Next run: {DateTime.Now.AddMinutes(1):T}");
        }

        RecommendationsTimers[bot.BotName].Change(TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(-1));
    }
}
