using Desktop_Frames;
using Newtonsoft.Json.Linq;
using Shouldly;
using Xunit;

namespace DesktopFramesPossible.Tests
{
    public class DataSafetyItemMoveDialogTests
    {
        [Fact]
        public void GetOrCreateItemsArray_MissingItems_CreatesAndAttachesArray()
        {
            // Arrange - a tab/frame object with no Items property at all
            var container = new JObject { ["TabName"] = "Docs" };

            // Act
            JArray items = ItemMoveDialog.GetOrCreateItemsArray(container);
            items.Add(new JObject { ["Filename"] = "Shortcuts\\a.lnk" });

            // Assert - the added item must be reachable THROUGH the container (not detached)
            var persisted = container["Items"] as JArray;
            persisted.ShouldNotBeNull();
            persisted.ShouldBeSameAs(items);
            persisted.Count.ShouldBe(1);
            persisted[0]["Filename"]!.ToString().ShouldBe("Shortcuts\\a.lnk");
        }

        [Fact]
        public void GetOrCreateItemsArray_ExistingItems_ReturnsSameArray()
        {
            // Arrange
            var existing = new JArray { new JObject { ["Filename"] = "Shortcuts\\keep.lnk" } };
            var container = new JObject { ["Items"] = existing };

            // Act
            JArray items = ItemMoveDialog.GetOrCreateItemsArray(container);

            // Assert
            items.ShouldBeSameAs(existing);
            items.Count.ShouldBe(1);
        }

        [Fact]
        public void GetOrCreateItemsArray_NonArrayItems_ReplacesWithAttachedArray()
        {
            // Arrange - Items present but not a JArray (e.g. legacy/corrupt "" value)
            var container = new JObject { ["Items"] = "" };

            // Act
            JArray items = ItemMoveDialog.GetOrCreateItemsArray(container);
            items.Add(new JObject { ["Filename"] = "Shortcuts\\b.lnk" });

            // Assert
            (container["Items"] as JArray).ShouldBeSameAs(items);
            ((JArray)container["Items"]!).Count.ShouldBe(1);
        }

        [Fact]
        public void GetOrCreateItemsArray_NullContainer_ReturnsNull()
        {
            // Act & Assert
            ItemMoveDialog.GetOrCreateItemsArray(null).ShouldBeNull();
        }

        [Fact]
        public void GetOrCreateItemsArray_MovedItem_SurvivesSerializationRoundTrip()
        {
            // Arrange - the original bug: destination tab without Items lost the moved item
            var frame = new JObject
            {
                ["Title"] = "Target",
                ["TabsEnabled"] = "true",
                ["Tabs"] = new JArray { new JObject { ["TabName"] = "Tab 0" } }
            };
            var tab = (JObject)((JArray)frame["Tabs"]!)[0];

            // Act - simulate the move: resolve destination and add the item
            JArray destItems = ItemMoveDialog.GetOrCreateItemsArray(tab);
            destItems.Add(new JObject { ["Filename"] = "Shortcuts\\moved.lnk" });

            var reloaded = JObject.Parse(frame.ToString());

            // Assert - the item persists through a save/load round trip
            reloaded["Tabs"]![0]!["Items"]![0]!["Filename"]!.ToString().ShouldBe("Shortcuts\\moved.lnk");
        }
    }
}
