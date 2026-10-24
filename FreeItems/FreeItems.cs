using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Integration;
using ArchiSteamFarm.Web.Responses;
using SteamKit2;

namespace FreeItems;

internal sealed class FreeItems : IGitHubPluginUpdates, IBotModules, IBotConnection, IDisposable {
    public string Name => nameof(FreeItems);
    public string RepositoryName => "JackieWaltRyan/FreeItems";
    public Version Version => typeof(FreeItems).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version));

    public Dictionary<string, FreeItemsConfig> FreeItemsConfig = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Dictionary<string, Timer>> FreeItemsTimers = new(StringComparer.OrdinalIgnoreCase);

    public Task OnLoaded() {
        ASF.ArchiLogger.LogGenericInfo($"{Name} V{Version} loaded.");
        return Task.CompletedTask;
    }

    public async Task OnBotInitModules(Bot bot, IReadOnlyDictionary<string, JsonElement>? additionalConfigProperties = null) {
        if (FreeItemsTimers.TryGetValue(bot.BotName, out Dictionary<string, Timer>? dict)) {
            foreach (KeyValuePair<string, Timer> timers in dict) {
                await timers.Value.DisposeAsync().ConfigureAwait(false);
                bot.ArchiLogger.LogGenericInfo($"{timers.Key} Dispose.");
            }
        }

        FreeItemsTimers[bot.BotName] = new Dictionary<string, Timer> {
            { "ClaimPointStoreItems", new Timer(async _ => await ClaimPointStoreItems(bot).ConfigureAwait(false), null, Timeout.Infinite, Timeout.Infinite) },
            { "ClaimRecommendationsItems", new Timer(async _ => await ClaimRecommendationsItems(bot).ConfigureAwait(false), null, Timeout.Infinite, Timeout.Infinite) },
            { "ClaimDailyStickers", new Timer(async _ => await ClaimDailyStickers(bot).ConfigureAwait(false), null, Timeout.Infinite, Timeout.Infinite) }
        };

        FreeItemsConfig[bot.BotName] = new FreeItemsConfig {
            PointStoreItems = true,
            RecommendationsItems = true,
            DailyStickers = true,
            Timeout = 6
        };

        if (additionalConfigProperties != null) {
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
        }

        FreeItemsConfig psc = FreeItemsConfig[bot.BotName];

        if (psc.PointStoreItems || psc.RecommendationsItems || psc.DailyStickers) {
            bot.ArchiLogger.LogGenericInfo($"FreeItemsConfig: {psc.ToJsonText()}");

            if (bot.IsConnectedAndLoggedOn) {
                ScheduleBotTimers(bot, TimeSpan.FromSeconds(5));
            }
        }
    }

    public Task OnBotLoggedOn(Bot bot) {
        if (!FreeItemsConfig.ContainsKey(bot.BotName)) {
            FreeItemsConfig[bot.BotName] = new FreeItemsConfig {
                PointStoreItems = true,
                RecommendationsItems = true,
                DailyStickers = true,
                Timeout = 6
            };
        }

        if (!FreeItemsTimers.ContainsKey(bot.BotName)) {
            FreeItemsTimers[bot.BotName] = new Dictionary<string, Timer> {
                { "ClaimPointStoreItems", new Timer(async _ => await ClaimPointStoreItems(bot).ConfigureAwait(false), null, Timeout.Infinite, Timeout.Infinite) },
                { "ClaimRecommendationsItems", new Timer(async _ => await ClaimRecommendationsItems(bot).ConfigureAwait(false), null, Timeout.Infinite, Timeout.Infinite) },
                { "ClaimDailyStickers", new Timer(async _ => await ClaimDailyStickers(bot).ConfigureAwait(false), null, Timeout.Infinite, Timeout.Infinite) }
            };
        }

        ScheduleBotTimers(bot, TimeSpan.FromMinutes(1));
        return Task.CompletedTask;
    }

    public Task OnBotDisconnected(Bot bot, EResult reason) {
        if (FreeItemsTimers.TryGetValue(bot.BotName, out Dictionary<string, Timer>? dict)) {
            foreach (Timer timer in dict.Values) {
                timer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        return Task.CompletedTask;
    }

    private void ScheduleBotTimers(Bot bot, TimeSpan initialDelay) {
        if (!FreeItemsConfig.TryGetValue(bot.BotName, out FreeItemsConfig? psc) ||
            !FreeItemsTimers.TryGetValue(bot.BotName, out Dictionary<string, Timer>? timers)) {
            return;
        }

        if (psc.PointStoreItems && timers.TryGetValue("ClaimPointStoreItems", out Timer? pointTimer)) {
            pointTimer.Change(initialDelay, Timeout.InfiniteTimeSpan);
        }

        if (psc.RecommendationsItems && timers.TryGetValue("ClaimRecommendationsItems", out Timer? recTimer)) {
            recTimer.Change(initialDelay + TimeSpan.FromSeconds(15), Timeout.InfiniteTimeSpan);
        }

        if (psc.DailyStickers && timers.TryGetValue("ClaimDailyStickers", out Timer? stickerTimer)) {
            stickerTimer.Change(initialDelay + TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
        }
    }

    public async Task<Dictionary<uint, uint>> LoadPointStoreItems(Bot bot, uint? count = 0, string? cursor = null, int retryCount = 0) {
        const int maxRetries = 3;
        try {
            Dictionary<uint, uint> pointDict = new();

            if (!bot.IsConnectedAndLoggedOn) {
                return pointDict;
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
                        if ((item.BundleDefIds == null) && (item.PointCost == "0") && !FreeItemsConfig[bot.BotName].BlackList.Contains(item.DefId)) {
                            pointDict[item.DefId] = item.AppId;
                        }
                    }

                    if (count >= response.TotalCount || string.IsNullOrEmpty(response.Cursor)) {
                        return pointDict;
                    }

                    cursor = response.Cursor;
                    retryCount = 0;

                    await Task.Delay(1000).ConfigureAwait(false);
                } else {
                    if (retryCount >= maxRetries) {
                        return pointDict;
                    }

                    await Task.Delay(5000).ConfigureAwait(false);
                    retryCount++;
                }
            } else {
                if (retryCount >= maxRetries) {
                    return pointDict;
                }

                await Task.Delay(5000).ConfigureAwait(false);
                retryCount++;
            }

            Dictionary<uint, uint> nextItems = await LoadPointStoreItems(bot, count, cursor, retryCount).ConfigureAwait(false);
            foreach (KeyValuePair<uint, uint> kv in nextItems) {
                pointDict[kv.Key] = kv.Value;
            }

            return pointDict;
        } catch {
            if (retryCount >= maxRetries) {
                return new Dictionary<uint, uint>();
            }

            await Task.Delay(5000).ConfigureAwait(false);

            return await LoadPointStoreItems(bot, count, cursor, retryCount + 1).ConfigureAwait(false);
        }
    }

    public async Task ClaimPointStoreItems(Bot bot) {
        if (bot.IsConnectedAndLoggedOn) {
            if (FreeItemsConfig[bot.BotName].BlackList.Count > 0) {
                bot.ArchiLogger.LogGenericInfo($"BlackList: {FreeItemsConfig[bot.BotName].BlackList.ToJsonText()}");
            }

            Dictionary<uint, uint> freePoints = await LoadPointStoreItems(bot).ConfigureAwait(false);

            bot.ArchiLogger.LogGenericInfo($"Free items found: {freePoints.Count}");

            if (freePoints.Count > 0) {
                int queue = freePoints.Count;

                foreach (KeyValuePair<uint, uint> point in freePoints) {
                    ObjectResponse<RedeemPointsResponse>? rawResponse = await bot.ArchiWebHandler.UrlPostToJsonObjectWithSession<RedeemPointsResponse>(
                        new Uri("https://api.steampowered.com/ILoyaltyRewardsService/RedeemPoints/v1/"), data: new Dictionary<string, string>(2) {
                            { "access_token", bot.AccessToken ?? string.Empty },
                            { "defid", $"{point.Key}" }
                        }, session: ArchiWebHandler.ESession.None
                    ).ConfigureAwait(false);

                    string? response = rawResponse?.Content?.Response?.CommunityItemId;

                    queue -= 1;

                    bot.ArchiLogger.LogGenericInfo(response != null ? $"App: {point.Value} / ID: {point.Key} | Status: OK | Queue: {queue}" : $"App: {point.Value} / ID: {point.Key} | Status: Error | Queue: {queue}");

                    if (response != null) {
                        await Task.Delay(1000).ConfigureAwait(false);
                    } else {
                        await Task.Delay(5000).ConfigureAwait(false);
                    }
                }
            }

            uint timeoutHours = Math.Max(1u, FreeItemsConfig[bot.BotName].Timeout);
            bot.ArchiLogger.LogGenericInfo($"Status: QueueIsEmpty | Next run: {DateTime.Now.AddHours(timeoutHours):T}");

            FreeItemsTimers[bot.BotName]["ClaimPointStoreItems"].Change(TimeSpan.FromHours(timeoutHours), Timeout.InfiniteTimeSpan);
        } else {
            bot.ArchiLogger.LogGenericInfo($"Status: BotNotConnected | Next run: {DateTime.Now.AddMinutes(1):T}");

            FreeItemsTimers[bot.BotName]["ClaimPointStoreItems"].Change(TimeSpan.FromMinutes(1), Timeout.InfiniteTimeSpan);
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

                                    await Task.Delay(1000).ConfigureAwait(false);
                                } else {
                                    bot.ArchiLogger.LogGenericInfo($"ID: {gameId} | Status: Error | Queue: {queue} | Next run: {DateTime.Now.AddMinutes(1):T}");

                                    FreeItemsTimers[bot.BotName]["ClaimRecommendationsItems"].Change(TimeSpan.FromMinutes(1), Timeout.InfiniteTimeSpan);

                                    return;
                                }
                            }
                        } else {
                            bot.ArchiLogger.LogGenericInfo($"Status: Error | Next run: {DateTime.Now.AddMinutes(1):T}");

                            FreeItemsTimers[bot.BotName]["ClaimRecommendationsItems"].Change(TimeSpan.FromMinutes(1), Timeout.InfiniteTimeSpan);

                            return;
                        }
                    }

                    if (queue != 0) {
                        continue;
                    }

                    bot.ArchiLogger.LogGenericInfo($"Status: QueueIsEmpty | Next run: {DateTime.Now.AddHours(24):T}");

                    FreeItemsTimers[bot.BotName]["ClaimRecommendationsItems"].Change(TimeSpan.FromHours(24), Timeout.InfiniteTimeSpan);

                    return;
                }

                uint timeoutHours = Math.Max(1u, FreeItemsConfig[bot.BotName].Timeout);
                bot.ArchiLogger.LogGenericInfo($"Status: NoItemToClaim | Next run: {DateTime.Now.AddHours(timeoutHours):T}");

                FreeItemsTimers[bot.BotName]["ClaimRecommendationsItems"].Change(TimeSpan.FromHours(timeoutHours), Timeout.InfiniteTimeSpan);

                return;
            }

            bot.ArchiLogger.LogGenericInfo($"Status: Error | Next run: {DateTime.Now.AddMinutes(1):T}");
        } else {
            bot.ArchiLogger.LogGenericInfo($"Status: BotNotConnected | Next run: {DateTime.Now.AddMinutes(1):T}");
        }

        FreeItemsTimers[bot.BotName]["ClaimRecommendationsItems"].Change(TimeSpan.FromMinutes(1), Timeout.InfiniteTimeSpan);
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

                    int timeout = Math.Max(1, (int) Math.Ceiling((DateTimeOffset.FromUnixTimeSeconds(response.NextClaimTime) - DateTimeOffset.UtcNow).TotalMinutes));

                    bot.ArchiLogger.LogGenericInfo($"ID: {response.RewardItem.DefId} {name} | Status: OK | Next run: {DateTime.Now.AddMinutes(timeout):T}");

                    FreeItemsTimers[bot.BotName]["ClaimDailyStickers"].Change(TimeSpan.FromMinutes(timeout), Timeout.InfiniteTimeSpan);
                } else {
                    uint timeoutHours = Math.Max(1u, FreeItemsConfig[bot.BotName].Timeout);
                    bot.ArchiLogger.LogGenericInfo($"Status: NoItemToClaim | Next run: {DateTime.Now.AddHours(timeoutHours):T}");

                    FreeItemsTimers[bot.BotName]["ClaimDailyStickers"].Change(TimeSpan.FromHours(timeoutHours), Timeout.InfiniteTimeSpan);
                }

                return;
            }

            bot.ArchiLogger.LogGenericInfo($"Status: Error | Next run: {DateTime.Now.AddMinutes(1):T}");
        } else {
            bot.ArchiLogger.LogGenericInfo($"Status: BotNotConnected | Next run: {DateTime.Now.AddMinutes(1):T}");
        }

        FreeItemsTimers[bot.BotName]["ClaimDailyStickers"].Change(TimeSpan.FromMinutes(1), Timeout.InfiniteTimeSpan);
    }

    public void Dispose() {
        foreach (Dictionary<string, Timer> dict in FreeItemsTimers.Values) {
            foreach (Timer timer in dict.Values) {
                timer.Dispose();
            }
        }

        FreeItemsTimers.Clear();
    }
}
