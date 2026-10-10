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

        private class SecondaryModel : BaseObservableDataModel { }
        private class SecondaryPresenter : BaseObservablePresenter<SamplePlayerModel> { }

        [Test]
        public void MultipleModels_OnSameGameObject_LogsError() {
            var model1 = _goA.AddComponent<SamplePlayerModel>();
            var model2 = _goA.AddComponent<SecondaryModel>();
            var presenter = _goA.AddComponent<SamplePlayerPresenter>();

            LogAssert.Expect(LogType.Error, new Regex(@"GameObject 'UnitA' has multiple BaseObservableDataModel components"));

            Observable.SetListeners(presenter, model1);
        }

        [Test]
        public void MultiplePresenters_OnSameGameObject_LogsError() {
            var model = _goA.AddComponent<SamplePlayerModel>();
            var presenter1 = _goA.AddComponent<SamplePlayerPresenter>();
            var presenter2 = _goA.AddComponent<SecondaryPresenter>();

            LogAssert.Expect(LogType.Error, new Regex(@"GameObject 'UnitA' has multiple IObservablePresenter components"));

            Observable.SetListeners(presenter1, model);
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
