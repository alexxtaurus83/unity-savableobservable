using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
#if (UNITY_6000_0_OR_NEWER || UNITY_6_OR_NEWER) && HAS_UNITY_PIPELINE
using Unity.Pipeline.Commands;
using Unity.Pipeline.Models;
#endif
using UnityEditor.TestTools.TestRunner.Api;

namespace SavableObservable.Editor {
    /// <summary>
    /// Unattended Test Guard and TestRunnerApi integration for MMVC.
    /// Exposes unattended preparation, execution, and status polling commands to the Unity Pipeline,
    /// saves user-dirty scenes unconditionally without silent discarding, safely stores untitled scratch scenes in Assets,
    /// and ensures XML results are written and persisted across domain reloads via SessionState.
    /// </summary>
    [InitializeOnLoad]
    public static class MmvcUnattendedTestGuard {
        private const string SessionKeyStatus = "MMVC_UNATTENDED_STATUS";
        private const string SessionKeyOutputPath = "MMVC_UNATTENDED_OUTPUT_PATH";
        private const string SessionKeyMode = "MMVC_UNATTENDED_MODE";
        private const string SessionKeyFilter = "MMVC_UNATTENDED_FILTER";
        private const string SessionKeyStartTime = "MMVC_UNATTENDED_START_TIME";

        private const string ScratchSceneDir = "Assets/Scenes/Scratch";
        private const string ScratchScenePath = "Assets/Scenes/Scratch/UnattendedScratchScene.unity";

        private static TestRunnerApi s_Api;
        private static GuardTestCallbacks s_ActiveCallbacks;

        static MmvcUnattendedTestGuard() {
            // Subscribe to delayCall to check if an async test run was resumed or finished across domain reload
            EditorApplication.delayCall += OnDomainReloadOrInit;
        }

        private static void OnDomainReloadOrInit() {
            string status = SessionState.GetString(SessionKeyStatus, "idle");
            if (status == "running") {
                // Re-register callbacks if TestRunnerApi is executing across PlayMode domain reload
                EnsureCallbacksRegistered();
            }
        }

        private static void EnsureCallbacksRegistered() {
            if (s_Api == null) {
                s_Api = ScriptableObject.CreateInstance<TestRunnerApi>();
            }
            if (s_ActiveCallbacks == null) {
                s_ActiveCallbacks = new GuardTestCallbacks();
                s_Api.RegisterCallbacks(s_ActiveCallbacks);
            }
        }

        [Serializable]
        public class PrepareResult {
            public bool success;
            public bool stoppedPlayMode;
            public bool pendingPlayModeExit;
            public bool closedPrefabStage;
            public List<string> savedScenes = new List<string>();
            public List<string> scratchScenesCreated = new List<string>();
            public string message;
        }

        [Serializable]
        public class RunTestsResult {
            public bool success;
            public string status; // "completed", "running", "failed"
            public string mode;
            public string filter;
            public string outputPath;
            public int total;
            public int passed;
            public int failed;
            public int skipped;
            public int inconclusive;
            public double duration;
            public string message;
            public List<string> failureDetails = new List<string>();
        }

        /// <summary>
        /// Prepares the Unity Editor for unattended test execution:
        /// 1. Stops active PlayMode if running.
        /// 2. Closes any open Prefab Stage (saving changes).
        /// 3. Saves all dirty open scenes. Real project scenes (Assets/...) are saved in-place.
        ///    Untitled dirty scenes (empty path) are saved to Assets/Scenes/Scratch/UnattendedScratchScene.unity so SaveFilePanel never opens.
        /// 4. Saves dirty assets via AssetDatabase.SaveAssets().
        /// </summary>
#if (UNITY_6000_0_OR_NEWER || UNITY_6_OR_NEWER) && HAS_UNITY_PIPELINE
        [CliCommand("mmvc_prepare_unattended", "Prepares editor for unattended execution: exits PlayMode/PrefabStage, saves all dirty scenes and assets safely without modal prompts.")]
        public static string PrepareUnattendedCommand() {
            var result = PrepareUnattended();
            return EditorJsonUtility.ToJson(result, true);
        }
#endif

