using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SavableObservable;
using SavableObservable.Samples;

namespace SavableObservable.Tests {
    [TestFixture]
    public class MMVCRestrictionsTests {
        private GameObject _goA;
        private GameObject _goB;

        [SetUp]
        public void SetUp() {
            _goA = new GameObject("UnitA");
            _goB = new GameObject("UnitB");
        }

        [TearDown]
        public void TearDown() {
            if (_goA != null) UnityEngine.Object.DestroyImmediate(_goA);
            if (_goB != null) UnityEngine.Object.DestroyImmediate(_goB);
        }

        [Test]
        public void CrossGameObject_Binding_IsRejectedWithLogError() {
            var modelA = _goA.AddComponent<SamplePlayerModel>();
            var presenterB = _goB.AddComponent<SamplePlayerPresenter>();

            LogAssert.Expect(LogType.Error, new Regex(@"Cross-GameObject binding rejected in SetListeners"));

            Observable.SetListeners(presenterB, modelA);
        }

        [Test]
        public void ValidateSingleMmvc_Rules_EnforcedViaDisallowMultipleComponent() {
            // Rule 1: One GameObject = one model.
            // Both BaseObservableDataModel and its subclasses are protected by [DisallowMultipleComponent].
            var modelAttrs = typeof(BaseObservableDataModel).GetCustomAttributes(typeof(DisallowMultipleComponent), true);
            Assert.IsTrue(modelAttrs.Length > 0, "BaseObservableDataModel must have [DisallowMultipleComponent]");

            // Rule 2: One GameObject = one presenter.
            // BasePresenter<M> and BaseObservablePresenter<M> are protected by [DisallowMultipleComponent].
            var presenterAttrs = typeof(BasePresenter<SamplePlayerModel>).GetCustomAttributes(typeof(DisallowMultipleComponent), true);
            Assert.IsTrue(presenterAttrs.Length > 0, "BasePresenter must have [DisallowMultipleComponent]");

            // Rule 3: One GameObject = one logic.
            // BaseLogic<M> is protected by [DisallowMultipleComponent].
            var logicAttrs = typeof(BaseLogic<SamplePlayerModel>).GetCustomAttributes(typeof(DisallowMultipleComponent), true);
            Assert.IsTrue(logicAttrs.Length > 0, "BaseLogic must have [DisallowMultipleComponent]");
        }

        [Test]
        public void ComponentAutoRequire_RejectsNonComponentTypes() {
            var dummy = _goA.AddComponent<SamplePlayerModel>();

            LogAssert.Expect(LogType.Error, new Regex(@"Cannot auto-add 'String' to 'SamplePlayerModel'. Type must inherit from UnityEngine.Component"));

            var autoRequireType = typeof(BaseObservableDataModel).Assembly.GetType("SavableObservable.ComponentAutoRequire");
            var ensureMethod = autoRequireType?.GetMethod("EnsureComponent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static, null, new[] { typeof(MonoBehaviour), typeof(Type) }, null);
            ensureMethod?.Invoke(null, new object[] { dummy, typeof(string) });
        }

        [Test]
        public void ComponentAutoRequire_AddsRequiredComponentIfNotPresent() {
            var logic = _goA.AddComponent<SamplePlayerLogic>();

            var autoRequireType = typeof(BaseObservableDataModel).Assembly.GetType("SavableObservable.ComponentAutoRequire");
            var ensureMethod = autoRequireType?.GetMethod("EnsureComponent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static, null, new[] { typeof(MonoBehaviour), typeof(Type) }, null);
            ensureMethod?.Invoke(null, new object[] { logic, typeof(SamplePlayerModel) });

            Assert.IsNotNull(_goA.GetComponent<SamplePlayerModel>());
        }
    }
}
