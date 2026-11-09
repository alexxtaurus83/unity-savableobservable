using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;
using TMPro;
using SavableObservable;
using SavableObservable.Samples;

namespace SavableObservable.Tests {
    #region Top-Level Fixture Components for PlayMode Tests

    public class PlayModeExecutionTracker : MonoBehaviour {
        public static readonly List<string> ExecutionLog = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetLog() {
            ExecutionLog.Clear();
        }
    }

    // No [DefaultExecutionOrder]: the model runs at the default order, after the presenter's -150 Awake
    public class PlayModeOrderedModel : BaseObservableDataModel {
        public ObservableVariable<int> level = new ObservableVariable<int>();

        protected override void Awake() {
            base.Awake();
            PlayModeExecutionTracker.ExecutionLog.Add("Model:Awake");
        }
    }

    [DefaultExecutionOrder(-150)]
    public class PlayModeOrderedPresenter : BaseObservablePresenter<PlayModeOrderedModel> {
        [AutoBind(nameof(PlayModeOrderedModel.level))]
        public TextMeshProUGUI levelDisplay;

        public int LevelChangeNotifications { get; private set; }

        protected override void Awake() {
            base.Awake();
            PlayModeExecutionTracker.ExecutionLog.Add("Presenter:Awake");
        }

        [ObservableHandler(nameof(PlayModeOrderedModel.level))]
        private void OnLevelChanged(int cur) {
            LevelChangeNotifications++;
        }
    }

    [DefaultExecutionOrder(0)]
    public class PlayModeOrderedLogic : BaseLogic<PlayModeOrderedModel, PlayModeOrderedPresenter> {
        protected override bool RegisterAsSharedSingleton => false;

        protected override void Awake() {
            base.Awake();
            PlayModeExecutionTracker.ExecutionLog.Add("Logic:Awake");
        }
    }

    #endregion

    [TestFixture]
    public class MmvcPrefabPlayModeTests {
        private GameObject _fixtureRoot;

        [UnitySetUp]
        public IEnumerator SetUp() {
            _fixtureRoot = new GameObject("PrefabPlayModeFixtureRoot");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            if (_fixtureRoot != null) {
                Object.Destroy(_fixtureRoot);
            }
            yield return null;
        }

        private GameObject CreatePlayModePrefab(bool activeByDefault) {
            var template = new GameObject("PlayModePrefabTemplate");
            template.transform.SetParent(_fixtureRoot.transform, false);
            template.SetActive(false);

            var canvas = new GameObject("Canvas", typeof(RectTransform));
            canvas.transform.SetParent(template.transform, false);

            var textObj = new GameObject("LevelText", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObj.transform.SetParent(canvas.transform, false);
            var textComp = textObj.GetComponent<TextMeshProUGUI>();

            var model = template.AddComponent<PlayModeOrderedModel>();
            model.EnsureFieldsInitialized();
            model.level.Value = 1;

            var logic = template.AddComponent<PlayModeOrderedLogic>();
            var presenter = template.AddComponent<PlayModeOrderedPresenter>();
            presenter.levelDisplay = textComp;

            if (activeByDefault) {
                template.SetActive(true);
            }

            return template;
        }

        [UnityTest]
        public IEnumerator PlayMode_ActiveClonedPrefab_DefensiveReadiness_AndReactivity() {
            var template = CreatePlayModePrefab(activeByDefault: true);
            yield return null;

            PlayModeExecutionTracker.ExecutionLog.Clear();

            // Instantiate active prefab clone
            var clone = Object.Instantiate(template, _fixtureRoot.transform);
            yield return null;

            var model = clone.GetComponent<PlayModeOrderedModel>();
            var logic = clone.GetComponent<PlayModeOrderedLogic>();
            var presenter = clone.GetComponent<PlayModeOrderedPresenter>();

            Assert.IsNotNull(model);
            Assert.IsNotNull(logic);
            Assert.IsNotNull(presenter);

            // Defensive contract: presenter.EnsureInitialized() guarantees readiness
            presenter.EnsureInitialized();

            // Model mutation propagates in PlayMode
            model.level.Value = 5;
            yield return null;

            Assert.AreEqual("5", presenter.levelDisplay.text);
            Assert.AreEqual(1, presenter.LevelChangeNotifications);

            Object.Destroy(clone);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlayMode_InactiveClonedPrefab_EarlyGetModel_DoesNotDoubleBindOnSetActive() {
            var template = CreatePlayModePrefab(activeByDefault: false);
            yield return null;

            var clone = Object.Instantiate(template, _fixtureRoot.transform);
            Assert.IsFalse(clone.activeSelf);

            var logic = clone.GetComponent<PlayModeOrderedLogic>();
            var presenter = clone.GetComponent<PlayModeOrderedPresenter>();

            // Call GetModel while inactive (early initialization)
            var model = logic.GetModel();
            Assert.IsNotNull(model);
            Assert.AreEqual(1, model.level.Value);

            model.level.Value = 10;
            yield return null;
            Assert.AreEqual(1, presenter.LevelChangeNotifications);

            // Activate clone in PlayMode
            clone.SetActive(true);
            yield return null;

            // Mutate model again after activation: must not double-bind
            int countBefore = presenter.LevelChangeNotifications;
            model.level.Value = 20;
            yield return null;

            Assert.AreEqual(countBefore + 1, presenter.LevelChangeNotifications, "Notifications must not duplicate after SetActive(true)");
            Assert.AreEqual("20", presenter.levelDisplay.text);

            Object.Destroy(clone);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlayMode_EnableDisablePoolingCycles_AndEndOfFrameDestroyCleanup() {
            var template = CreatePlayModePrefab(activeByDefault: true);
            yield return null;

            var clone = Object.Instantiate(template, _fixtureRoot.transform);
            yield return null;

            var model = clone.GetComponent<PlayModeOrderedModel>();
            var presenter = clone.GetComponent<PlayModeOrderedPresenter>();
            presenter.EnsureInitialized();

            // Active update
            model.level.Value = 2;
            yield return null;
            Assert.AreEqual("2", presenter.levelDisplay.text);

            // Recycle to pool: SetActive(false)
            clone.SetActive(false);
            yield return null;

            // Mutate while disabled
            model.level.Value = 3;
            yield return null;

            // Re-activate from pool: SetActive(true)
            clone.SetActive(true);
            yield return null;

            // Reactive update after reactivating
            model.level.Value = 4;
            yield return null;
            Assert.AreEqual("4", presenter.levelDisplay.text);

            // End-of-frame Destroy cleanup
            Object.Destroy(clone);
            yield return null; // Frame 1: destruction queued
            yield return null; // Frame 2: destruction executed

            // Ensure no errors occurred
            Assert.IsTrue(clone == null);
        }
    }
}
