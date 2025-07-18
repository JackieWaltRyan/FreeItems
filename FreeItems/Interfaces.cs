using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FreeItems;

internal sealed record FreeItemsConfig {
    [JsonInclude]
    public bool PointStoreItems { get; set; }

    [JsonInclude]
    public bool RecommendationsItems { get; set; }

    [JsonInclude]
    public bool DailyStickers { get; set; }

    [JsonInclude]
    public uint Timeout { get; set; } = 6;

    [JsonConstructor]
    public FreeItemsConfig() { }
}

internal sealed record QueryRewardItemsResponse {
    [JsonPropertyName("response")]
    public ResponseData? Response { get; set; }

    internal sealed record ResponseData {
        [JsonPropertyName("definitions")]
        public List<RewardItemData>? Definitions { get; set; }

        [JsonPropertyName("total_count")]
        public uint? TotalCount { get; set; }

        [JsonPropertyName("next_cursor")]
        public string? Cursor { get; set; }

        internal sealed record RewardItemData {
            [JsonPropertyName("defid")]
            public uint DefId { get; set; }

            [JsonPropertyName("point_cost")]
            public string? PointCost { get; set; }
        }
    }
}

internal sealed record RedeemPointsResponse {
    [JsonPropertyName("response")]
    public ResponseData? Response { get; set; }

    internal sealed record ResponseData {
        [JsonPropertyName("communityitemid")]
        public long CommunityItemId { get; set; }
    }
}

internal sealed record SeasonalSalesDateResponse {
    [JsonPropertyName("start")]
    public required string Start { get; set; }

    [JsonPropertyName("end")]
    public required string End { get; set; }
}

internal sealed record GetDiscoveryQueueResponse {
    [JsonPropertyName("response")]
    public ResponseData? Response { get; set; }

    internal sealed record ResponseData {
        [JsonPropertyName("appids")]
        public List<uint>? AppIds { get; set; }
    }
}

internal sealed record ClaimItemResponse {
    [JsonPropertyName("next_claim_time")]
    public long NextClaimTime { get; set; }

    [JsonPropertyName("reward_item")]
    public RewardItemData? RewardItem { get; set; }

    internal sealed record RewardItemData {
        [JsonPropertyName("defid")]
        public uint DefId { get; set; }

        [JsonPropertyName("community_item_data")]
        public CommunityItemData? CommunityItem { get; set; }

        public sealed record CommunityItemData {
            [JsonPropertyName("item_name")]
            public string? ItemName { get; set; }

            [JsonPropertyName("item_title")]
            public string? ItemTitle { get; set; }
        }
    }
}
