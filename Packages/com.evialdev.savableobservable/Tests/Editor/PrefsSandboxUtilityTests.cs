using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using SavableObservable.Tests.Contracts;
using UnityEngine;

namespace SavableObservable.Tests {
    [TestFixture]
    public class PrefsSandboxUtilityTests {
        private const string TestIntKey = "SavableObservable_Test_IntKey";
        private const string TestFloatKey = "SavableObservable_Test_FloatKey";
        private const string TestStringKey = "SavableObservable_Test_StringKey";
        private const string TestAbsentKey = "SavableObservable_Test_AbsentKey";

        private string _testParentTempDir;

        [SetUp]
        public void SetUp() {
            _testParentTempDir = Path.Combine(Path.GetTempPath(), "PrefsSandboxUtilityTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testParentTempDir);

            // Clean up any stale prefs before each test
            PlayerPrefs.DeleteKey(TestIntKey);
            PlayerPrefs.DeleteKey(TestFloatKey);
            PlayerPrefs.DeleteKey(TestStringKey);
            PlayerPrefs.DeleteKey(TestAbsentKey);
            PlayerPrefs.Save();
        }

        [TearDown]
        public void TearDown() {
            PlayerPrefs.DeleteKey(TestIntKey);
            PlayerPrefs.DeleteKey(TestFloatKey);
            PlayerPrefs.DeleteKey(TestStringKey);
            PlayerPrefs.DeleteKey(TestAbsentKey);
            PlayerPrefs.Save();

            if (Directory.Exists(_testParentTempDir)) {
                try {
                    Directory.Delete(_testParentTempDir, true);
                } catch {
                }
            }
        }

        [Test]
        public void TypedRoundtrip_PreservesIntMinValue_NaN_EmptyString_AndAbsentKeys() {
            // Setup original values
            PlayerPrefs.SetInt(TestIntKey, int.MinValue);
            PlayerPrefs.SetFloat(TestFloatKey, float.NaN);
            PlayerPrefs.SetString(TestStringKey, string.Empty);
            // TestAbsentKey is left absent

            var trackedKeyTypes = new Dictionary<string, Type> {
                { TestIntKey, typeof(int) },
                { TestFloatKey, typeof(float) },
                { TestStringKey, typeof(string) },
                { TestAbsentKey, typeof(string) }
            };

            using (var sandbox = new PrefsSandboxUtility(trackedKeyTypes, _testParentTempDir)) {
                sandbox.Begin();

                // Mutate PlayerPrefs inside sandbox
                PlayerPrefs.SetInt(TestIntKey, 42);
                PlayerPrefs.SetFloat(TestFloatKey, 3.14f);
                PlayerPrefs.SetString(TestStringKey, "mutated string value");
                PlayerPrefs.SetString(TestAbsentKey, "now present");
                PlayerPrefs.Save();

                Assert.AreEqual(42, PlayerPrefs.GetInt(TestIntKey));
                Assert.AreEqual(3.14f, PlayerPrefs.GetFloat(TestFloatKey));
                Assert.AreEqual("mutated string value", PlayerPrefs.GetString(TestStringKey));
                Assert.IsTrue(PlayerPrefs.HasKey(TestAbsentKey));

                // End sandbox to restore original values
                sandbox.End();
            }

            // Assert exact restoration
            Assert.AreEqual(int.MinValue, PlayerPrefs.GetInt(TestIntKey));
            Assert.IsTrue(float.IsNaN(PlayerPrefs.GetFloat(TestFloatKey)));
            Assert.AreEqual(string.Empty, PlayerPrefs.GetString(TestStringKey));
            Assert.IsFalse(PlayerPrefs.HasKey(TestAbsentKey), "Absent key must be deleted upon End/restore");
        }

        [Test]
        public void SeparateListsConstructor_RoundtripsAccurately() {
            PlayerPrefs.SetInt(TestIntKey, 9999);
            PlayerPrefs.SetFloat(TestFloatKey, 1.2345f);
            PlayerPrefs.SetString(TestStringKey, "ExactMatch");

            var sandbox = new PrefsSandboxUtility(
                intKeys: new[] { TestIntKey },
                floatKeys: new[] { TestFloatKey },
                stringKeys: new[] { TestStringKey, TestAbsentKey },
                tempDirectory: _testParentTempDir);

            using (sandbox) {
                sandbox.Begin();
                sandbox.ClearTrackedKeys();

                Assert.IsFalse(PlayerPrefs.HasKey(TestIntKey));
                Assert.IsFalse(PlayerPrefs.HasKey(TestFloatKey));
                Assert.IsFalse(PlayerPrefs.HasKey(TestStringKey));
                Assert.IsFalse(PlayerPrefs.HasKey(TestAbsentKey));

                PlayerPrefs.SetInt(TestIntKey, 1);
                PlayerPrefs.SetFloat(TestFloatKey, 2f);
                PlayerPrefs.SetString(TestStringKey, "different");
                PlayerPrefs.SetString(TestAbsentKey, "createdInTest");
            }

            Assert.AreEqual(9999, PlayerPrefs.GetInt(TestIntKey));
            Assert.AreEqual(1.2345f, PlayerPrefs.GetFloat(TestFloatKey), 0.0001f);
            Assert.AreEqual("ExactMatch", PlayerPrefs.GetString(TestStringKey));
            Assert.IsFalse(PlayerPrefs.HasKey(TestAbsentKey));
        }

        [Test]
        public void BeginAndEnd_AreIdempotent() {
            PlayerPrefs.SetInt(TestIntKey, 123);

            var tracked = new[] { new KeyValuePair<string, PrefKeyType>(TestIntKey, PrefKeyType.Int) };
            using (var sandbox = new PrefsSandboxUtility(tracked, _testParentTempDir)) {
                // Call Begin multiple times
                sandbox.Begin();
                Assert.IsTrue(sandbox.IsActive);
                sandbox.Begin();
                Assert.IsTrue(sandbox.IsActive);

                PlayerPrefs.SetInt(TestIntKey, 456);

                // Call End multiple times
                sandbox.End();
                Assert.IsFalse(sandbox.IsActive);
                Assert.AreEqual(123, PlayerPrefs.GetInt(TestIntKey));

                // Redundant End should not throw or alter state
                sandbox.End();
                Assert.IsFalse(sandbox.IsActive);
                Assert.AreEqual(123, PlayerPrefs.GetInt(TestIntKey));
            }

            // Dispose after End should also be a no-op
            Assert.AreEqual(123, PlayerPrefs.GetInt(TestIntKey));
        }

        [Test]
        public void PreexistingDirectory_IsNeverRecursivelyDeleted() {
            // Write a sentinel file to the parent temp directory before starting sandbox
            string sentinelFile = Path.Combine(_testParentTempDir, "preexisting_user_data.txt");
            File.WriteAllText(sentinelFile, "important user test data");

            string createdScratchDir;
            using (var sandbox = new PrefsSandboxUtility(new KeyValuePair<string, Type>[0], _testParentTempDir)) {
                sandbox.Begin();
                createdScratchDir = sandbox.ScratchDirectory;

                Assert.IsTrue(Directory.Exists(_testParentTempDir), "Parent temp directory must exist");
                Assert.IsTrue(Directory.Exists(createdScratchDir), "Owned scratch directory must have been created");

                // Write a scratch file inside scratch dir
                File.WriteAllText(Path.Combine(createdScratchDir, "scratch.tmp"), "scratch content");
            }

            // After disposal/End:
            // 1. Parent temp directory must still exist and NOT be deleted
            Assert.IsTrue(Directory.Exists(_testParentTempDir), "Parent temp directory must NEVER be recursively deleted");
            Assert.IsTrue(File.Exists(sentinelFile), "Pre-existing files in parent temp directory must be preserved");
            Assert.AreEqual("important user test data", File.ReadAllText(sentinelFile));

            // 2. Only the unique owned scratch directory should have been cleaned up
            Assert.IsFalse(Directory.Exists(createdScratchDir), "Unique owned scratch directory should be deleted on End/Dispose");
        }

        [Test]
        public void UnsupportedType_ThrowsNotSupportedException() {
            var tracked = new Dictionary<string, Type> {
                { "InvalidKey", typeof(PrefsSandboxUtilityTests) }
            };

            Assert.Throws<NotSupportedException>(() => new PrefsSandboxUtility(tracked));
        }

        [Test]
        public void ClearTrackedKeys_ThrowsIfNotActive() {
            var tracked = new[] { new KeyValuePair<string, PrefKeyType>(TestIntKey, PrefKeyType.Int) };
            var sandbox = new PrefsSandboxUtility(tracked);

            Assert.Throws<InvalidOperationException>(() => sandbox.ClearTrackedKeys());
        }
    }
}
