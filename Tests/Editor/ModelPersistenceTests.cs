using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SavableObservable;
using SavableObservable.Samples;

namespace SavableObservable.Tests {
    [TestFixture]
    public class ModelPersistenceTests {
        private GameObject _unitA;
        private GameObject _unitB;

        [SetUp]
        public void SetUp() {
            _unitA = new GameObject("UnitA");
            _unitB = new GameObject("UnitB");
        }

        [TearDown]
        public void TearDown() {
            if (_unitA != null) UnityEngine.Object.DestroyImmediate(_unitA);
            if (_unitB != null) UnityEngine.Object.DestroyImmediate(_unitB);
        }

        [Test]
        public void BaseObservableDataModel_LoadDataFromModel_CopiesValuesWithoutReplacingWrappers() {
            var modelTarget = _unitA.AddComponent<SamplePlayerModel>();
            var modelSource = _unitB.AddComponent<SamplePlayerModel>();

            modelTarget.InitializeDefaults();
            var originalWrapper = modelTarget.health;

            modelSource.playerName.Value = "Archmage";
            modelSource.health.Value = 999;
            modelSource.energy.Value = 0.42f;
            modelSource.isShieldActive.Value = true;
            modelSource.score = 777;

            modelTarget.LoadDataFromModel(modelSource);

            Assert.AreSame(originalWrapper, modelTarget.health, "ObservableVariable wrapper instance must be preserved");
            Assert.AreEqual("Archmage", modelTarget.playerName.Value);
            Assert.AreEqual(999, modelTarget.health.Value);
            Assert.AreEqual(0.42f, modelTarget.energy.Value);
            Assert.IsTrue(modelTarget.isShieldActive.Value);
            Assert.AreEqual(777, modelTarget.score);
        }

        [Test]
        public void BaseObservableDataModel_LoadDataFromModel_NullSource_LogsErrorSafely() {
            var model = _unitA.AddComponent<SamplePlayerModel>();

            LogAssert.Expect(LogType.Error, new Regex(@"\[BaseObservableDataModel\] LoadDataFromModel: model is null"));

            model.LoadDataFromModel(null);
        }

        [Test]
        public void SamplePlayerLoader_SaveAndLoadRoundtrip() {
            var model = _unitA.AddComponent<SamplePlayerModel>();
            var logic = _unitA.AddComponent<SamplePlayerLogic>();
            var loader = _unitA.AddComponent<SamplePlayerLoader>();

            model.InitializeDefaults();
            model.playerName.Value = "Paladin";
            model.health.Value = 88;
            model.inventory.Add("HolySword");

            var dto = loader.SaveCurrentState();
            Assert.IsNotNull(dto);
            Assert.AreEqual("Paladin", dto.playerName);
            Assert.AreEqual(88, dto.health);
            Assert.IsTrue(dto.inventory.Contains("HolySword"));

            // Mutate model
            model.playerName.Value = "Mutated";
            model.health.Value = 5;
            model.inventory.Clear();

            // Restore from DTO
            loader.LoadSampleState(dto);

            Assert.AreEqual("Paladin", model.playerName.Value);
            Assert.AreEqual(88, model.health.Value);
            Assert.IsTrue(model.inventory.Contains("HolySword"));
        }
    }
}
