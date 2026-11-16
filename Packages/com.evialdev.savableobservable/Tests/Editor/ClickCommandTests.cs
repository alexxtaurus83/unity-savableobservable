using System;
using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;
using UnityEngine.UI;
using TMPro;
using SavableObservable;

namespace SavableObservable.Tests {

    [TestFixture]
    public class ClickCommandTests {

        private GameObject _host;
        private TestModel _model;
        private TestLogic _logic;
        private TestPresenter _presenter;

        public class TestModel : BaseObservableDataModel {
            public ObservableVariable<int> score = new ObservableVariable<int>();
        }

        public class TestLogic : BaseLogic<TestModel, TestPresenter> {
            public int ResetCount;
            public int InheritedCount;
            public bool ThrowOnNextClick;

            [UnityEngine.Scripting.Preserve]
            public void ResetScore() {
                if (ThrowOnNextClick) throw new InvalidOperationException("Simulated reset failure");
                ResetCount++;
            }

            [UnityEngine.Scripting.Preserve]
            public void InheritedAction() {
                InheritedCount++;
            }

            public async void AsyncVoidMethod() {
                await Task.Yield();
            }

            public Task TaskMethod() => Task.CompletedTask;

            public IEnumerator CoroutineMethod() {
                yield break;
            }

            public int NonVoidReturnMethod() => 42;

            public void MethodWithParam(int x) { }

            private void PrivateMethod() { }
        }

        public class BaseTestPresenter : ObservablePresenterWithLogic<TestModel, TestLogic> {
            [SerializeField, ClickCommand(nameof(TestLogic.InheritedAction))]
            public Button baseButton;
        }

        public class TestPresenter : BaseTestPresenter {
            [SerializeField, ClickCommand(nameof(TestLogic.ResetScore))]
            public Button resetButton;

            [SerializeField, ClickCommand(nameof(TestLogic.ResetScore))]
            public Button unassignedButton;

            public bool ClearListenersInInit;
            public bool ThrowInInitOnce;
            public bool AssignButtonInInit;
            private bool _threw;

            protected override void InitializePresenter() {
                base.InitializePresenter();
                if (AssignButtonInInit) resetButton = GetComponent<Button>();
                if (ThrowInInitOnce && !_threw) {
                    _threw = true;
                    throw new InvalidOperationException("Simulated init failure");
                }
                if (ClearListenersInInit) {
                    Listeners.Clear();
                }
            }
        }

        public class PlainPresenterWithoutLogic : BaseObservablePresenter<TestModel> {
            [SerializeField, ClickCommand("SomeCommand")]
            public Button actionButton;
        }

        public class NonButtonLogic : BaseLogic<TestModel, NonButtonFieldPresenter> {
            public void ResetScore() { }
        }

        public class NonButtonFieldPresenter : ObservablePresenterWithLogic<TestModel, NonButtonLogic> {
            [SerializeField, ClickCommand(nameof(NonButtonLogic.ResetScore))]
            public TextMeshProUGUI notAButton;
        }

        public class ConflictingLogic : BaseLogic<TestModel, ConflictingAttributesPresenter> {
            public void ResetScore() { }
        }

        public class ConflictingAttributesPresenter : ObservablePresenterWithLogic<TestModel, ConflictingLogic> {
            [SerializeField, ClickCommand(nameof(ConflictingLogic.ResetScore)), AutoBind("score")]
            public Button conflictedButton;
        }

        public class UnsupportedLogic : BaseLogic<TestModel, UnsupportedSignaturesPresenter> {
            public async void AsyncVoidMethod() {
                await Task.Yield();
            }

            public Task TaskMethod() => Task.CompletedTask;

            public IEnumerator CoroutineMethod() {
                yield break;
            }

            public int NonVoidReturnMethod() => 42;

            public void MethodWithParam(int x) { }
        }

        public class UnsupportedSignaturesPresenter : ObservablePresenterWithLogic<TestModel, UnsupportedLogic> {
            [SerializeField, ClickCommand(nameof(UnsupportedLogic.AsyncVoidMethod))]
            public Button asyncVoidBtn;

            [SerializeField, ClickCommand(nameof(UnsupportedLogic.TaskMethod))]
            public Button taskBtn;

            [SerializeField, ClickCommand(nameof(UnsupportedLogic.CoroutineMethod))]
            public Button coroutineBtn;

            [SerializeField, ClickCommand(nameof(UnsupportedLogic.NonVoidReturnMethod))]
            public Button nonVoidBtn;

            [SerializeField, ClickCommand(nameof(UnsupportedLogic.MethodWithParam))]
            public Button paramBtn;
        }

        [SetUp]
        public void SetUp() {
            _host = new GameObject("TestHost");
            _host.SetActive(false);
            _model = _host.AddComponent<TestModel>();
            _logic = _host.AddComponent<TestLogic>();
            _presenter = _host.GetComponent<TestPresenter>() ?? _host.AddComponent<TestPresenter>();

            _presenter.resetButton = _host.AddComponent<Button>();
            _presenter.baseButton = new GameObject("BaseButton").AddComponent<Button>();
            _host.SetActive(true);
        }

