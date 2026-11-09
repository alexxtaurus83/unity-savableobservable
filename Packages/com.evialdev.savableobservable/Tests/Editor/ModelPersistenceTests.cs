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
        public void LoaderWithModel_LoadDataFromModel_KeepsExistingBindingAndNotifies() {
            var model = _unitA.AddComponent<SamplePlayerModel>();
            var presenter = _unitA.AddComponent<SamplePlayerPresenter>();
            var loader = _unitA.AddComponent<SamplePlayerLoader>();

            var sourceModel = _unitB.AddComponent<SamplePlayerModel>();
            sourceModel.InitializeDefaults();
            sourceModel.health.Value = 70;

            model.InitializeDefaults();
            presenter.InitializeBinding();
            var initializeCountBeforeLoad = presenter.InitializePresenterCount;
            Assert.AreEqual(1, initializeCountBeforeLoad);

            // Mutate target model
            model.health.Value = 10;

            // Load saved model through loader: no rebind, presenter observes the loaded value during load
            loader.LoadDataFromModel(sourceModel);

            Assert.AreEqual(70, model.health.Value);
            Assert.AreEqual(70, presenter.LastObservedHealth, "Presenter must observe the loaded value through its existing binding.");
            Assert.AreEqual(10, presenter.LastObservedPrevHealth);
            Assert.AreEqual(1, presenter.InitializePresenterCount, "Load must not re-run InitializePresenter (no rebind).");

            // Further changes continue to notify presenter exactly once per change
            var eventsBefore = presenter.HealthChangeEventsCount;
            model.health.Value = 65;
            Assert.AreEqual(65, presenter.LastObservedHealth);
            Assert.AreEqual(eventsBefore + 1, presenter.HealthChangeEventsCount, "Listeners must not be duplicated by loading.");
        }

        [Test]
        public void LoaderWithModel_LoadDataFromModel_EqualValues_AreSilent() {
            var model = _unitA.AddComponent<SamplePlayerModel>();
            var presenter = _unitA.AddComponent<SamplePlayerPresenter>();
            var loader = _unitA.AddComponent<SamplePlayerLoader>();

            var sourceModel = _unitB.AddComponent<SamplePlayerModel>();
            sourceModel.InitializeDefaults();
            model.InitializeDefaults();
            presenter.InitializeBinding();
            var eventsBefore = presenter.HealthChangeEventsCount;

            loader.LoadDataFromModel(sourceModel); // every observable value already equals the current one

            Assert.AreEqual(eventsBefore, presenter.HealthChangeEventsCount, "Loading equal values must not notify.");
        }

        [Test]
        public void LoaderWithModel_LoadDataFromModel_FlaggedField_NotifiesOnEqualValue() {
            var model = _unitA.AddComponent<SamplePlayerModel>();
            var presenter = _unitA.AddComponent<SamplePlayerPresenter>();
            var loader = _unitA.AddComponent<SamplePlayerLoader>();

            var sourceModel = _unitB.AddComponent<SamplePlayerModel>();
            sourceModel.InitializeDefaults();
            model.InitializeDefaults();
            presenter.InitializeBinding();
            model.health.NotifyOnSameValue = true;
            var eventsBefore = presenter.HealthChangeEventsCount;

            loader.LoadDataFromModel(sourceModel);

            Assert.AreEqual(eventsBefore + 1, presenter.HealthChangeEventsCount, "A flagged field notifies even when the loaded value is equal.");
        }

        [Test]
        public void LoadDataFromModel_DoesNotCopyNotifyOnSameValueFlag() {
            var target = _unitA.AddComponent<SamplePlayerModel>();
            var source = _unitB.AddComponent<SamplePlayerModel>();
            target.EnsureFieldsInitialized();
            source.EnsureFieldsInitialized();
            source.health.NotifyOnSameValue = true;

            target.LoadDataFromModel(source);

            Assert.IsFalse(target.health.NotifyOnSameValue, "The runtime flag is neither saved nor copied on load.");
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

        private class BaseTestModelWithPrivateField : BaseObservableDataModel {
            [SerializeField] private ObservableVariable<int> _baseSecret = new ObservableVariable<int>();
            public int BaseSecret {
                get => _baseSecret.Value;
                set => _baseSecret.Value = value;
            }
        }

        private class DerivedListModel : BaseTestModelWithPrivateField {
            public ObservableList<string> items = new ObservableList<string>();
            public System.Collections.Generic.List<int> numbers = new System.Collections.Generic.List<int>();
        }

        private class DescriptorCoverageModel : BaseObservableDataModel {
            public ObservableVariable<int> saved = new ObservableVariable<int>();
            [NonSaved] public ObservableVariable<int> transientVar = new ObservableVariable<int>();
            [NonSaved] public ObservableList<string> transientList = new ObservableList<string>();
            public ObservableList<string> savedList = new ObservableList<string>();
            public int plainSaved;
            [NonSaved] public int plainTransient;
            public int[] plainArray = Array.Empty<int>();
            public System.Collections.Generic.List<string> plainList = new System.Collections.Generic.List<string>();

            public string PropertyBacked { get; set; }
            public int ReadOnlyProperty => 5;
        }

        [Test]
        public void LoadDataFromModel_Descriptor_CoversPropertiesObservablePlainNonSavedAndCloning() {
            var target = _unitA.AddComponent<DescriptorCoverageModel>();
            var source = _unitB.AddComponent<DescriptorCoverageModel>();
            target.EnsureFieldsInitialized();
            source.EnsureFieldsInitialized();

            source.saved.Value = 11;
            source.transientVar.Value = 22;
            source.transientList.Add("secret");
            source.savedList.Add("kept");
            source.plainSaved = 33;
            source.plainTransient = 44;
            source.plainArray = new[] { 1, 2, 3 };
            source.plainList.Add("p");
            source.PropertyBacked = "prop";

            target.transientVar.Value = -1;
            target.plainTransient = -2;

            var targetSavedWrapper = target.saved;
            target.LoadDataFromModel(source);

            Assert.AreEqual("prop", target.PropertyBacked, "Writable properties are copied.");
            Assert.AreEqual(11, target.saved.Value, "Observable fields are copied.");
            Assert.AreSame(targetSavedWrapper, target.saved, "Observable wrappers are preserved.");
            CollectionAssert.AreEqual(new[] { "kept" }, target.savedList, "Observable lists are copied.");
            Assert.AreEqual(33, target.plainSaved, "Plain fields are copied.");

            Assert.AreEqual(-1, target.transientVar.Value, "[NonSaved] observable variables are skipped.");
            Assert.AreEqual(0, target.transientList.Count, "[NonSaved] observable lists are skipped.");
            Assert.AreEqual(-2, target.plainTransient, "[NonSaved] plain fields are skipped.");

            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, target.plainArray);
            Assert.AreNotSame(source.plainArray, target.plainArray, "Arrays are cloned, not aliased.");
            CollectionAssert.AreEqual(new[] { "p" }, target.plainList);
            Assert.AreNotSame(source.plainList, target.plainList, "Lists are cloned, not aliased.");
        }

        [Test]
        public void ModelDescriptor_IsCached_AndSharedBetweenBindingAndPersistence() {
            var first = ModelDescriptor.Get(typeof(DescriptorCoverageModel));
            var second = ModelDescriptor.Get(typeof(DescriptorCoverageModel));

            Assert.AreSame(first, second);
            CollectionAssert.Contains(first.ObservableFieldsByName.Keys, "transientVar", "Binding sees [NonSaved] observables.");
            CollectionAssert.DoesNotContain(System.Linq.Enumerable.Select(first.LoadObservableFields, t => t.field.Name), "transientVar", "Persistence skips [NonSaved] observables.");
        }

        [Test]
        public void LoadDataFromModel_ClonesListsAndCopiesPrivateBaseFieldsWithoutAliasing() {
            var target = _unitA.AddComponent<DerivedListModel>();
            var source = _unitB.AddComponent<DerivedListModel>();

            target.EnsureFieldsInitialized();
            source.EnsureFieldsInitialized();

            source.BaseSecret = 99;
            source.items.Add("Alpha");
            source.items.Add("Beta");
            source.numbers.Add(10);
            source.numbers.Add(20);

            target.LoadDataFromModel(source);

            // Verify base private observable field copied
            Assert.AreEqual(99, target.BaseSecret);

            // Verify lists copied accurately
            Assert.AreEqual(2, target.items.Count);
            Assert.AreEqual("Alpha", target.items[0]);
            Assert.AreEqual("Beta", target.items[1]);
            Assert.AreEqual(2, target.numbers.Count);
            Assert.AreEqual(10, target.numbers[0]);

            // Mutate target: must NOT mutate source
            target.items.Add("Gamma");
            target.numbers.Add(30);

            Assert.AreEqual(3, target.items.Count);
            Assert.AreEqual(3, target.numbers.Count);
            Assert.AreEqual(2, source.items.Count, "Mutating target ObservableList must not affect source model");
            Assert.AreEqual(2, source.numbers.Count, "Mutating target List must not affect source model");

            // Mutate source: must NOT mutate target
            source.items.Add("Delta");
            source.numbers.Add(40);

            Assert.AreEqual(3, source.items.Count);
            Assert.AreEqual(3, source.numbers.Count);
            Assert.AreEqual(3, target.items.Count, "Mutating source ObservableList must not affect target model");
            Assert.AreEqual(3, target.numbers.Count, "Mutating source List must not affect target model");
        }
    }
}
