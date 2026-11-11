using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;
using SavableObservable;

namespace SavableObservable.Tests {

    [TestFixture]
    public class CommandBindingPlayModeTests {

        private GameObject _root;
        private RuntimeTestModel _model;
        private RuntimeTestLogic _logic;
        private RuntimeTestPresenter _presenter;
        private Slider _slider;
        private Button _button;

        public class RuntimeTestModel : BaseObservableDataModel {
            public ObservableVariable<float> volume = new ObservableVariable<float>();
        }

        public class RuntimeTestLogic : BaseLogic<RuntimeTestModel, RuntimeTestPresenter> {
            public int VolumeCallCount;
            public int ActionCount;
            public bool DestroySelfOnCommand;

            [UnityEngine.Scripting.Preserve]
            public void SetVolume(float val) {
                VolumeCallCount++;
                if (DestroySelfOnCommand) {
                    UnityEngine.Object.Destroy(gameObject);
                } else {
                    GetModel().volume.Value = Mathf.Clamp01(val);
                }
            }

            [UnityEngine.Scripting.Preserve]
            public void DoAction() {
                ActionCount++;
                if (DestroySelfOnCommand) {
                    UnityEngine.Object.Destroy(gameObject);
                }
            }
        }

        public class RuntimeTestPresenter : ObservablePresenterWithLogic<RuntimeTestModel, RuntimeTestLogic> {
            [AutoBind(nameof(RuntimeTestModel.volume), Command = nameof(RuntimeTestLogic.SetVolume))]
            public Slider volumeSlider;

            [ClickCommand(nameof(RuntimeTestLogic.DoAction))]
            public Button actionButton;
        }

        [UnitySetUp]
        public IEnumerator SetUp() {
            _root = new GameObject("CommandPlayModeRoot");

            var canvasObj = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            canvasObj.transform.SetParent(_root.transform, false);

            var sliderObj = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            sliderObj.transform.SetParent(canvasObj.transform, false);
            _slider = sliderObj.GetComponent<Slider>();
            _slider.minValue = 0f;
            _slider.maxValue = 2f;

            var buttonObj = new GameObject("Button", typeof(RectTransform), typeof(Button));
            buttonObj.transform.SetParent(canvasObj.transform, false);
            _button = buttonObj.GetComponent<Button>();

            var unitObj = new GameObject("UnitHost");
            unitObj.SetActive(false);
            unitObj.transform.SetParent(_root.transform, false);

            _model = unitObj.AddComponent<RuntimeTestModel>();
            _model.volume.Value = 0.5f;

            _logic = unitObj.AddComponent<RuntimeTestLogic>();
            _presenter = unitObj.GetComponent<RuntimeTestPresenter>() ?? unitObj.AddComponent<RuntimeTestPresenter>();

            _presenter.volumeSlider = _slider;
            _presenter.actionButton = _button;

            unitObj.SetActive(true);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            if (_root != null) {
                UnityEngine.Object.Destroy(_root);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator RuntimeCommand_SliderInput_UpdatesModelAndReconciles() {
            _slider.value = 1.5f;
            yield return null;

            Assert.AreEqual(1, _logic.VolumeCallCount);
            Assert.AreEqual(1.0f, _model.volume.Value);
            Assert.AreEqual(1.0f, _slider.value);
        }

        [UnityTest]
        public IEnumerator RuntimeCommand_ClickCommand_FiresZeroArgAction() {
            _button.onClick.Invoke();
            yield return null;

            Assert.AreEqual(1, _logic.ActionCount);
        }

        [UnityTest]
        public IEnumerator DestructionOrder_PresenterDestroyedFirst_NoExceptionsOrCallbacksOnInput() {
            UnityEngine.Object.Destroy(_presenter);
            yield return null;

            _slider.value = 0.2f;
            yield return null;

            // Command was not invoked because presenter was destroyed
            Assert.AreEqual(0, _logic.VolumeCallCount);
            Assert.AreEqual(0.5f, _model.volume.Value);
        }

        [UnityTest]
        public IEnumerator DestructionOrder_ModelDestroyedFirst_NoExceptionsOrCallbacksOnInput() {
            UnityEngine.Object.Destroy(_model);
            yield return null;

            Assert.DoesNotThrow(() => _slider.value = 0.2f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DestructionOrder_ControlDestroyedFirst_ModelUpdatesDoNotThrow() {
            UnityEngine.Object.Destroy(_slider.gameObject);
            yield return null;

            Assert.DoesNotThrow(() => _model.volume.Value = 0.9f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DestructionOrder_CommandTriggeredDestruction_SucceedsCleanly() {
            _logic.DestroySelfOnCommand = true;

            Assert.DoesNotThrow(() => _button.onClick.Invoke());
            yield return null;

            Assert.AreEqual(1, _logic.ActionCount);
        }

        [UnityTest]
        public IEnumerator DisableAndReEnable_PreservesListenersWithoutDuplicates() {
            _presenter.gameObject.SetActive(false);
            yield return null;

            _presenter.gameObject.SetActive(true);
            yield return null;

            _slider.value = 0.7f;
            yield return null;

            Assert.AreEqual(1, _logic.VolumeCallCount);
            Assert.AreEqual(0.7f, _model.volume.Value);
        }
    }
}
