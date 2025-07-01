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