        public static PrepareResult PrepareUnattended() {
            var result = new PrepareResult { success = true };

            try {
                // Ensure runInBackground is enabled so Editor doesn't throttle or freeze tests when unfocused
                Application.runInBackground = true;

                // 1. Exit Play Mode if active or paused
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPaused) {
                    EditorApplication.isPlaying = false;
                    result.stoppedPlayMode = true;
                    result.pendingPlayModeExit = true;
                    result.success = false;
                    result.message = "Waiting for Play Mode to exit before saving scenes.";
                    return result;
                }

                // 2. Close Prefab Stage if open
                var currentPrefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
                if (currentPrefabStage != null) {
                    if (currentPrefabStage.scene.isDirty) {
                        result.success = false;
                        result.message = "Save the dirty Prefab Stage before running tests.";
                        return result;
                    }
                    StageUtility.GoToMainStage();
                    result.closedPrefabStage = true;
                }

                // 3. Save open dirty scenes
                int sceneCount = UnityEngine.SceneManagement.SceneManager.loadedSceneCount;
                for (int i = 0; i < sceneCount; i++) {
                    var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                    if (!scene.IsValid() || !scene.isLoaded) continue;

                    if (scene.isDirty) {
                        if (!string.IsNullOrEmpty(scene.path) && scene.path.StartsWith("Assets/")) {
                            // Saved project scene: save in-place
                            bool saved = EditorSceneManager.SaveScene(scene);
                            if (saved) {
                                result.savedScenes.Add(scene.path);
                            } else throw new IOException("Could not save scene: " + scene.path);
                        } else {
                            // Untitled scratch scene: save to safe Assets path to avoid SaveFilePanel dialog
                            if (!Directory.Exists(ScratchSceneDir)) {
                                Directory.CreateDirectory(ScratchSceneDir);
                            }
                            string scratchPath = AssetDatabase.GenerateUniqueAssetPath(ScratchScenePath);
                            bool saved = EditorSceneManager.SaveScene(scene, scratchPath);
                            if (saved) {
                                result.scratchScenesCreated.Add(scratchPath);
                            } else throw new IOException("Could not save scratch scene: " + scratchPath);
                        }
                    }
                }

                // 4. Save dirty assets
                AssetDatabase.SaveAssets();
                result.message = $"Successfully prepared editor. Saved {result.savedScenes.Count} scene(s), {result.scratchScenesCreated.Count} scratch scene(s).";
            } catch (Exception ex) {
                result.success = false;
                result.message = $"Error preparing unattended state: {ex.Message}";
                Debug.LogError($"[MmvcUnattendedTestGuard] PrepareUnattended error: {ex}");
            }

            return result;
        }

        /// <summary>
        /// Runs tests unattended via TestRunnerApi and writes complete NUnit3 XML to outputPath.
        /// If mode is playmode, sets up SessionState persistence so test completion survives domain reload.
        /// </summary>
#if (UNITY_6000_0_OR_NEWER || UNITY_6_OR_NEWER) && HAS_UNITY_PIPELINE
        [CliCommand("mmvc_run_tests_unattended", "Executes EditMode or PlayMode tests unattended via TestRunnerApi and writes NUnit3 XML directly to outputPath.")]
        public static string RunTestsUnattendedCommand(
            [CliArg("mode", "Test mode: editor, playmode (default: editor)")] string mode = "editor",
            [CliArg("filter", "Test name filter pattern (default: empty)")] string filter = "",
            [CliArg("output", "Path to save NUnit3 XML report (default: Temp/mmvc_test_result.xml)")] string output = "Temp/mmvc_test_result.xml",
            [CliArg("async_tests", "Run asynchronously: returns immediately, poll mmvc_test_status")] bool asyncTests = false,
            [CliArg("exclude_assembly", "Semicolon-separated assemblies to exclude (default: empty)")] string excludeAssembly = "",
            [CliArg("exclude_categories", "Semicolon-separated categories to exclude (default: empty)")] string excludeCategories = "") {
            
            var result = RunTestsUnattended(mode, filter, output, asyncTests, excludeAssembly, excludeCategories);
            return EditorJsonUtility.ToJson(result, true);
        }
#endif

