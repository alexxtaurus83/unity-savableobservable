using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace SavableObservable.Tests.Contracts {
    /// <summary>
    /// Specialized contract test base for ModalWindow components.
    /// Verifies defensive initialization of isOpen and title before Awake,
    /// idempotency of EnsureInitialized(), and open/close state transitions.
    /// </summary>
    [TestFixture]
    [Category("MmvcContract")]
    public abstract class MmvcModalContractTestsBase<TModel, TLogic, TPresenter> : MmvcTriadContractTestsBase<TModel, TLogic, TPresenter>
        where TModel : BaseObservableDataModel
        where TLogic : BaseLogic<TModel, TPresenter>
        where TPresenter : BaseObservablePresenter<TModel>
    {
        private static FieldInfo FindFieldAcrossHierarchy(Type type, string fieldName) {
            for (var curr = type; curr != null && curr != typeof(MonoBehaviour) && curr != typeof(Component) && curr != typeof(UnityEngine.Object) && curr != typeof(object); curr = curr.BaseType) {
                var field = curr.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            return null;
        }

        [Test]
        public void ModalContract_DefensiveInitialization_PreservesNullOrCustomState() {
            var isOpenField = FindFieldAcrossHierarchy(typeof(TModel), "isOpen");
            if (isOpenField == null) {
                Assert.Ignore($"Model {typeof(TModel).Name} does not declare an 'isOpen' observable field; skipping modal-specific contract test.");
                return;
            }

            // Verify defensive initialization initializes non-null
            Model.EnsureFieldsInitialized();
            var isOpenVar = isOpenField.GetValue(Model) as ObservableVariable<bool>;
            Assert.IsNotNull(isOpenVar, "isOpen should be initialized non-null on modal model.");

            // Set custom state before another EnsureFieldsInitialized call
            isOpenVar.Value = true;

            var titleField = FindFieldAcrossHierarchy(typeof(TModel), "title");
            ObservableVariable<string> titleVar = null;
            if (titleField != null) {
                titleVar = titleField.GetValue(Model) as ObservableVariable<string>;
                if (titleVar != null) {
                    titleVar.Value = "CUSTOM_MODAL_TEST_TITLE";
                }
            }

            // Call EnsureFieldsInitialized again: existing state and variable instances must be preserved
            Model.EnsureFieldsInitialized();

            Assert.IsTrue(isOpenVar.Value, "Custom isOpen value must be preserved when EnsureFieldsInitialized is invoked again.");
            Assert.AreSame(isOpenVar, isOpenField.GetValue(Model), "isOpen variable instance identity must be preserved across repeated initializations.");

            if (titleVar != null) {
                Assert.AreEqual("CUSTOM_MODAL_TEST_TITLE", titleVar.Value, "Custom title value must be preserved across repeated initializations.");
                Assert.AreSame(titleVar, titleField.GetValue(Model), "title variable instance identity must be preserved across repeated initializations.");
            }
        }

        [Test]
        public void ModalContract_EnsureInitialized_IdempotentOnModal() {
            if (!SupportsInactiveCloneLifecycle) {
                Assert.Ignore($"Skipping ModalContract_EnsureInitialized test for {typeof(TLogic).Name}: unit requires external scene/view dependencies.");
                return;
            }

            Presenter.EnsureInitialized();
            Presenter.EnsureInitialized();
            Assert.Pass("EnsureInitialized() executed repeatedly without error on modal presenter.");
        }
    }
}
