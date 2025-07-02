using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FreeItems;

internal sealed record QueryRewardItemsResponse {
    [JsonPropertyName("response")]
    public ResponseData? Response { get; set; }

    internal sealed record ResponseData {
        [JsonPropertyName("definitions")]
        public List<RewardItemData>? Definitions { get; set; }

        [JsonPropertyName("total_count")]
        public int? TotalCount { get; set; }

        [JsonPropertyName("next_cursor")]
        public string? Cursor { get; set; }

        internal sealed record RewardItemData {
            [JsonPropertyName("defid")]
            public uint DefId { get; set; }

            [JsonPropertyName("point_cost")]
            public uint? PointCost { get; set; }
        }
    }
}

internal sealed record GetDiscoveryQueueResponse {
    [JsonPropertyName("response")]
    public ResponseData? Response { get; set; }

    internal sealed record ResponseData {
        [JsonPropertyName("appids")]
        public List<int>? AppIds { get; set; }
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