        public static RunTestsResult RunTestsUnattended(string mode, string filter, string output, bool asyncTests, string excludeAssembly = "", string excludeCategories = "") {
            var prep = PrepareUnattended();
            if (!prep.success) {
                return new RunTestsResult {
                    success = false,
                    status = "failed",
                    message = "Failed to prepare unattended editor: " + prep.message
                };
            }

            TestMode testMode = TestMode.EditMode;
            if (!string.IsNullOrEmpty(mode) && (mode.IndexOf("play", StringComparison.OrdinalIgnoreCase) >= 0)) {
                testMode = TestMode.PlayMode;
            }

            string fullOutputPath = Path.GetFullPath(output);
            string outputDir = Path.GetDirectoryName(fullOutputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir)) {
                Directory.CreateDirectory(outputDir);
            }

            // Persist status and config to SessionState for cross-domain reload durability
            SessionState.SetString(SessionKeyStatus, "running");
            SessionState.SetString(SessionKeyOutputPath, fullOutputPath);
            SessionState.SetString(SessionKeyMode, testMode.ToString());
            SessionState.SetString(SessionKeyFilter, filter ?? "");
            SessionState.SetString(SessionKeyStartTime, DateTime.UtcNow.ToString("o"));

            EnsureCallbacksRegistered();
            s_ActiveCallbacks.Reset(fullOutputPath);

            var filterObj = new Filter {
                testMode = testMode
            };
            if (!string.IsNullOrEmpty(filter)) {
                filterObj.testNames = filter.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            }
            if (!string.IsNullOrWhiteSpace(excludeAssembly)) {
                filterObj.assemblyNames = Array.ConvertAll(
                    excludeAssembly.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries),
                    name => "!" + name.Trim());
            }
            if (!string.IsNullOrWhiteSpace(excludeCategories)) {
                filterObj.categoryNames = Array.ConvertAll(
                    excludeCategories.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries),
                    name => "!^" + System.Text.RegularExpressions.Regex.Escape(name.Trim()) + "$");
            }

            try {
                s_Api.Execute(new ExecutionSettings(filterObj));
            } catch (Exception ex) {
                SessionState.SetString(SessionKeyStatus, "failed");
                return new RunTestsResult {
                    success = false,
                    status = "failed",
                    message = "TestRunnerApi.Execute threw exception: " + ex.Message
                };
            }

            if (asyncTests || testMode == TestMode.PlayMode) {
                // PlayMode triggers domain reload; return running immediately
                return new RunTestsResult {
                    success = true,
                    status = "running",
                    mode = testMode.ToString(),
                    filter = filter,
                    outputPath = fullOutputPath,
                    message = "Tests started asynchronously. Poll mmvc_test_status."
                };
            }

            // For synchronous EditMode, wait briefly if already finished, or return running status
            return GetCurrentStatus();
        }

        /// <summary>
        /// Query current test status from SessionState or XML file.
        /// </summary>
#if (UNITY_6000_0_OR_NEWER || UNITY_6_OR_NEWER) && HAS_UNITY_PIPELINE
        [CliCommand("mmvc_test_status", "Returns current execution status and summary of unattended tests.")]
        public static string TestStatusCommand() {
            var result = GetCurrentStatus();
            return EditorJsonUtility.ToJson(result, true);
        }
