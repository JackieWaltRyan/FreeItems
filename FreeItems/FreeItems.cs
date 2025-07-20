using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Integration;
using ArchiSteamFarm.Web.Responses;

namespace FreeItems;

internal sealed class FreeItems : IGitHubPluginUpdates, IBotModules {
    public string Name => nameof(FreeItems);
    public string RepositoryName => "JackieWaltRyan/FreeItems";
    public Version Version => typeof(FreeItems).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version));

    public Dictionary<string, FreeItemsConfig> FreeItemsConfig = new();
    public Dictionary<string, Dictionary<string, Timer>> FreeItemsTimers = new();

    public Task OnLoaded() => Task.CompletedTask;

    public async Task OnBotInitModules(Bot bot, IReadOnlyDictionary<string, JsonElement>? additionalConfigProperties = null) {
        if (additionalConfigProperties != null) {
            if (FreeItemsTimers.TryGetValue(bot.BotName, out Dictionary<string, Timer>? dict)) {
                foreach (KeyValuePair<string, Timer> timers in dict) {
                    switch (timers.Key) {
                        case "ClaimPointStoreItems": {
                            await timers.Value.DisposeAsync().ConfigureAwait(false);

                            bot.ArchiLogger.LogGenericInfo("ClaimPointStoreItems Dispose.");

                            break;
                        }

                        case "ClaimRecommendationsItems": {
                            await timers.Value.DisposeAsync().ConfigureAwait(false);

                            bot.ArchiLogger.LogGenericInfo("ClaimRecommendationsItems Dispose.");

                            break;
                        }

                        case "ClaimDailyStickers": {
                            await timers.Value.DisposeAsync().ConfigureAwait(false);

                            bot.ArchiLogger.LogGenericInfo("ClaimDailyStickers Dispose.");

                            break;
                        }
                    }
                }
            }

            FreeItemsTimers[bot.BotName] = new Dictionary<string, Timer> {
                { "ClaimPointStoreItems", new Timer(async e => await ClaimPointStoreItems(bot).ConfigureAwait(false), null, Timeout.Infinite, Timeout.Infinite) },
                { "ClaimRecommendationsItems", new Timer(async e => await ClaimRecommendationsItems(bot).ConfigureAwait(false), null, Timeout.Infinite, Timeout.Infinite) },
                { "ClaimDailyStickers", new Timer(async e => await ClaimDailyStickers(bot).ConfigureAwait(false), null, Timeout.Infinite, Timeout.Infinite) }
            };

            FreeItemsConfig[bot.BotName] = new FreeItemsConfig();

            foreach (KeyValuePair<string, JsonElement> configProperty in additionalConfigProperties) {
                switch (configProperty.Key) {
                    case "FreeItemsConfig": {
                        FreeItemsConfig? config = configProperty.Value.ToJsonObject<FreeItemsConfig>();

                        if (config != null) {
                            FreeItemsConfig[bot.BotName] = config;
                        }

                        break;
                    }
                }
            }

            FreeItemsConfig psc = FreeItemsConfig[bot.BotName];

            if (psc.PointStoreItems || psc.RecommendationsItems || psc.DailyStickers) {
                bot.ArchiLogger.LogGenericInfo($"FreeItemsConfig: {psc.ToJsonText()}");

                if (psc.PointStoreItems) {
                    FreeItemsTimers[bot.BotName]["ClaimPointStoreItems"].Change(1, -1);
                }

                if (psc.RecommendationsItems) {
                    FreeItemsTimers[bot.BotName]["ClaimRecommendationsItems"].Change(1, -1);
                }

                if (psc.DailyStickers) {
                    FreeItemsTimers[bot.BotName]["ClaimDailyStickers"].Change(1, -1);
                }
            }
        }
    }

    public static async Task<List<uint>> LoadPointStoreItems(Bot bot, uint? count = 0, string? cursor = null) {
        try {
            List<uint> pointList = [];

            if (!bot.IsConnectedAndLoggedOn) {
                return pointList;
            }

            string url = $"https://api.steampowered.com/ILoyaltyRewardsService/QueryRewardItems/v1/?access_token={bot.AccessToken}&count=1000&include_direct_purchase_disabled=true";

            if (cursor != null) {
                url += $"&cursor={Uri.EscapeDataString(cursor)}";
            }

            ObjectResponse<QueryRewardItemsResponse>? rawResponse = await bot.ArchiWebHandler.UrlGetToJsonObjectWithSession<QueryRewardItemsResponse>(new Uri(url)).ConfigureAwait(false);

            QueryRewardItemsResponse.ResponseData? response = rawResponse?.Content?.Response;

            if (response != null) {
                if (response.Definitions != null) {
                    if (response.TotalCount - count >= 1000) {
                        count += 1000;
                    } else {
                        count += response.TotalCount - count;
                    }

                    bot.ArchiLogger.LogGenericInfo($"Load all items: {count}/{response.TotalCount}");

                    foreach (QueryRewardItemsResponse.ResponseData.RewardItemData item in response.Definitions) {
                        if ((item.BundleDefIds == null) && (item.PointCost == "0")) {
                            pointList.Add(item.DefId);
                        }
                    }

                    if (count >= response.TotalCount) {
                        return pointList;
                    }

                    cursor = response.Cursor;

                    await Task.Delay(1000).ConfigureAwait(false);
                } else {
                    await Task.Delay(3000).ConfigureAwait(false);
                }
            } else {
                await Task.Delay(3000).ConfigureAwait(false);
            }

            pointList.AddRange(await LoadPointStoreItems(bot, count, cursor).ConfigureAwait(false));

            return pointList;
        } catch {
            await Task.Delay(3000).ConfigureAwait(false);

            return await LoadPointStoreItems(bot, count, cursor).ConfigureAwait(false);
        }
    }

    public async Task ClaimPointStoreItems(Bot bot) {
        if (bot.IsConnectedAndLoggedOn) {
            List<uint> freePoints = await LoadPointStoreItems(bot).ConfigureAwait(false);

            bot.ArchiLogger.LogGenericInfo($"Free items found: {freePoints.Count}");

            if (freePoints.Count > 0) {
                int queue = freePoints.Count;

                foreach (uint pointId in freePoints) {
                    ObjectResponse<RedeemPointsResponse>? rawResponse = await bot.ArchiWebHandler.UrlPostToJsonObjectWithSession<RedeemPointsResponse>(
                        new Uri("https://api.steampowered.com/ILoyaltyRewardsService/RedeemPoints/v1/"), data: new Dictionary<string, string>(2) {
                            { "access_token", bot.AccessToken ?? string.Empty },
                            { "defid", $"{pointId}" }
                        }, session: ArchiWebHandler.ESession.None
                    ).ConfigureAwait(false);

                    string? response = rawResponse?.Content?.Response?.CommunityItemId;

                    queue -= 1;

                    bot.ArchiLogger.LogGenericInfo(response != null ? $"ID: {pointId} | Status: OK | Queue: {queue}" : $"ID: {pointId} | Status: Error | Queue: {queue}");
                }
            }

            bot.ArchiLogger.LogGenericInfo($"Status: QueueIsEmpty | Next run: {DateTime.Now.AddHours(FreeItemsConfig[bot.BotName].Timeout):T}");

            FreeItemsTimers[bot.BotName]["ClaimPointStoreItems"].Change(TimeSpan.FromHours(FreeItemsConfig[bot.BotName].Timeout), TimeSpan.FromMilliseconds(-1));
        } else {
            bot.ArchiLogger.LogGenericInfo($"Status: BotNotConnected | Next run: {DateTime.Now.AddMinutes(1):T}");

            FreeItemsTimers[bot.BotName]["ClaimPointStoreItems"].Change(TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(-1));
        }
    }

    public async Task ClaimRecommendationsItems(Bot bot) {
        if (bot.IsConnectedAndLoggedOn) {
            ObjectResponse<List<SeasonalSalesDateResponse>>? seasonalSalesDateResponse = await bot.ArchiWebHandler.WebBrowser.UrlGetToJsonObject<List<SeasonalSalesDateResponse>>(new Uri("https://raw.githubusercontent.com/JackieWaltRyan/FreeItems/refs/heads/main/SeasonalSalesDate.json")).ConfigureAwait(false);

            List<SeasonalSalesDateResponse>? dates = seasonalSalesDateResponse?.Content;

            if (dates != null) {
                foreach (SeasonalSalesDateResponse date in dates) {
                    DateTime dateStart = DateTime.Parse(date.Start, CultureInfo.CreateSpecificCulture("ru-RU"));
                    DateTime dateEnd = DateTime.Parse(date.End, CultureInfo.CreateSpecificCulture("ru-RU"));

                    int resultStart = DateTime.Compare(dateStart, DateTime.Now);
                    int resultEnd = DateTime.Compare(DateTime.Now, dateEnd);

                    if ((resultStart > 0) || (resultEnd > 0)) {
                        if (resultStart == 1) {
                            bot.ArchiLogger.LogGenericInfo($"Next Seasonal Sale: {dateStart:D}");
                        }

                        continue;
                    }

                    uint queue = 36;

                    for (uint i = 1; i <= 3; i++) {
                        ObjectResponse<GetDiscoveryQueueResponse>? rawResponse = await bot.ArchiWebHandler.UrlGetToJsonObjectWithSession<GetDiscoveryQueueResponse>(new Uri($"https://api.steampowered.com/IStoreService/GetDiscoveryQueue/v1/?access_token={bot.AccessToken}&queue_type=0&country_code=EU&rebuild_queue=true&ignore_user_preferences=true")).ConfigureAwait(false);

                        List<uint>? games = rawResponse?.Content?.Response?.AppIds;

                        if ((games != null) && (games.Count > 0)) {
                            foreach (uint gameId in games) {
                                bool response = await bot.ArchiWebHandler.UrlPostWithSession(
                                    new Uri("https://api.steampowered.com/IStoreService/SkipDiscoveryQueueItem/v1/"), data: new Dictionary<string, string>(2) {
                                        { "access_token", bot.AccessToken ?? string.Empty },
                                        { "appid", $"{gameId}" }
                                    }, session: ArchiWebHandler.ESession.None
                                ).ConfigureAwait(false);

                                queue -= 1;

                                if (response) {
                                    bot.ArchiLogger.LogGenericInfo($"ID: {gameId} | Status: OK | Queue: {queue}");
                                } else {
                                    bot.ArchiLogger.LogGenericInfo($"ID: {gameId} | Status: Error | Queue: {queue} | Next run: {DateTime.Now.AddMinutes(1):T}");

                                    FreeItemsTimers[bot.BotName]["ClaimRecommendationsItems"].Change(TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(-1));

                                    return;
                                }
                            }
                        } else {
                            bot.ArchiLogger.LogGenericInfo($"Status: Error | Next run: {DateTime.Now.AddMinutes(1):T}");

                            FreeItemsTimers[bot.BotName]["ClaimRecommendationsItems"].Change(TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(-1));

                            return;
                        }
                    }

                    if (queue != 0) {
                        continue;
                    }

                    bot.ArchiLogger.LogGenericInfo($"Status: QueueIsEmpty | Next run: {DateTime.Now.AddHours(24):T}");

                    FreeItemsTimers[bot.BotName]["ClaimRecommendationsItems"].Change(TimeSpan.FromHours(24), TimeSpan.FromMilliseconds(-1));

                    return;
                }

                bot.ArchiLogger.LogGenericInfo($"Status: NoItemToClaim | Next run: {DateTime.Now.AddHours(FreeItemsConfig[bot.BotName].Timeout):T}");

                FreeItemsTimers[bot.BotName]["ClaimRecommendationsItems"].Change(TimeSpan.FromHours(FreeItemsConfig[bot.BotName].Timeout), TimeSpan.FromMilliseconds(-1));

                return;
            }

            bot.ArchiLogger.LogGenericInfo($"Status: Error | Next run: {DateTime.Now.AddMinutes(1):T}");
        } else {
            bot.ArchiLogger.LogGenericInfo($"Status: BotNotConnected | Next run: {DateTime.Now.AddMinutes(1):T}");
        }

        FreeItemsTimers[bot.BotName]["ClaimRecommendationsItems"].Change(TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(-1));
    }

    public async Task ClaimDailyStickers(Bot bot) {
        if (bot.IsConnectedAndLoggedOn) {
            ObjectResponse<ClaimItemResponse>? rawResponse = await bot.ArchiWebHandler.UrlPostToJsonObjectWithSession<ClaimItemResponse>(
                new Uri("https://api.steampowered.com/ISaleItemRewardsService/ClaimItem/v1/"), data: new Dictionary<string, string>(1) {
                    { "access_token", bot.AccessToken ?? string.Empty }
                }, session: ArchiWebHandler.ESession.None
            ).ConfigureAwait(false);

            ClaimItemResponse? response = rawResponse?.Content;

            if (response != null) {
                if (response.RewardItem != null) {
                    string name = response.RewardItem.CommunityItem?.ItemName ?? response.RewardItem.CommunityItem?.ItemTitle ?? "UNKNOWN";

                    int timeout = (DateTimeOffset.FromUnixTimeSeconds(response.NextClaimTime).LocalDateTime - DateTime.Now).Minutes;

                    bot.ArchiLogger.LogGenericInfo($"ID: {response.RewardItem.DefId} {name} | Status: OK | Next run: {DateTime.Now.AddMinutes(timeout):T}");

                    FreeItemsTimers[bot.BotName]["ClaimDailyStickers"].Change(TimeSpan.FromMinutes(timeout), TimeSpan.FromMilliseconds(-1));
                } else {
                    bot.ArchiLogger.LogGenericInfo($"Status: NoItemToClaim | Next run: {DateTime.Now.AddHours(FreeItemsConfig[bot.BotName].Timeout):T}");

                    FreeItemsTimers[bot.BotName]["ClaimDailyStickers"].Change(TimeSpan.FromHours(FreeItemsConfig[bot.BotName].Timeout), TimeSpan.FromMilliseconds(-1));
                }

                return;
            }

            bot.ArchiLogger.LogGenericInfo($"Status: Error | Next run: {DateTime.Now.AddMinutes(1):T}");
        } else {
            bot.ArchiLogger.LogGenericInfo($"Status: BotNotConnected | Next run: {DateTime.Now.AddMinutes(1):T}");
        }

        FreeItemsTimers[bot.BotName]["ClaimDailyStickers"].Change(TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(-1));
    }
}
