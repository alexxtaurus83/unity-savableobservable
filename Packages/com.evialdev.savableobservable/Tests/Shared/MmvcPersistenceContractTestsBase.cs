using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace SavableObservable.Tests.Contracts {
    /// <summary>
    /// Reusable contract test base for LoaderWithModel persistence implementations.
    /// Uses hooks for model mutation and save/load roundtripping without assuming universal JSON serialization.
    /// </summary>
    [TestFixture]
    [Category("MmvcContract")]
    public abstract class MmvcPersistenceContractTestsBase<TModel, TLoader>
        where TModel : BaseObservableDataModel
        where TLoader : LoaderWithModel<TModel>
    {
        protected GameObject Host;
        protected TModel Model;
        protected TLoader Loader;

        [SetUp]
        public virtual void SetUp() {
            Host = new GameObject(typeof(TLoader).Name + "_PersistenceHost");
            Host.SetActive(false);
            Model = Host.AddComponent<TModel>();
            Loader = Host.AddComponent<TLoader>();
            Model.EnsureFieldsInitialized();
        }

        [TearDown]
        public virtual void TearDown() {
            if (Host != null) {
                UnityEngine.Object.DestroyImmediate(Host);
            }
        }

        /// <summary>Mutate model fields to known test values prior to saving.</summary>
        protected abstract void MutateModelForSave(TModel model);

        /// <summary>Mutate model fields to a different set of values post-save to prove loading restores saved state.</summary>
        protected abstract void MutateModelAfterSave(TModel model);

        /// <summary>Assert that the model contains the restored values after loading, and observer identity is preserved.</summary>
        protected abstract void AssertModelRestored(TModel model);

        /// <summary>Hook to invoke save on the loader.</summary>
        protected abstract bool PerformSave(TLoader loader, TModel model);

        /// <summary>Hook to invoke load on the loader.</summary>
        protected abstract void PerformLoad(TLoader loader, TModel model);

        private static Action AttachCounter<TVariable>(ObservableTrackedAction<TVariable> action, Action onTrigger, object subscriber)
            where TVariable : class, IObservableVariable
        {
            Action<TVariable> handler = _ => onTrigger();
            action.Add(handler, subscriber);
            return () => action.Remove(handler);
        }

        [Test]
        public void PersistenceContract_LoaderResolvesModel_WithoutException() {
            var resolved = Loader.GetModel();
            Assert.IsNotNull(resolved, "Loader.GetModel() should return the attached model.");
            Assert.AreSame(Model, resolved);
        }

        [Test]
        public void PersistenceContract_LoadDataFromModel_ClonesObservableFields() {
            var sourceGo = new GameObject("PersistenceSource");
            try {
                var sourceModel = sourceGo.AddComponent<TModel>();
                sourceModel.EnsureFieldsInitialized();

                // Mutate source model via hook
                MutateModelForSave(sourceModel);

                // Load source into target
                Model.LoadDataFromModel(sourceModel);

                // Assert target model values match mutated source
                AssertModelRestored(Model);
            } finally {
                UnityEngine.Object.DestroyImmediate(sourceGo);
            }
        }

        [Test]
        public void PersistenceContract_SaveAndLoadRoundtrip_PreservesObserverIdentity() {
            var fields = Model.GetCachedObservableFields();
            var recordedVarInstances = new Dictionary<string, object>();
            var recordedActionInstances = new Dictionary<string, object>();

            // Record variable and tracked action object identities pre-save
            foreach (var f in fields) {
                object val = f.GetValue(Model);
                if (val != null) {
                    recordedVarInstances[f.Name] = val;
                    var onValueChangedProp = val.GetType().GetProperty("OnValueChanged", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        ?? val.GetType().GetProperty("OnChanged", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (onValueChangedProp != null) {
                        object actionObj = onValueChangedProp.GetValue(val);
                        if (actionObj != null) {
                            recordedActionInstances[f.Name] = actionObj;
                        }
                    }
                }
            }

            // 1. Mutate model to target save values
            MutateModelForSave(Model);

            // 2. Perform save
            bool saved = PerformSave(Loader, Model);
            Assert.IsTrue(saved, "PerformSave should succeed.");

            // 3. Mutate model to completely different values post-save
            MutateModelAfterSave(Model);

            // 4. Attach notification observer to verify change emission upon loading
            int notificationCount = 0;
            var cleanupActions = new List<Action>();
            var attachHelper = typeof(MmvcPersistenceContractTestsBase<TModel, TLoader>).GetMethod("AttachCounter", BindingFlags.NonPublic | BindingFlags.Static);

            foreach (var f in fields) {
                object val = f.GetValue(Model);
                if (val != null) {
                    var onValueChangedProp = val.GetType().GetProperty("OnValueChanged", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        ?? val.GetType().GetProperty("OnChanged", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (onValueChangedProp != null) {
                        object actionObj = onValueChangedProp.GetValue(val);
                        if (actionObj != null) {
                            // The tracked action generic type parameter T in ObservableTrackedAction<T>
                            Type actionType = actionObj.GetType();
                            if (actionType.IsGenericType) {
                                Type eventArgType = actionType.GetGenericArguments()[0];
                                Action triggerAction = () => notificationCount++;
                                var genericAttach = attachHelper.MakeGenericMethod(eventArgType);
                                var unbind = (Action)genericAttach.Invoke(null, new object[] { actionObj, triggerAction, this });
                                cleanupActions.Add(unbind);
                            }
                        }
                    }
                }
            }

            try {
                // 5. Perform load
                PerformLoad(Loader, Model);

                // 6. Assert restored values
                AssertModelRestored(Model);

                // 7. Assert notifications fired during load
                Assert.IsTrue(notificationCount > 0, "Observable fields should fire change notifications when values are restored from persistence.");

                // 8. Assert variable and tracked action object identities were preserved (no re-instantiation / no listener orphaning)
                foreach (var f in fields) {
                    object currentVal = f.GetValue(Model);
                    if (recordedVarInstances.TryGetValue(f.Name, out var originalVar)) {
                        Assert.AreSame(originalVar, currentVal, $"Observable variable '{f.Name}' instance identity must be preserved across persistence load.");
                    }

                    if (currentVal != null && recordedActionInstances.TryGetValue(f.Name, out var originalAction)) {
                        var onValueChangedProp = currentVal.GetType().GetProperty("OnValueChanged", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                            ?? currentVal.GetType().GetProperty("OnChanged", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        object currentAction = onValueChangedProp?.GetValue(currentVal);
                        Assert.AreSame(originalAction, currentAction, $"ObservableTrackedAction instance identity on '{f.Name}' must be preserved across persistence load.");
                    }
                }
            } finally {
                foreach (var cleanup in cleanupActions) {
                    try { cleanup(); } catch { }
                }
            }
        }
    }
}
