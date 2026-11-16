using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using SavableObservable.Samples;

namespace SavableObservable.Tests {

    [TestFixture]
    public class ServiceLocatorAllocationTests {

        private GameObject _locatorGo;
        private ServiceLocator _locator;
        private GameObject _goA;
        private GameObject _goB;

        [SetUp]
        public void SetUp() {
            _locatorGo = new GameObject("LocatorHost");
            _locator = _locatorGo.AddComponent<ServiceLocator>();
            _goA = new GameObject("GoA");
            _goB = new GameObject("GoB");
        }

        [TearDown]
        public void TearDown() {
            if (_goB != null) UnityEngine.Object.DestroyImmediate(_goB);
            if (_goA != null) UnityEngine.Object.DestroyImmediate(_goA);
            if (_locatorGo != null) UnityEngine.Object.DestroyImmediate(_locatorGo);
        }

        [Test]
        public void Get_Unnamed_WithDefaultKey_DoesNotAllocateGCMemory() {
            var logic = _goA.AddComponent<SamplePlayerLogic>();
            _locator.TryRegister(logic);

            // Warm up
            _ = _locator.Get<SamplePlayerLogic>();

            Assert.That(() => {
                _ = _locator.Get<SamplePlayerLogic>();
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void Get_Unnamed_WithCustomSingletonName_SoleInstance_DoesNotAllocateGCMemory() {
            var logic = _goA.AddComponent<SamplePlayerLogic>();
            logic.SetSingletonName("CustomName");
            _locator.TryRegister(logic);

            // Warm up
            _ = _locator.Get<SamplePlayerLogic>();

            Assert.That(() => {
                _ = _locator.Get<SamplePlayerLogic>();
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void Get_Unnamed_UnregisteredType_DoesNotAllocateGCMemory() {
            // Warm up
            _ = _locator.Get<SamplePlayerLogic>();

            Assert.That(() => {
                _ = _locator.Get<SamplePlayerLogic>();
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void GetModel_Named_DoesNotAllocateGCMemory() {
            var logic = _goA.AddComponent<SamplePlayerLogic>();
            logic.SetSingletonName("PlayerA");
            _locator.TryRegister(logic);

            string key = "PlayerA";
            // Warm up
            _ = _locator.GetModel<SamplePlayerModel>(key);

            Assert.That(() => {
                _ = _locator.GetModel<SamplePlayerModel>(key);
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void GetModel_Unnamed_Unique_DoesNotAllocateGCMemory() {
            var logic = _goA.AddComponent<SamplePlayerLogic>();
            _locator.TryRegister(logic);

            // Warm up
            _ = _locator.GetModel<SamplePlayerModel>();

            Assert.That(() => {
                _ = _locator.GetModel<SamplePlayerModel>();
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void Get_Ambiguity_LogsError_AndReturnsNull() {
            var logicA = _goA.AddComponent<SamplePlayerLogic>();
            logicA.SetSingletonName("KeyA");
            var logicB = _goB.AddComponent<SamplePlayerLogic>();
            logicB.SetSingletonName("KeyB");

            _locator.TryRegister(logicA);
            _locator.TryRegister(logicB);

            LogAssert.Expect(LogType.Error, new Regex(@"Ambiguous Get request for type.*SamplePlayerLogic.*(KeyA.*KeyB|KeyB.*KeyA)"));
            var result = _locator.Get<SamplePlayerLogic>();
            Assert.IsNull(result);
        }

        [Test]
        public void GetModel_Ambiguity_LogsError_AndReturnsNull() {
            var logicA = _goA.AddComponent<SamplePlayerLogic>();
            logicA.SetSingletonName("KeyA");
            var logicB = _goB.AddComponent<SamplePlayerLogic>();
            logicB.SetSingletonName("KeyB");

            _locator.TryRegister(logicA);
            _locator.TryRegister(logicB);

            LogAssert.Expect(LogType.Error, new Regex(@"Ambiguous GetModel request for model type.*SamplePlayerModel.*(KeyA.*KeyB|KeyB.*KeyA)"));
            var result = _locator.GetModel<SamplePlayerModel>();
            Assert.IsNull(result);
        }

        [Test]
        public void Get_PrunesDestroyedEntries_AndFallsBackToRemainingLiveInstance() {
            var logicA = _goA.AddComponent<SamplePlayerLogic>();
            logicA.SetSingletonName("KeyA");
            var logicB = _goB.AddComponent<SamplePlayerLogic>();
            logicB.SetSingletonName("KeyB");

            _locator.TryRegister(logicA);
            _locator.TryRegister(logicB);

            UnityEngine.Object.DestroyImmediate(logicA);

            // logicA is destroyed, should be pruned during iteration, sole remaining is logicB
            var resolved = _locator.Get<SamplePlayerLogic>();
            Assert.AreSame(logicB, resolved);
        }

        [Test]
        public void GetModel_PrunesDestroyedLogic_AndFallsBackToRemainingLiveModel() {
            var logicA = _goA.AddComponent<SamplePlayerLogic>();
            logicA.SetSingletonName("KeyA");
            var logicB = _goB.AddComponent<SamplePlayerLogic>();
            logicB.SetSingletonName("KeyB");
            var modelB = _goB.GetComponent<SamplePlayerModel>();

            _locator.TryRegister(logicA);
            _locator.TryRegister(logicB);

            UnityEngine.Object.DestroyImmediate(logicA);

            var resolvedModel = _locator.GetModel<SamplePlayerModel>();
            Assert.AreSame(modelB, resolvedModel);
        }

        [Test]
        public void Get_NamedLookup_DoesNotFallBackToOtherInstance() {
            var logicA = _goA.AddComponent<SamplePlayerLogic>();
            logicA.SetSingletonName("KeyA");
            _locator.TryRegister(logicA);

            Assert.IsNull(_locator.Get<SamplePlayerLogic>("NonExistentKey"));
        }

        [Test]
        public void GetModel_NamedLookup_DoesNotFallBackToOtherInstance() {
            var logicA = _goA.AddComponent<SamplePlayerLogic>();
            logicA.SetSingletonName("KeyA");
            _locator.TryRegister(logicA);

            Assert.IsNull(_locator.GetModel<SamplePlayerModel>("NonExistentKey"));
        }

        [Test]
        public void IsRegistered_Unnamed_DoesNotAllocateGCMemory() {
            var logic = _goA.AddComponent<SamplePlayerLogic>();
            _locator.TryRegister(logic);
            _ = _locator.IsRegistered<SamplePlayerLogic>();

            Assert.That(() => {
                _ = _locator.IsRegistered<SamplePlayerLogic>();
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void Get_InactiveRegisteredLogic_ReturnsInstance() {
            _goA.SetActive(false);
            var logic = _goA.AddComponent<SamplePlayerLogic>();
            logic.SetSingletonName("Inactive");
            _locator.TryRegister(logic);

            Assert.AreSame(logic, _locator.Get<SamplePlayerLogic>());
        }
    }
}
