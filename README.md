# Free Items Plugin for ArchiSteamFarm

[![GitHub release version](https://img.shields.io/github/v/release/JackieWaltRyan/FreeItems.svg?label=Stable&logo=github)](https://github.com/JackieWaltRyan/FreeItems/releases/latest)
[![GitHub release date](https://img.shields.io/github/release-date/JackieWaltRyan/FreeItems.svg?label=Released&logo=github)](https://github.com/JackieWaltRyan/FreeItems/releases/latest)
[![Github release downloads](https://img.shields.io/github/downloads/JackieWaltRyan/FreeItems/latest/total.svg?label=Downloads&logo=github)](https://github.com/JackieWaltRyan/FreeItems/releases/latest)

ASF plugin for automatic receipt of various free items.

## Installation

1. Download the .zip file from
   the [![GitHub Release](https://img.shields.io/github/v/release/JackieWaltRyan/FreeItems?display_name=tag&logo=github&label=latest%20release)](https://github.com/JackieWaltRyan/FreeItems/releases/latest).<br><br>
2. Locate the `plugins` folder inside your ASF folder. Create a new folder here and unpack the downloaded .zip file to
   that folder.<br><br>
3. (Re)start ASF, you should get a message indicating that the plugin loaded successfully.

## Usage

Default configuration. To change this feature, add the following parameter to your bot's config file:

```json
{
  "FreeItemsConfig": {
    "PointStoreItems": false,
    "RecommendationsItems": false,
	"BlackList": [],
    "Timeout": 6
  }
}
```

- `PointStoreItems` - `bool` type with default value of `false`. If `true`, check and collect free items in the Steam
  Points Shop (animated avatars, frames, stickers, backgrounds, etc.).<br><br>
- `RecommendationsItems` - `bool` type with default value of `false`. If `true`, automatically browse the list of
  recommendations during seasonal sales (spring, summer, autumn, winter) to get 9 free stickers.<br><br>
- `BlackList` - `List<uint>` type with default value of being empty. List of `itemID` items that should never be 
  collected, even if possible. The main reason is that there are some items on Steam that are always free. Since 
  there is no way to check whether the bot already has such an item in its inventory or not, the plugin will always 
  try to get such items again. To prevent the plugin from sending extra requests to the server every time and wasting 
  time, you can disable such items in this list.<br><br>
- `Timeout` - `uint` type with default value of `6`. This is the number of hours to wait between rechecks for all free
  items. By default, this value is 6 hours. Since checking can create a large number of requests to the Steam servers,
  it is strongly recommended not to set this value too low!