#endif

        public static RunTestsResult GetCurrentStatus() {
            string status = SessionState.GetString(SessionKeyStatus, "idle");
            string outputPath = SessionState.GetString(SessionKeyOutputPath, "");
            string mode = SessionState.GetString(SessionKeyMode, "EditMode");
            string filter = SessionState.GetString(SessionKeyFilter, "");

            var result = new RunTestsResult {
                mode = mode,
                filter = filter,
                outputPath = outputPath,
                status = status
            };

            if (status == "completed" && File.Exists(outputPath)) {
                result.success = true;
                ParseNUnitXml(outputPath, result);
            } else if (status == "running") {
                result.success = true;
                result.message = "Test run is currently in progress.";
            } else if (status == "failed") {
                result.success = false;
                result.message = "Test run failed or was aborted.";
            } else {
                result.success = true;
                result.message = "No test run in progress (idle).";
            }

            return result;
        }

        private static void ParseNUnitXml(string xmlPath, RunTestsResult result) {
            try {
                var doc = new XmlDocument();
                doc.Load(xmlPath);
                var runNode = doc.SelectSingleNode("//test-run");
                if (runNode != null) {
                    int.TryParse(runNode.Attributes["total"]?.Value, out result.total);
                    int.TryParse(runNode.Attributes["passed"]?.Value, out result.passed);
                    int.TryParse(runNode.Attributes["failed"]?.Value, out result.failed);
                    int.TryParse(runNode.Attributes["skipped"]?.Value, out result.skipped);
                    int.TryParse(runNode.Attributes["inconclusive"]?.Value, out result.inconclusive);
                    double.TryParse(runNode.Attributes["duration"]?.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out result.duration);
                }

                var failureNodes = doc.SelectNodes("//test-case[failure]");
                if (failureNodes != null) {
                    foreach (XmlNode node in failureNodes) {
                        string name = node.Attributes["fullname"]?.Value ?? node.Attributes["name"]?.Value ?? "UnknownTest";
                        string msg = node.SelectSingleNode("failure/message")?.InnerText?.Trim() ?? "No message";
                        result.failureDetails.Add($"{name}: {msg}");
                    }
                }

                result.message = $"Run completed: {result.passed}/{result.total} passed, {result.failed} failed, {result.skipped} skipped.";
            } catch (Exception ex) {
                result.message = $"Completed with XML parse error: {ex.Message}";
            }
        }

        public static bool SaveResultToXml(ITestResultAdaptor result, string filePath) {
            try {
                string dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) {
                    Directory.CreateDirectory(dir);
                }

                TestRunnerApi.SaveResultToFile(result, filePath);
                var written = new XmlDocument();
                written.Load(filePath);
                if (written.DocumentElement == null || written.DocumentElement.Name != "test-run") {
                    throw new IOException("Unity did not write a complete NUnit test-run report.");
                }
                return true;
            } catch (Exception ex) {
                Debug.LogError($"[MmvcUnattendedTestGuard] Failed to save NUnit XML to '{filePath}': {ex.Message}");
                return false;
            }
        }

        private class GuardTestCallbacks : ICallbacks {
            private string m_OutputPath;

            public void Reset(string outputPath) {
                m_OutputPath = outputPath;
            }

            public void RunStarted(ITestAdaptor testsToRun) {
                SessionState.SetString(SessionKeyStatus, "running");
            }

            public void RunFinished(ITestResultAdaptor result) {
                if (string.IsNullOrEmpty(m_OutputPath)) {
                    m_OutputPath = SessionState.GetString(SessionKeyOutputPath, "Temp/mmvc_test_result.xml");
                }

                if (!SaveResultToXml(result, m_OutputPath)) {
                    SessionState.SetString(SessionKeyStatus, "failed");
                    return;
                }

                SessionState.SetString(SessionKeyStatus, "completed");

                Debug.Log($"[MmvcUnattendedTestGuard] Tests finished. Result saved to: {m_OutputPath}. Total: {result.PassCount + result.FailCount + result.SkipCount}, Passed: {result.PassCount}, Failed: {result.FailCount}");
            }

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result) { }
        }
    }
}
