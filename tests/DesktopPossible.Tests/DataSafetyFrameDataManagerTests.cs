using System;
using System.Collections.Generic;
using System.Globalization;
using Desktop_Frames;
using Shouldly;
using Xunit;

namespace DesktopPossible.Tests
{
    public class DataSafetyFrameDataManagerTests
    {
        /// <summary>Runs an action with a forced comma-decimal culture (de-DE) on the
        /// current thread, restoring the original culture afterwards.</summary>
        private static void WithCommaDecimalCulture(Action action)
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                action();
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        #region Invariant-culture round trips

        [Fact]
        public void ValidateDataTypes_ValidDecimalGeometry_UnchangedUnderCommaCulture()
        {
            WithCommaDecimalCulture(() =>
            {
                // Arrange - geometry as persisted (invariant strings and boxed doubles)
                var frameDict = new Dictionary<string, object>
                {
                    ["Width"] = 130.5,
                    ["Height"] = 95.25,
                    ["UnrolledHeight"] = "130.5"
                };

                // Act
                bool modified = FrameDataManager.ValidateDataTypes(frameDict);

                // Assert - "130.5" must NOT be misread (1305) or reset under de-DE
                modified.ShouldBeFalse();
                frameDict["Width"].ShouldBe(130.5);
                frameDict["Height"].ShouldBe(95.25);
                frameDict["UnrolledHeight"].ShouldBe("130.5");
            });
        }

        [Fact]
        public void ValidateDataTypes_InvalidUnrolledHeight_RewrittenInvariantlyUnderCommaCulture()
        {
            WithCommaDecimalCulture(() =>
            {
                // Arrange
                var frameDict = new Dictionary<string, object>
                {
                    ["Width"] = 230,
                    ["Height"] = 130.5,
                    ["UnrolledHeight"] = "garbage"
                };

                // Act
                bool modified = FrameDataManager.ValidateDataTypes(frameDict);

                // Assert - fallback is written with a dot, never "130,5"
                modified.ShouldBeTrue();
                frameDict["UnrolledHeight"].ShouldBe("130.5");
            });
        }

        [Fact]
        public void ValidateDataTypes_MissingWidthAndHeight_DefaultsApplied()
        {
            // Arrange - no Width/Height keys at all (previously threw via the indexer)
            var frameDict = new Dictionary<string, object>();

            // Act
            bool modified = FrameDataManager.ValidateDataTypes(frameDict);

            // Assert
            modified.ShouldBeTrue();
            frameDict["Width"].ShouldBe(230);
            frameDict["Height"].ShouldBe(130);
        }

        [Fact]
        public void ValidateDataTypes_InvariantStringGeometry_ParsesUnderCommaCulture()
        {
            WithCommaDecimalCulture(() =>
            {
                // Arrange - strings as older configs persisted them
                var frameDict = new Dictionary<string, object>
                {
                    ["Width"] = "412.5",
                    ["Height"] = "260.75"
                };

                // Act
                bool modified = FrameDataManager.ValidateDataTypes(frameDict);

                // Assert - valid values are kept as-is, not reset to defaults
                modified.ShouldBeFalse();
                frameDict["Width"].ShouldBe("412.5");
                frameDict["Height"].ShouldBe("260.75");
            });
        }

        #endregion

        #region Legacy key consolidation

        [Fact]
        public void ConsolidateLegacyKeys_ZeroBorderThickness_IsPreserved()
        {
            // Arrange - border thickness 0 is a legitimate user setting
            var frameDict = new Dictionary<string, object>
            {
                ["FrameBorderThickness"] = "0"
            };

            // Act
            FrameDataManager.ConsolidateLegacyKeys(frameDict);

            // Assert - previously vacuumed away on every save
            frameDict.ContainsKey("FrameBorderThickness").ShouldBeTrue();
            frameDict["FrameBorderThickness"].ShouldBe("0");
        }

        [Fact]
        public void ConsolidateLegacyKeys_EmptyStringColor_IsPreserved()
        {
            // Arrange
            var frameDict = new Dictionary<string, object>
            {
                ["FrameBorderColor"] = ""
            };

            // Act
            FrameDataManager.ConsolidateLegacyKeys(frameDict);

            // Assert
            frameDict["FrameBorderColor"].ShouldBe("");
        }

        [Fact]
        public void ConsolidateLegacyKeys_LegacyKeys_RescuedIntoOfficialKeysAndRemoved()
        {
            // Arrange - legacy spellings as produced by pre-rename versions
            var frameDict = new Dictionary<string, object>
            {
                ["FenceBorderColor"] = "Red",
                ["frameBorderThickness"] = 3
            };

            // Act
            FrameDataManager.ConsolidateLegacyKeys(frameDict);

            // Assert
            frameDict.ContainsKey("FenceBorderColor").ShouldBeFalse();
            frameDict.ContainsKey("frameBorderThickness").ShouldBeFalse();
            frameDict["FrameBorderColor"].ShouldBe("Red");
            frameDict["FrameBorderThickness"].ShouldBe(3);
        }

        [Fact]
        public void ConsolidateLegacyKeys_OfficialValuePresent_NotOverwrittenByLegacy()
        {
            // Arrange - official "0" must win over a stale legacy value
            var frameDict = new Dictionary<string, object>
            {
                ["FrameBorderThickness"] = "0",
                ["FenceBorderThickness"] = 5
            };

            // Act
            FrameDataManager.ConsolidateLegacyKeys(frameDict);

            // Assert
            frameDict["FrameBorderThickness"].ShouldBe("0");
            frameDict.ContainsKey("FenceBorderThickness").ShouldBeFalse();
        }

        [Fact]
        public void ConsolidateLegacyKeys_NullLegacyValue_DiscardedWithoutRescue()
        {
            // Arrange - null legacy entries are the only invalid ones
            var frameDict = new Dictionary<string, object>
            {
                ["FenceBorderColor"] = null!
            };

            // Act
            FrameDataManager.ConsolidateLegacyKeys(frameDict);

            // Assert
            frameDict.ContainsKey("FenceBorderColor").ShouldBeFalse();
            frameDict.ContainsKey("FrameBorderColor").ShouldBeFalse();
        }

        #endregion
    }
}
