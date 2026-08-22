using System;
using System.Linq;
using Desktop_Frames;
using Newtonsoft.Json;
using Shouldly;
using Xunit;

namespace DesktopFramesPossible.Tests
{
    public class RemoteDataModelTests
    {
        [Fact]
        public void RemoteAnnouncement_NewInstance_HasSafeDefaults()
        {
            // Act
            var announcement = new RemoteAnnouncement();

            // Assert - defaults define the "show to everyone once, dismissible" contract
            announcement.TargetVersionMin.ShouldBe("0.0.0.0");
            announcement.TargetVersionMax.ShouldBe("99.9.9.9");
            announcement.MaxDisplayCount.ShouldBe(1);
            announcement.AutoCloseSeconds.ShouldBe(0);
            announcement.CanUserDismiss.ShouldBeTrue();
        }

        [Fact]
        public void RemoteAnnouncement_DefaultVersionWindow_CoversAnyRealAppVersion()
        {
            // Arrange
            var announcement = new RemoteAnnouncement();

            // Act - the default min/max strings must parse as 4-part versions
            bool minOk = Version.TryParse(announcement.TargetVersionMin, out Version? min);
            bool maxOk = Version.TryParse(announcement.TargetVersionMax, out Version? max);

            // Assert - and the window must be wide open (min < any plausible version < max)
            minOk.ShouldBeTrue();
            maxOk.ShouldBeTrue();
            var typicalAppVersion = new Version(2, 7, 0, 0);
            (min! < typicalAppVersion).ShouldBeTrue();
            (max! > typicalAppVersion).ShouldBeTrue();
        }

        [Fact]
        public void RemoteMeta_NewInstance_DefaultsToNoUpdateNoKillSwitch()
        {
            // Act
            var meta = new RemoteMeta();

            // Assert
            meta.LatestVersion.ShouldBe("0.0.0.0");
            meta.CriticalUpdate.ShouldBeFalse();
            meta.ForceKillSwitch.ShouldBeFalse();
        }

        [Fact]
        public void RemoteManifest_DeserializeFullManifest_MapsAllSections()
        {
            // Arrange - shape mirrors the published ngdfcs/getversion.json manifest
            const string json = """
                {
                  "Meta": {
                    "LatestVersion": "2.8.1.0",
                    "CriticalUpdate": true,
                    "DownloadUrl": "https://example.com/releases",
                    "ForceKillSwitch": false
                  },
                  "Announcements": [
                    {
                      "Id": "MSG_2026_01",
                      "Type": "Warning",
                      "Title": "Heads up",
                      "Body": "Something changed.",
                      "Link": "https://example.com/news",
                      "TargetVersionMin": "2.0.0.0",
                      "TargetVersionMax": "2.9.9.9",
                      "MaxDisplayCount": 3,
                      "AutoCloseSeconds": 15,
                      "CanUserDismiss": false
                    }
                  ],
                  "Settings": {
                    "EnableBackgroundLogging": true,
                    "SearchEngineUrl": "https://duckduckgo.com/?q="
                  }
                }
                """;

            // Act
            var manifest = JsonConvert.DeserializeObject<RemoteManifest>(json);

            // Assert
            manifest.ShouldNotBeNull();
            manifest.Meta.LatestVersion.ShouldBe("2.8.1.0");
            manifest.Meta.CriticalUpdate.ShouldBeTrue();
            manifest.Meta.DownloadUrl.ShouldBe("https://example.com/releases");
            manifest.Meta.ForceKillSwitch.ShouldBeFalse();

            var msg = manifest.Announcements.ShouldHaveSingleItem();
            msg.Id.ShouldBe("MSG_2026_01");
            msg.Type.ShouldBe("Warning");
            msg.TargetVersionMin.ShouldBe("2.0.0.0");
            msg.TargetVersionMax.ShouldBe("2.9.9.9");
            msg.MaxDisplayCount.ShouldBe(3);
            msg.AutoCloseSeconds.ShouldBe(15);
            msg.CanUserDismiss.ShouldBeFalse();

            manifest.Settings.EnableBackgroundLogging.ShouldBeTrue();
            manifest.Settings.SearchEngineUrl.ShouldBe("https://duckduckgo.com/?q=");
        }

        [Fact]
        public void RemoteAnnouncement_DeserializeMinimalJson_KeepsPropertyDefaults()
        {
            // Arrange - a server may publish only Id/Title/Body
            const string json = """{ "Id": "TINY", "Title": "t", "Body": "b" }""";

            // Act
            var msg = JsonConvert.DeserializeObject<RemoteAnnouncement>(json);

            // Assert - unspecified fields keep the class defaults
            msg.ShouldNotBeNull();
            msg.Id.ShouldBe("TINY");
            msg.TargetVersionMin.ShouldBe("0.0.0.0");
            msg.TargetVersionMax.ShouldBe("99.9.9.9");
            msg.MaxDisplayCount.ShouldBe(1);
            msg.AutoCloseSeconds.ShouldBe(0);
            msg.CanUserDismiss.ShouldBeTrue();
        }

        [Fact]
        public void RemoteManifest_SerializeThenDeserialize_RoundTripsValues()
        {
            // Arrange
            var original = new RemoteManifest
            {
                Meta = new RemoteMeta { LatestVersion = "3.0.0.1", CriticalUpdate = false, DownloadUrl = "https://dl.example.com", ForceKillSwitch = true },
                Announcements = new()
                {
                    new RemoteAnnouncement { Id = "A1", Type = "Info", Title = "Hello", Body = "World", MaxDisplayCount = 5 }
                },
                Settings = new RemoteSettings { EnableBackgroundLogging = false, SearchEngineUrl = "https://bing.com/search?q=" }
            };

            // Act
            string json = JsonConvert.SerializeObject(original);
            var copy = JsonConvert.DeserializeObject<RemoteManifest>(json);

            // Assert
            copy.ShouldNotBeNull();
            copy.Meta.LatestVersion.ShouldBe("3.0.0.1");
            copy.Meta.ForceKillSwitch.ShouldBeTrue();
            copy.Announcements.Single().Id.ShouldBe("A1");
            copy.Announcements.Single().MaxDisplayCount.ShouldBe(5);
            copy.Settings.SearchEngineUrl.ShouldBe("https://bing.com/search?q=");
        }

        [Fact]
        public void RemoteManifest_DeserializeWithUnknownProperties_IgnoresThemForForwardCompatibility()
        {
            // Arrange - a newer server manifest may contain fields this client doesn't know
            const string json = """
                {
                  "Meta": { "LatestVersion": "2.9.0.0", "BrandNewField": 42 },
                  "FutureSection": { "x": true }
                }
                """;

            // Act
            var manifest = JsonConvert.DeserializeObject<RemoteManifest>(json);

            // Assert - known values still bind, unknown ones are ignored without throwing
            manifest.ShouldNotBeNull();
            manifest.Meta.LatestVersion.ShouldBe("2.9.0.0");
            manifest.Announcements.ShouldBeNull();
        }
    }
}
