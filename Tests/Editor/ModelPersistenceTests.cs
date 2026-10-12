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

        private class DerivedPlayerModel : SamplePlayerModel {
            public ObservableVariable<int> level = new ObservableVariable<int>();
            public string characterClass = "Mage";
        }

        [Test]
        public void BaseObservableDataModel_LoadDataFromModel_CopiesInheritedAndDerivedFields() {
            var target = _unitA.AddComponent<DerivedPlayerModel>();
            var source = _unitB.AddComponent<DerivedPlayerModel>();

            target.InitializeDefaults();
            target.level.Value = 1;
            target.characterClass = "Novice";

            source.InitializeDefaults();
            source.playerName.Value = "Gandalf";
            source.health.Value = 95;
            source.level.Value = 50;
            source.characterClass = "Wizard";

            target.LoadDataFromModel(source);

            // Verify both base class fields and derived class fields are copied
            Assert.AreEqual("Gandalf", target.playerName.Value);
            Assert.AreEqual(95, target.health.Value);
            Assert.AreEqual(50, target.level.Value);
            Assert.AreEqual("Wizard", target.characterClass);
        }

        [Test]
        public void LoaderWithModel_LoadDataFromModel_RebindsPresenterWithoutCorruptingListeners() {
            var model = _unitA.AddComponent<SamplePlayerModel>();
            var presenter = _unitA.AddComponent<SamplePlayerPresenter>();
            var loader = _unitA.AddComponent<SamplePlayerLoader>();

            var sourceModel = _unitB.AddComponent<SamplePlayerModel>();
            sourceModel.InitializeDefaults();
            sourceModel.health.Value = 70;

            model.InitializeDefaults();
            Observable.SetListeners(presenter, model);

            // Mutate target model
            model.health.Value = 10;

            // Load saved model through loader (LoaderWithModel passes model state object)
            loader.LoadDataFromModel(sourceModel);

            // Verify state is restored and listeners remain intact and active
            Assert.AreEqual(70, model.health.Value);

            // Further changes continue to notify presenter
            model.health.Value = 65;
            Assert.AreEqual(65, presenter.LastObservedHealth);
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