        [Test]
        public void ForeignModelCleanup_PreservesPresenterListenersAndClickCommands() {
            _presenter.EnsureInitialized();
            var foreignHost = new GameObject("ForeignModel");
            foreignHost.SetActive(false);
            try {
                var foreign = foreignHost.AddComponent<TestModel>();
                foreign.EnsureFieldsInitialized();
                foreign.score.OnValueChanged.Add(_ => { }, _presenter);
                int manualClicks = 0;
                _presenter.Listeners.Listen(_presenter.resetButton.onClick, () => manualClicks++);
                Observable.CleanupSubscriptions(foreign);
                _presenter.resetButton.onClick.Invoke();
                Assert.AreEqual(1, manualClicks);
                Assert.AreEqual(1, _logic.ResetCount);
                Assert.AreEqual(0, foreign.score.OnValueChanged.HandlerCount);
            } finally { UnityEngine.Object.DestroyImmediate(foreignHost); }
        }

        [Test]
        public void ClickCommand_RebindReplacesButtonAndRecoversAfterCleanup() {
            _presenter.EnsureInitialized();
            var old = _presenter.resetButton;
            var child = new GameObject("ReplacementButton");
            child.transform.SetParent(_host.transform);
            _presenter.resetButton = child.AddComponent<Button>();
            Observable.SetListeners(_presenter, _model);
            old.onClick.Invoke();
            Assert.AreEqual(0, _logic.ResetCount);
            _presenter.resetButton.onClick.Invoke();
            Assert.AreEqual(1, _logic.ResetCount);
            Observable.CleanupSubscriptions(_model);
            _presenter.EnsureInitialized();
            _presenter.resetButton.onClick.Invoke();
            Assert.AreEqual(2, _logic.ResetCount);
        }

        [Test]
        public void ClickCommand_ButtonAssignedInInitializePresenter_IsWired() {
            var host = new GameObject("LateButtonHost");
            host.SetActive(false);
            try {
                host.AddComponent<TestModel>();
                var logic = host.AddComponent<TestLogic>();
                var presenter = host.GetComponent<TestPresenter>() ?? host.AddComponent<TestPresenter>();
                var button = host.AddComponent<Button>();
                presenter.AssignButtonInInit = true;
                presenter.EnsureInitialized();
                button.onClick.Invoke();
                Assert.AreEqual(1, logic.ResetCount);
            } finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [TearDown]
        public void TearDown() {
            if (_presenter != null && _presenter.baseButton != null) {
                UnityEngine.Object.DestroyImmediate(_presenter.baseButton.gameObject);
            }
            if (_host != null) {
                UnityEngine.Object.DestroyImmediate(_host);
            }
        }

        [Test]
        public void ClickCommand_FiresCommand_ExactlyOnce() {
            _presenter.EnsureInitialized();

            _presenter.resetButton.onClick.Invoke();
            Assert.AreEqual(1, _logic.ResetCount);

            _presenter.resetButton.onClick.Invoke();
            Assert.AreEqual(2, _logic.ResetCount);
        }

        [Test]
        public void ClickCommand_InheritedField_DiscoveredAndWired() {
            _presenter.EnsureInitialized();

            _presenter.baseButton.onClick.Invoke();
            Assert.AreEqual(1, _logic.InheritedCount);
        }

        [Test]
        public void ClickCommand_Survives_ListenersClearInInitializePresenter() {
            _presenter.ClearListenersInInit = true;
            _presenter.EnsureInitialized();

            _presenter.resetButton.onClick.Invoke();
            Assert.AreEqual(1, _logic.ResetCount);
        }

        [Test]
        public void ClickCommand_UnassignedButtonField_SkippedSilently() {
            _presenter.unassignedButton = null;
            Assert.DoesNotThrow(() => _presenter.EnsureInitialized());
        }

        [Test]
        public void ClickCommand_DestroyedButtonField_SkippedSilently() {
            var tempBtnHost = new GameObject("TempBtn");
            _presenter.unassignedButton = tempBtnHost.AddComponent<Button>();
            UnityEngine.Object.DestroyImmediate(tempBtnHost);

            Assert.DoesNotThrow(() => _presenter.EnsureInitialized());
        }

        [Test]
        public void ClickCommand_InitRetryAndRebinding_DoNotDuplicateListener() {
            _presenter.ThrowInInitOnce = true;

            Assert.Throws<InvalidOperationException>(() => _presenter.EnsureInitialized());

            // Second attempt succeeds
            _presenter.EnsureInitialized();

            // Rebinding via SetListeners
            Observable.SetListeners(_presenter, _model);

            _presenter.resetButton.onClick.Invoke();
            Assert.AreEqual(1, _logic.ResetCount);
        }

        [Test]
        public void ClickCommand_PresenterDestruction_RemovesListener() {
            _presenter.EnsureInitialized();

            var btn = _presenter.resetButton;
            UnityEngine.Object.DestroyImmediate(_presenter);

            // Button click after presenter destruction should not invoke command or throw
            Assert.DoesNotThrow(() => btn.onClick.Invoke());
            Assert.AreEqual(0, _logic.ResetCount);
        }

        [Test]
        public void ClickCommand_ExceptionInCommand_LoggedOnce_DoesNotThrow() {
            _presenter.EnsureInitialized();
            _logic.ThrowOnNextClick = true;

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("ClickCommand 'ResetScore' on button 'resetButton'.*threw an exception: Simulated reset failure"));

            Assert.DoesNotThrow(() => _presenter.resetButton.onClick.Invoke());
        }

