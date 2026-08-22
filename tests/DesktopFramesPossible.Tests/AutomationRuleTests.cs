using System.Collections.Generic;
using System.Linq;
using Desktop_Frames;
using Newtonsoft.Json.Linq;
using Shouldly;
using Xunit;

namespace DesktopFramesPossible.Tests
{
    /// <summary>
    /// AutomationRule (de)serialization contract. ProfileManager persists rules with
    /// JArray.FromObject(rules) and reads them back with token.ToObject&lt;List&lt;AutomationRule&gt;&gt;()
    /// (default Newtonsoft settings) — these tests exercise that exact code path.
    /// </summary>
    public class AutomationRuleTests
    {
        [Fact]
        public void NewInstance_Defaults_ToProcessTrigger()
        {
            // Act
            var rule = new AutomationRule();

            // Assert - legacy semantics are the default; VD fields start empty
            rule.TriggerType.ShouldBe(AutomationTriggerType.Process);
            rule.VirtualDesktopId.ShouldBeNull();
            rule.VirtualDesktopName.ShouldBeNull();
            rule.DelaySeconds.ShouldBe(0);
            rule.IsPersisted.ShouldBeFalse();
        }

        [Fact]
        public void OldFormatJson_WithoutTriggerType_DeserializesAsProcessRule()
        {
            // Arrange - the exact shape written by pre-virtual-desktop builds
            const string oldJson = """
                [
                  {
                    "ProcessName": "chrome",
                    "TargetProfile": "Work",
                    "DelaySeconds": 3,
                    "IsPersisted": true
                  }
                ]
                """;

            // Act - same call ProfileManager.LoadAndSanitizeConfig makes
            var rules = JArray.Parse(oldJson).ToObject<List<AutomationRule>>();

            // Assert - absent TriggerType must fall back to Process, VD fields null
            rules.ShouldNotBeNull();
            var rule = rules!.Single();
            rule.TriggerType.ShouldBe(AutomationTriggerType.Process);
            rule.VirtualDesktopId.ShouldBeNull();
            rule.VirtualDesktopName.ShouldBeNull();
            rule.ProcessName.ShouldBe("chrome");
            rule.TargetProfile.ShouldBe("Work");
            rule.DelaySeconds.ShouldBe(3);
            rule.IsPersisted.ShouldBeTrue();
        }

        [Fact]
        public void OldFormatRule_RoundTrips_ThroughAppSerializationPath()
        {
            // Arrange - an old-format rule as loaded from disk
            const string oldJson = """
                [{ "ProcessName": "steam", "TargetProfile": "Gaming", "DelaySeconds": 1, "IsPersisted": false }]
                """;
            var loaded = JArray.Parse(oldJson).ToObject<List<AutomationRule>>();

            // Act - save (JArray.FromObject) then load again (ToObject), like SaveConfigInternal
            var reloaded = JArray.FromObject(loaded!).ToObject<List<AutomationRule>>();

            // Assert - nothing drifts across the round trip
            var rule = reloaded!.Single();
            rule.TriggerType.ShouldBe(AutomationTriggerType.Process);
            rule.ProcessName.ShouldBe("steam");
            rule.TargetProfile.ShouldBe("Gaming");
            rule.DelaySeconds.ShouldBe(1);
            rule.IsPersisted.ShouldBeFalse();
            rule.VirtualDesktopId.ShouldBeNull();
        }

        [Fact]
        public void VirtualDesktopRule_RoundTrips_AllFields()
        {
            // Arrange
            var original = new AutomationRule
            {
                TriggerType = AutomationTriggerType.VirtualDesktop,
                VirtualDesktopId = "a1b2c3d4-e5f6-4711-8899-aabbccddeeff",
                VirtualDesktopName = "Coding",
                TargetProfile = "Dev",
                IsPersisted = true,
                DelaySeconds = 0
            };

            // Act
            var reloaded = JArray.FromObject(new List<AutomationRule> { original })
                .ToObject<List<AutomationRule>>();

            // Assert
            var rule = reloaded!.Single();
            rule.TriggerType.ShouldBe(AutomationTriggerType.VirtualDesktop);
            rule.VirtualDesktopId.ShouldBe(original.VirtualDesktopId);
            rule.VirtualDesktopName.ShouldBe("Coding");
            rule.TargetProfile.ShouldBe("Dev");
            rule.IsPersisted.ShouldBeTrue();
            rule.DelaySeconds.ShouldBe(0);
            rule.ProcessName.ShouldBeNull();
        }

        [Fact]
        public void MixedRuleList_RoundTrips_PreservingBothTriggerTypes()
        {
            // Arrange - process and VD rules coexist in the single rule list
            var mixed = new List<AutomationRule>
            {
                new AutomationRule { ProcessName = "photoshop", TargetProfile = "Design", DelaySeconds = 2 },
                new AutomationRule
                {
                    TriggerType = AutomationTriggerType.VirtualDesktop,
                    VirtualDesktopId = "11111111-2222-3333-4444-555555555555",
                    VirtualDesktopName = "Desktop 2",
                    TargetProfile = "Home",
                    IsPersisted = true
                }
            };

            // Act
            var reloaded = JArray.FromObject(mixed).ToObject<List<AutomationRule>>();

            // Assert - order and per-type fields survive
            reloaded!.Count.ShouldBe(2);
            reloaded[0].TriggerType.ShouldBe(AutomationTriggerType.Process);
            reloaded[0].ProcessName.ShouldBe("photoshop");
            reloaded[0].DelaySeconds.ShouldBe(2);
            reloaded[0].VirtualDesktopId.ShouldBeNull();
            reloaded[1].TriggerType.ShouldBe(AutomationTriggerType.VirtualDesktop);
            reloaded[1].VirtualDesktopId.ShouldBe("11111111-2222-3333-4444-555555555555");
            reloaded[1].VirtualDesktopName.ShouldBe("Desktop 2");
            reloaded[1].IsPersisted.ShouldBeTrue();
        }

        [Fact]
        public void SerializedProcessRule_StaysReadable_ByOlderBuilds()
        {
            // Arrange - a process rule saved by the new build
            var rule = new AutomationRule { ProcessName = "code", TargetProfile = "Dev", DelaySeconds = 1 };

            // Act
            var json = JArray.FromObject(new List<AutomationRule> { rule });

            // Assert - the enum default serializes as 0 (Process), so old readers that
            // ignore unknown properties keep working; core fields remain untouched
            var obj = (JObject)json.Single();
            obj["ProcessName"]!.Value<string>().ShouldBe("code");
            obj["TargetProfile"]!.Value<string>().ShouldBe("Dev");
            obj["DelaySeconds"]!.Value<int>().ShouldBe(1);
            obj["TriggerType"]!.Value<int>().ShouldBe((int)AutomationTriggerType.Process);
        }
    }
}
