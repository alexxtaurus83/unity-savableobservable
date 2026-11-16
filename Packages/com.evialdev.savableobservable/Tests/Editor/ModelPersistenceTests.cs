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
            var inventoryEventsBefore = presenter.InventoryChangeEventsCount;

            loader.LoadDataFromModel(sourceModel); // every observable value already equals the current one

            Assert.AreEqual(eventsBefore, presenter.HealthChangeEventsCount, "Loading equal values must not notify.");
            Assert.AreEqual(inventoryEventsBefore, presenter.InventoryChangeEventsCount, "Loading equal ObservableList values must not notify.");
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
            public int AutoReadOnly { get; } = 42;
            [NonSaved] public string TransientProperty { get; set; }
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
            source.TransientProperty = "should-not-copy";

            target.transientVar.Value = -1;
            target.plainTransient = -2;
            target.TransientProperty = "original";

            var targetSavedWrapper = target.saved;
            target.LoadDataFromModel(source);

            Assert.AreEqual("prop", target.PropertyBacked, "Writable properties are copied.");
            Assert.AreEqual(42, target.AutoReadOnly, "Read-only auto-properties must not be overwritten.");
            Assert.AreEqual("original", target.TransientProperty, "[NonSaved] property must be skipped.");
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

        [Test]
        public void LoadDataFromModel_FromPlainDtoObject_CopiesMatchingFieldsAndObservablesWithoutTargetException() {
            var model = _unitA.AddComponent<SamplePlayerModel>();
            model.InitializeDefaults();

            var dto = new SampleSaveDto {
                playerName = "Gandalf",
                health = 150,
                energy = 0.9f,
                isShieldActive = true,
                inventory = new System.Collections.Generic.List<string> { "Staff", "Ring" },
                score = 12345
            };

            model.LoadDataFromModel(dto);

            Assert.AreEqual("Gandalf", model.playerName.Value);
            Assert.AreEqual(150, model.health.Value);
            Assert.AreEqual(0.9f, model.energy.Value, 1e-5f);
            Assert.IsTrue(model.isShieldActive.Value);
            CollectionAssert.AreEqual(new[] { "Staff", "Ring" }, model.inventory.Value);
            Assert.AreEqual(12345, model.score);
        }

        private class BaseShadowModel : BaseObservableDataModel {
            [SerializeField] private ObservableVariable<int> _secret = new ObservableVariable<int>();
            [SerializeField] private int _plain;

            public int BaseSecret {
                get => _secret.Value;
                set => _secret.Value = value;
            }
            public int BasePlain {
                get => _plain;
                set => _plain = value;
            }
            public ObservableVariable<int> BaseSecretVar => _secret;
        }

        private class DerivedShadowModel : BaseShadowModel {
            [SerializeField] private ObservableVariable<int> _secret = new ObservableVariable<int>();
            [SerializeField] private int _plain;

            public int DerivedSecret {
                get => _secret.Value;
                set => _secret.Value = value;
            }
            public int DerivedPlain {
                get => _plain;
                set => _plain = value;
            }
            public ObservableVariable<int> DerivedSecretVar => _secret;
        }

        [Test]
        public void ModelDescriptor_PreservesShadowedBaseClassPrivateFields() {
            var target = _unitA.AddComponent<DerivedShadowModel>();
            var source = _unitB.AddComponent<DerivedShadowModel>();

            target.EnsureFieldsInitialized();
            source.EnsureFieldsInitialized();

            Assert.IsNotNull(target.BaseSecretVar, "Base private observable must be initialized");
            Assert.IsNotNull(target.DerivedSecretVar, "Derived private observable must be initialized");
            Assert.AreNotSame(target.BaseSecretVar, target.DerivedSecretVar, "Base and derived observables must be distinct instances");

            source.BaseSecret = 100;
            source.DerivedSecret = 200;
            source.BasePlain = 10;
            source.DerivedPlain = 20;

            target.LoadDataFromModel(source);

            Assert.AreEqual(100, target.BaseSecret, "Base private observable must be copied independently");
            Assert.AreEqual(200, target.DerivedSecret, "Derived private observable must be copied independently");
            Assert.AreEqual(10, target.BasePlain, "Base private plain field must be copied independently");
            Assert.AreEqual(20, target.DerivedPlain, "Derived private plain field must be copied independently");
        }

        private class BaseShadowDto {
            public int level { get; set; } = 10;
        }

        private class DerivedShadowDto : BaseShadowDto {
            public new int level { get; set; } = 20;
        }

        private class CaseCollisionDto {
            public string item { get; set; } = "lower";
            public string ITEM { get; set; } = "UPPER";
        }

        private class CaseCollisionTargetModel : BaseObservableDataModel {
            public ObservableVariable<string> item = new ObservableVariable<string>();
        }

        private class ShadowDtoTargetModel : BaseObservableDataModel {
            public ObservableVariable<int> level = new ObservableVariable<int>();
        }

        [Test]
        public void LoadDataFromModel_ShadowedDtoMember_DoesNotThrowAmbiguousMatchException() {
            var target = _unitA.AddComponent<ShadowDtoTargetModel>();
            target.EnsureFieldsInitialized();
            target.level.Value = 0;

            var dto = new DerivedShadowDto();
            // Should succeed without AmbiguousMatchException and prefer derived property
            Assert.DoesNotThrow(() => target.LoadDataFromModel(dto));
            Assert.AreEqual(20, target.level.Value);
        }

        [Test]
        public void LoadDataFromModel_CaseCollidingDtoMembers_PrefersExactCaseMatch() {
            var target = _unitA.AddComponent<CaseCollisionTargetModel>();
            target.EnsureFieldsInitialized();
            target.item.Value = "initial";

            var dto = new CaseCollisionDto();
            Assert.DoesNotThrow(() => target.LoadDataFromModel(dto));
            Assert.AreEqual("lower", target.item.Value);
        }

        private class InterfaceCollectionDto {
            public System.Collections.Generic.IReadOnlyList<string> tags { get; set; }
        }

        private class InterfaceCollectionModel : BaseObservableDataModel {
            public ObservableVariable<System.Collections.Generic.List<string>> tags = new ObservableVariable<System.Collections.Generic.List<string>>();
        }

        [Test]
        public void LoadDataFromModel_ReadOnlyListSource_ClonesToListWhenTargetIsList() {
            var target = _unitA.AddComponent<InterfaceCollectionModel>();
            target.EnsureFieldsInitialized();

            var originalList = new System.Collections.ObjectModel.ReadOnlyCollection<string>(new System.Collections.Generic.List<string> { "one", "two" });
            var dto = new InterfaceCollectionDto { tags = originalList };

            target.LoadDataFromModel(dto);

            Assert.IsNotNull(target.tags.Value);
            CollectionAssert.AreEqual(new[] { "one", "two" }, target.tags.Value);
            Assert.AreNotSame(originalList, target.tags.Value);
        }
    }
}