        [Test]
        public void ClickCommand_PreservesInspectorPersistentListeners() {
            int persistentCalls = 0;
            UnityAction persistentAction = () => persistentCalls++;
            _presenter.resetButton.onClick.AddListener(persistentAction);

            _presenter.EnsureInitialized();

            _presenter.resetButton.onClick.Invoke();
            Assert.AreEqual(1, _logic.ResetCount);
            Assert.AreEqual(1, persistentCalls);
        }

        [Test]
        public void ClickCommand_NonButtonField_LogsErrorAndDoesNotWire() {
            var go = new GameObject("NonButtonHost");
            try {
                go.SetActive(false);
                go.AddComponent<TestModel>();
                go.AddComponent<NonButtonLogic>();
                var p = go.GetComponent<NonButtonFieldPresenter>() ?? go.AddComponent<NonButtonFieldPresenter>();
                go.SetActive(true);

                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Invalid \\[ClickCommand\\] on field 'notAButton'"));
                p.EnsureInitialized();
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Invalid \\[ClickCommand\\] on field 'notAButton'"));
                Observable.SetListeners(p, go.GetComponent<TestModel>());
            } finally {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ClickCommand_ConflictedAttributes_LogsErrorAndDoesNotWire() {
            var go = new GameObject("ConflictedHost");
            try {
                go.SetActive(false);
                go.AddComponent<TestModel>();
                go.AddComponent<ConflictingLogic>();
                var p = go.GetComponent<ConflictingAttributesPresenter>() ?? go.AddComponent<ConflictingAttributesPresenter>();
                p.conflictedButton = go.AddComponent<Button>();
                go.SetActive(true);

                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("cannot have both \\[ClickCommand\\] and \\[AutoBind\\]"));
                p.EnsureInitialized();

                p.conflictedButton.onClick.Invoke();
            } finally {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ClickCommand_PlainPresenterWithoutLogic_LogsError() {
            var go = new GameObject("PlainHost");
            try {
                go.SetActive(false);
                go.AddComponent<TestModel>();
                var p = go.AddComponent<PlainPresenterWithoutLogic>();
                p.actionButton = go.AddComponent<Button>();
                go.SetActive(true);

                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("requires a paired Logic component, but PlainPresenterWithoutLogic does not implement IAutoBindCommandTarget"));
                p.EnsureInitialized();

                p.actionButton.onClick.Invoke();
            } finally {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ClickCommand_UnsupportedSignatures_RejectedWithErrors() {
            var go = new GameObject("UnsupportedHost");
            var btnList = new System.Collections.Generic.List<GameObject>();
            try {
                go.SetActive(false);
                go.AddComponent<TestModel>();
                go.AddComponent<UnsupportedLogic>();
                var p = go.GetComponent<UnsupportedSignaturesPresenter>() ?? go.AddComponent<UnsupportedSignaturesPresenter>();

                Button CreateBtn(string name) {
                    var bGo = new GameObject(name);
                    btnList.Add(bGo);
                    return bGo.AddComponent<Button>();
                }

                p.asyncVoidBtn = CreateBtn("AsyncVoidBtn");
                p.taskBtn = CreateBtn("TaskBtn");
                p.coroutineBtn = CreateBtn("CoroutineBtn");
                p.nonVoidBtn = CreateBtn("NonVoidBtn");
                p.paramBtn = CreateBtn("ParamBtn");
                go.SetActive(true);

                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Invalid \\[ClickCommand\\] 'AsyncVoidMethod'.*is async void"));
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Invalid \\[ClickCommand\\] 'TaskMethod'.*returns 'Task'"));
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Invalid \\[ClickCommand\\] 'CoroutineMethod'.*returns 'IEnumerator'"));
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Invalid \\[ClickCommand\\] 'NonVoidReturnMethod'.*returns 'Int32'"));
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Invalid \\[ClickCommand\\] 'MethodWithParam'.*has 1 parameters"));

                p.EnsureInitialized();
            } finally {
                foreach (var b in btnList) {
                    if (b != null) UnityEngine.Object.DestroyImmediate(b);
                }
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
