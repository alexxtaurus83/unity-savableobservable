using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SavableObservable;
using SavableObservable.Tests.Contracts;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace SavableObservable.Tests {
    public enum LiveUiBadgeType {
        None,
        Bronze,
        Silver,
        Gold
    }

    [Serializable]
    public class LiveUiFeatureModel : BaseObservableDataModel {
        public ObservableVariable<string> title = new ObservableVariable<string>();
        public ObservableVariable<int> level = new ObservableVariable<int>();
        public ObservableVariable<bool> active = new ObservableVariable<bool>();
        public ObservableVariable<LiveUiBadgeType> badge = new ObservableVariable<LiveUiBadgeType>();
        public ObservableVariable<float> progress = new ObservableVariable<float>();
        public ObservableVariable<bool> isAnimating = new ObservableVariable<bool>();
        public ObservableList<string> tags = new ObservableList<string>();
    }

    [DisallowMultipleComponent]
    public class LiveUiFeaturePresenter : BaseObservablePresenter<LiveUiFeatureModel> {
        [AutoBind("title")] public TextMeshProUGUI titleText;
        [AutoBind("level")] public Slider levelSlider;
        [AutoBind("active")] public Toggle activeToggle;
        [AutoBind("title")] public TMP_InputField titleInput;

        public Image badgeImage;
        public Image statusIndicator;
        public CanvasGroup panelGroup;
        public Transform tagsContainer;

        public Sprite bronzeSprite;
        public Sprite silverSprite;
        public Sprite goldSprite;

        public int badgeHandlerCalls;
        public int statusHandlerCalls;
        public int panelHandlerCalls;
        public int animHandlerCalls;
        public int tagsHandlerCalls;

        public int lastLevelCurrent;
        public int lastLevelPrevious;
        public bool isAnimationRunning;

        [ObservableHandler("badge")]
        private void OnBadgeChanged(LiveUiBadgeType current) {
            badgeHandlerCalls++;
            if (badgeImage == null) return;
            switch (current) {
                case LiveUiBadgeType.Bronze: badgeImage.sprite = bronzeSprite; break;
                case LiveUiBadgeType.Silver: badgeImage.sprite = silverSprite; break;
                case LiveUiBadgeType.Gold: badgeImage.sprite = goldSprite; break;
                default: badgeImage.sprite = null; break;
            }
        }

        [ObservableHandler("active")]
        private void OnActiveStatus(bool current) {
            statusHandlerCalls++;
            if (statusIndicator != null) {
                statusIndicator.enabled = current;
                statusIndicator.color = current ? Color.green : Color.red;
            }
        }

        [ObservableHandler("active")]
        private void OnPanelState(bool current) {
            panelHandlerCalls++;
            if (panelGroup != null) {
                panelGroup.alpha = current ? 1.0f : 0.4f;
                panelGroup.interactable = current;
            }
        }

        [ObservableHandler("level")]
        private void OnLevel(int current, int previous) {
            lastLevelCurrent = current;
            lastLevelPrevious = previous;
        }

        [ObservableHandler("isAnimating")]
        private void OnAnimationToggle(bool current) {
            animHandlerCalls++;
            isAnimationRunning = current;
        }

        [ObservableHandler("tags")]
        private void OnTags(ObservableList<string> list) {
            tagsHandlerCalls++;
            if (tagsContainer == null) return;
            for (int i = tagsContainer.childCount - 1; i >= 0; i--) {
                Destroy(tagsContainer.GetChild(i).gameObject);
            }
            for (int i = 0; i < list.Count; i++) {
                var go = new GameObject("Tag_" + list[i], typeof(RectTransform));
                go.transform.SetParent(tagsContainer, false);
            }
        }
    }

    public class BrokenSampleModel : BaseObservableDataModel {
        public ObservableVariable<int> validScore = new ObservableVariable<int>();
    }

    public class BrokenSamplePresenter : BaseObservablePresenter<BrokenSampleModel> {
        [AutoBind("nonExistentVariable")] public Slider brokenSlider;
        [AutoBind("validScore")] public Slider nullSlider;
    }

    [TestFixture]
    [Category("LiveUi")]
    public class MmvcLiveUiPlayModeTests {
        private GameObject _root;
        private GameObject _unitGo;
        private LiveUiFeatureModel _model;
        private LiveUiFeaturePresenter _presenter;

        private Sprite _bronze;
        private Sprite _silver;
        private Sprite _gold;

        [UnitySetUp]
        public IEnumerator SetUp() {
            _root = new GameObject("LiveUiRoot");
            var canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(_root.transform, false);

            _bronze = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.zero);
            _bronze.name = "BronzeSprite";
            _silver = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.zero);
            _silver.name = "SilverSprite";
            _gold = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.zero);
            _gold.name = "GoldSprite";

            var titleGo = new GameObject("TitleText", typeof(RectTransform), typeof(TextMeshProUGUI));
            titleGo.transform.SetParent(canvasGo.transform, false);

            var sliderGo = new GameObject("LevelSlider", typeof(RectTransform), typeof(Slider));
            sliderGo.transform.SetParent(canvasGo.transform, false);
            var slider = sliderGo.GetComponent<Slider>();
            slider.minValue = 1;
            slider.maxValue = 50;
            slider.wholeNumbers = true;

            var toggleGo = new GameObject("ActiveToggle", typeof(RectTransform), typeof(Toggle));
            toggleGo.transform.SetParent(canvasGo.transform, false);

            var inputGo = new GameObject("TitleInput", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            inputGo.transform.SetParent(canvasGo.transform, false);
            var input = inputGo.GetComponent<TMP_InputField>();
            var inputTextArea = new GameObject("TextArea", typeof(RectTransform), typeof(TextMeshProUGUI));
            inputTextArea.transform.SetParent(inputGo.transform, false);
            input.textComponent = inputTextArea.GetComponent<TextMeshProUGUI>();

            var badgeGo = new GameObject("BadgeImage", typeof(RectTransform), typeof(Image));
            badgeGo.transform.SetParent(canvasGo.transform, false);

            var statusGo = new GameObject("StatusIndicator", typeof(RectTransform), typeof(Image));
            statusGo.transform.SetParent(canvasGo.transform, false);

            var panelGo = new GameObject("PanelGroup", typeof(RectTransform), typeof(CanvasGroup));
            panelGo.transform.SetParent(canvasGo.transform, false);

            var tagsGo = new GameObject("TagsContainer", typeof(RectTransform));
            tagsGo.transform.SetParent(canvasGo.transform, false);

            _unitGo = new GameObject("LiveUiUnit");
            _unitGo.SetActive(false);
            _unitGo.transform.SetParent(_root.transform, false);

            _model = _unitGo.AddComponent<LiveUiFeatureModel>();
            _presenter = _unitGo.AddComponent<LiveUiFeaturePresenter>();

            _presenter.titleText = titleGo.GetComponent<TextMeshProUGUI>();
            _presenter.levelSlider = slider;
            _presenter.activeToggle = toggleGo.GetComponent<Toggle>();
            _presenter.titleInput = input;
            _presenter.badgeImage = badgeGo.GetComponent<Image>();
            _presenter.statusIndicator = statusGo.GetComponent<Image>();
            _presenter.panelGroup = panelGo.GetComponent<CanvasGroup>();
            _presenter.tagsContainer = tagsGo.transform;

            _presenter.bronzeSprite = _bronze;
            _presenter.silverSprite = _silver;
            _presenter.goldSprite = _gold;

            _model.EnsureFieldsInitialized();
            _presenter.EnsureInitialized();
            _unitGo.SetActive(true);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            if (_bronze != null) UnityEngine.Object.Destroy(_bronze);
            if (_silver != null) UnityEngine.Object.Destroy(_silver);
            if (_gold != null) UnityEngine.Object.Destroy(_gold);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TmpText_AutoBind_UpdatesViaModelToView() {
            yield return MmvcLiveUiAssert.ModelToView(_model.title, "New Hero", () => MmvcLiveUiAssert.Text(_presenter.titleText), "New Hero");
        }

        [UnityTest]
        public IEnumerator ImageSprite_AndStatusHandler_UpdatesVisuals() {
            _model.badge.Value = LiveUiBadgeType.Gold;
            yield return null;

            Assert.AreEqual("GoldSprite", MmvcLiveUiAssert.SpriteName(_presenter.badgeImage));
            Assert.AreEqual(1, _presenter.badgeHandlerCalls);

            _model.active.Value = true;
            yield return null;
            Assert.IsTrue(MmvcLiveUiAssert.IsEnabled(_presenter.statusIndicator));
            Assert.AreEqual(Color.green, _presenter.statusIndicator.color);

            _model.active.Value = false;
            yield return null;
            Assert.IsFalse(MmvcLiveUiAssert.IsEnabled(_presenter.statusIndicator));
            Assert.AreEqual(Color.red, _presenter.statusIndicator.color);
        }

        [UnityTest]
        public IEnumerator CanvasGroupAlpha_AndInteractable_DrivenByHandler() {
            _model.active.Value = true;
            yield return null;
            Assert.AreEqual(1.0f, MmvcLiveUiAssert.Alpha(_presenter.panelGroup), 0.01f);
            Assert.IsTrue(_presenter.panelGroup.interactable);

            _model.active.Value = false;
            yield return null;
            Assert.AreEqual(0.4f, MmvcLiveUiAssert.Alpha(_presenter.panelGroup), 0.01f);
            Assert.IsFalse(_presenter.panelGroup.interactable);
        }

        [UnityTest]
        public IEnumerator Slider_Toggle_InputField_BothDirections_WithAllThreeSubscriptionPaths() {
            int manualLevelCalls = 0;
            int manualToggleCalls = 0;
            using (MmvcLiveUiAssert.SubscribeTracked(_model.level, this, _ => manualLevelCalls++))
            using (MmvcLiveUiAssert.SubscribeTracked(_model.active, this, _ => manualToggleCalls++)) {
                // UI -> Model via Driver
                yield return MmvcLiveUiAssert.SetSlider(_presenter.levelSlider, 25f);
                Assert.AreEqual(25, _model.level.Value);
                Assert.AreEqual(1, manualLevelCalls);
                Assert.AreEqual(25, _presenter.lastLevelCurrent);

                yield return MmvcLiveUiAssert.SetToggle(_presenter.activeToggle, true);
                Assert.IsTrue(_model.active.Value);
                Assert.AreEqual(1, manualToggleCalls);

                yield return MmvcLiveUiAssert.Type(_presenter.titleInput, "Grandmaster");
                Assert.AreEqual("Grandmaster", _model.title.Value);
                Assert.AreEqual("Grandmaster", MmvcLiveUiAssert.Text(_presenter.titleText));
            }
        }

        [UnityTest]
        public IEnumerator ObservableList_AddRemoveClear_RenderedByHandler_AndNotifiesManualSub() {
            int manualListCalls = 0;
            using (MmvcLiveUiAssert.SubscribeTrackedList(_model.tags, this, _ => manualListCalls++)) {
                _model.tags.Add("Agile");
                _model.tags.Add("Speed");
                yield return null;

                Assert.AreEqual(2, _presenter.tagsHandlerCalls);
                Assert.AreEqual(2, manualListCalls);
                Assert.AreEqual(2, _presenter.tagsContainer.childCount);

                _model.tags.Remove("Agile");
                yield return null;

                Assert.AreEqual(3, _presenter.tagsHandlerCalls);
                Assert.AreEqual(3, manualListCalls);
                Assert.AreEqual(1, _presenter.tagsContainer.childCount);

                _model.tags.Clear();
                yield return null;

                Assert.AreEqual(4, _presenter.tagsHandlerCalls);
                Assert.AreEqual(4, manualListCalls);
                Assert.AreEqual(0, _presenter.tagsContainer.childCount);
            }
        }

        [UnityTest]
        public IEnumerator EnumDrivenSpriteSwap_UpdatesCorrectly() {
            _model.badge.Value = LiveUiBadgeType.Bronze;
            yield return null;
            Assert.AreEqual("BronzeSprite", MmvcLiveUiAssert.SpriteName(_presenter.badgeImage));

            _model.badge.Value = LiveUiBadgeType.Silver;
            yield return null;
            Assert.AreEqual("SilverSprite", MmvcLiveUiAssert.SpriteName(_presenter.badgeImage));

            _model.badge.Value = LiveUiBadgeType.Gold;
            yield return null;
            Assert.AreEqual("GoldSprite", MmvcLiveUiAssert.SpriteName(_presenter.badgeImage));

            _model.badge.Value = LiveUiBadgeType.None;
            yield return null;
            Assert.IsNull(_presenter.badgeImage.sprite);
        }

        [UnityTest]
        public IEnumerator ObservableHandler_CurrentPrevious_CapturesValues() {
            _model.level.Value = 10;
            yield return null;
            Assert.AreEqual(10, _presenter.lastLevelCurrent);
            Assert.AreEqual(0, _presenter.lastLevelPrevious);

            _model.level.Value = 20;
            yield return null;
            Assert.AreEqual(20, _presenter.lastLevelCurrent);
            Assert.AreEqual(10, _presenter.lastLevelPrevious);
        }

        [UnityTest]
        public IEnumerator HandlerDrivenAnimation_TogglesState() {
            _model.isAnimating.Value = true;
            yield return null;
            Assert.IsTrue(_presenter.isAnimationRunning);
            Assert.AreEqual(1, _presenter.animHandlerCalls);

            _model.isAnimating.Value = false;
            yield return null;
            Assert.IsFalse(_presenter.isAnimationRunning);
            Assert.AreEqual(2, _presenter.animHandlerCalls);
        }

        [UnityTest]
        public IEnumerator DestroyedPresenter_DoesNotThrow_AndManualSubscriptionWorks() {
            int manualCalls = 0;
            using (MmvcLiveUiAssert.SubscribeTracked(_model.level, this, _ => manualCalls++)) {
                UnityEngine.Object.Destroy(_presenter);
                yield return null;

                LogAssert.NoUnexpectedReceived();
                _model.level.Value = 42;
                yield return null;

                Assert.AreEqual(1, manualCalls, "Manual subscription continues working after presenter destroy.");
                Assert.AreEqual(42, _model.level.Value);
            }
        }

        [UnityTest]
        public IEnumerator InactivePrefab_ClonedThenActivated_InitializesCleanly() {
            var prefabHost = new GameObject("InactivePrefabHost");
            prefabHost.SetActive(false);
            prefabHost.transform.SetParent(_root.transform, false);

            var sliderGo = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            sliderGo.transform.SetParent(prefabHost.transform, false);

            var model = prefabHost.AddComponent<LiveUiFeatureModel>();
            var presenter = prefabHost.AddComponent<LiveUiFeaturePresenter>();
            presenter.levelSlider = sliderGo.GetComponent<Slider>();
            presenter.levelSlider.minValue = 0;
            presenter.levelSlider.maxValue = 100;

            // Clone inactive
            var clone = UnityEngine.Object.Instantiate(prefabHost, _root.transform, false);
            var cloneModel = clone.GetComponent<LiveUiFeatureModel>();
            var clonePresenter = clone.GetComponent<LiveUiFeaturePresenter>();

            clonePresenter.EnsureInitialized();
            cloneModel.level.Value = 33;
            clone.SetActive(true);
            yield return null;

            Assert.AreEqual(33f, clonePresenter.levelSlider.value);

            UnityEngine.Object.Destroy(prefabHost);
            UnityEngine.Object.Destroy(clone);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Audit_FailsWithReadableMessage_OnBrokenSamplePresenter() {
            var brokenGo = new GameObject("BrokenHost");
            brokenGo.transform.SetParent(_root.transform, false);

            var model = brokenGo.AddComponent<BrokenSampleModel>();
            var presenter = brokenGo.AddComponent<BrokenSamplePresenter>();

            var sliderGo = new GameObject("BrokenSlider", typeof(RectTransform), typeof(Slider));
            sliderGo.transform.SetParent(brokenGo.transform, false);
            presenter.brokenSlider = sliderGo.GetComponent<Slider>();
            presenter.nullSlider = null; // Deliberate null control

            model.EnsureFieldsInitialized();

            var report = new LiveUiAuditReport();
            var options = new LiveUiAuditOptions { UsePrefsSandbox = false };

            yield return MmvcLiveUiAudit.RunAudit(new[] { presenter }, report, options);

            Assert.Greater(report.FailedCount, 0, "Audit must fail on broken presenter.");
            bool hasMissingVar = false;
            bool hasNullControl = false;
            foreach (var r in report.Results) {
                if (r.Status == AuditStatus.Failed) {
                    if (r.Reason.Contains("nonExistentVariable")) hasMissingVar = true;
                    if (r.Reason.Contains("null or not a Component")) hasNullControl = true;
                }
            }

            Assert.IsTrue(hasMissingVar, "Report must identify nonExistentVariable failure.");
            Assert.IsTrue(hasNullControl, "Report must identify null control failure.");

            UnityEngine.Object.Destroy(brokenGo);
            yield return null;
        }
    }
}
