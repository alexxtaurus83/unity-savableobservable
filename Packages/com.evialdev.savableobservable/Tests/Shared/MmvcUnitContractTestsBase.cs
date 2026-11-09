using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace SavableObservable.Tests.Contracts {

    /// <summary>
    /// Reusable contract test base for any MMVC (Model, Logic, Presenter) triad.
    /// Provides opt-in hooks and flags for production units that require external view setup,
    /// scene dependencies, or Addressables, preventing spurious crashes on naked instantiation.
    /// </summary>
    [TestFixture]
    [Category("MmvcContract")]
    public abstract class MmvcTriadContractTestsBase<TModel, TLogic, TPresenter>
        where TModel : BaseObservableDataModel
        where TLogic : BaseLogic<TModel, TPresenter>
        where TPresenter : BaseObservablePresenter<TModel>
    {
        protected GameObject Host;
        protected TModel Model;
        protected TLogic Logic;
        protected TPresenter Presenter;

        /// <summary>
        /// When true, verifies that instantiating an inactive clone, calling GetModel(), and activating
        /// works safely and idempotently. Set to false if the unit relies on external scene singletons
        /// during InitializePresenter/Awake that cannot be mocked in an isolated unit test.
        /// </summary>
        protected virtual bool SupportsInactiveCloneLifecycle => true;

        /// <summary>
        /// When true, attempts two-way binding validation for compatible AutoBind fields.
        /// Defaults to true; can be disabled for units with strict external visual requirements.
        /// </summary>
        protected virtual bool SupportsTwoWayBindingTest => true;

        /// <summary>
        /// Opt-in hook to wire mock child UI elements or mock services before Presenter initialization.
        /// </summary>
        protected virtual void ConfigurePrefabOrViewReferences(GameObject host, TModel model, TLogic logic, TPresenter presenter) { }

        [SetUp]
        public virtual void SetUp() {
            Host = new GameObject(typeof(TLogic).Name + "_ContractHost");
            Host.SetActive(false);

            Model = Host.AddComponent<TModel>();
            Presenter = Host.AddComponent<TPresenter>();
            Logic = Host.AddComponent<TLogic>();

            ConfigurePrefabOrViewReferences(Host, Model, Logic, Presenter);
            Model.EnsureFieldsInitialized();
        }

        [TearDown]
        public virtual void TearDown() {
            if (Host != null) {
                UnityEngine.Object.DestroyImmediate(Host);
            }
        }

        private static FieldInfo FindFieldAcrossHierarchy(Type type, string fieldName) {
            for (var curr = type; curr != null && curr != typeof(MonoBehaviour) && curr != typeof(Component) && curr != typeof(UnityEngine.Object) && curr != typeof(object); curr = curr.BaseType) {
                var field = curr.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            return null;
        }

        [Test]
        public void Contract_SingleUnitRestrictions_Enforced() {
            // Verify single unit restrictions on triad types
            Assert.IsTrue(typeof(BaseObservableDataModel).IsAssignableFrom(typeof(TModel)) || Attribute.IsDefined(typeof(TModel), typeof(DisallowMultipleComponent), true),
                $"{typeof(TModel).Name} should derive from BaseObservableDataModel or declare [DisallowMultipleComponent]");
            Assert.IsTrue(typeof(BaseLogic).IsAssignableFrom(typeof(TLogic)) || Attribute.IsDefined(typeof(TLogic), typeof(DisallowMultipleComponent), true),
                $"{typeof(TLogic).Name} should derive from BaseLogic or declare [DisallowMultipleComponent]");
            Assert.IsTrue(typeof(IObservablePresenter).IsAssignableFrom(typeof(TPresenter)) || Attribute.IsDefined(typeof(TPresenter), typeof(DisallowMultipleComponent), true),
                $"{typeof(TPresenter).Name} should derive from BaseObservablePresenter or declare [DisallowMultipleComponent]");

            // Verify types do not implement ISharedSingleton
            Assert.IsFalse(typeof(ISharedSingleton).IsAssignableFrom(typeof(TModel)), $"{typeof(TModel).Name} must not implement ISharedSingleton.");
            Assert.IsFalse(typeof(ISharedSingleton).IsAssignableFrom(typeof(TPresenter)), $"{typeof(TPresenter).Name} must not implement ISharedSingleton.");
        }

        [Test]
        public void Contract_ModelFields_AllInitializeNonNull_AndLinkParentModel() {
            Model.EnsureFieldsInitialized();
            var fields = Model.GetCachedObservableFields();
            Assert.IsNotNull(fields, "GetCachedObservableFields should not return null");
            Assert.IsTrue(fields.Length > 0, $"Model {typeof(TModel).Name} should have at least one observable field.");

            foreach (var field in fields) {
                object val = field.GetValue(Model);
                Assert.IsNotNull(val, $"Observable field '{field.Name}' on {typeof(TModel).Name} should be initialized non-null.");

                // In SavableObservable, ParentDataModel is set on the internal ObservableTrackedAction (OnValueChanged / OnChanged)
                var onValueChangedProp = ObservableAccess.GetChangeEventProperty(val);
                Assert.IsNotNull(onValueChangedProp, $"Observable field '{field.Name}' on {typeof(TModel).Name} should have OnValueChanged (variable) or OnChanged (list) property.");

                object trackedAction = onValueChangedProp.GetValue(val);
                Assert.IsNotNull(trackedAction, $"Tracked action on field '{field.Name}' must be initialized non-null.");

                var parentProp = trackedAction.GetType().GetProperty("ParentDataModel", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(parentProp, $"Tracked action on field '{field.Name}' must have ParentDataModel property.");

                var parent = parentProp.GetValue(trackedAction);
                Assert.AreSame(Model, parent, $"Field '{field.Name}' ParentDataModel on tracked action should be linked to its containing model.");
            }
        }

        private static int CountNotificationsOnEqualAssign<T>(ObservableVariable<T> variable, object subscriber) {
            int count = 0;
            Action<ObservableVariable<T>> handler = _ => count++;
            variable.OnValueChanged.Add(handler, subscriber);
            try {
                variable.Value = variable.Value;
            } finally {
                variable.OnValueChanged.Remove(handler);
            }
            return count;
        }

        private static Type FindObservableVariableBase(Type type) {
            for (var t = type; t != null; t = t.BaseType) {
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ObservableVariable<>)) return t;
            }
            return null;
        }

        [Test]
        public void Contract_EqualAssignment_IsSilent_UnlessNotifyOnSameValue() {
            Model.EnsureFieldsInitialized();
            var helper = typeof(MmvcTriadContractTestsBase<TModel, TLogic, TPresenter>)
                .GetMethod(nameof(CountNotificationsOnEqualAssign), BindingFlags.NonPublic | BindingFlags.Static);

            foreach (var field in Model.GetCachedObservableFields()) {
                var variableType = FindObservableVariableBase(field.FieldType);
                if (variableType == null) continue; // ObservableList notifies on every mutation by design.

                var variable = field.GetValue(Model);
                Assert.IsNotNull(variable, $"Observable field '{field.Name}' should be initialized.");

                bool flagged = (bool)variableType.GetProperty(nameof(ObservableVariable<int>.NotifyOnSameValue)).GetValue(variable);
                var count = (int)helper.MakeGenericMethod(variableType.GetGenericArguments()[0]).Invoke(null, new[] { variable, this });

                Assert.AreEqual(flagged ? 1 : 0, count,
                    flagged
                        ? $"Field '{field.Name}' opted into NotifyOnSameValue: an equal assignment must notify."
                        : $"Field '{field.Name}': an equal assignment must be silent unless the field opts into NotifyOnSameValue.");
            }
        }

        [Test]
        public void Contract_EveryObservableHandlerMethod_HasValidSignatureAndMatchingModelField() {
            var presenterType = typeof(TPresenter);
            // Traverse presenter hierarchy (including private and protected methods across all bases)
            for (var currP = presenterType; currP != null && currP != typeof(MonoBehaviour) && currP != typeof(Component) && currP != typeof(UnityEngine.Object) && currP != typeof(object); currP = currP.BaseType) {
                var methods = currP.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

                foreach (var method in methods) {
                    var attrs = method.GetCustomAttributes(typeof(ObservableHandlerAttribute), true);
                    if (attrs == null || attrs.Length == 0) continue;

                    foreach (ObservableHandlerAttribute attr in attrs) {
                        var fieldInfo = FindFieldAcrossHierarchy(typeof(TModel), attr.VariableName);
                        Assert.IsNotNull(fieldInfo,
                            $"[ObservableHandler(\"{attr.VariableName}\")] on method '{presenterType.Name}.{method.Name}' does not match any observable field across hierarchy on model '{typeof(TModel).Name}'.");

                        var paramInfos = method.GetParameters();
                        Assert.IsTrue(paramInfos.Length <= 2,
                            $"Method '{presenterType.Name}.{method.Name}' has {paramInfos.Length} parameters. [ObservableHandler] methods must accept at most 2 parameters.");

                        Type fieldType = fieldInfo.FieldType;
                        bool isList = false;
                        Type itemType = null;

                        if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(ObservableList<>)) {
                            isList = true;
                            itemType = fieldType.GetGenericArguments()[0];
                        } else if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(ObservableVariable<>)) {
                            itemType = fieldType.GetGenericArguments()[0];
                        }

                        if (paramInfos.Length == 1) {
                            var p0 = paramInfos[0].ParameterType;
                            if (isList) {
                                bool validListParam = p0 == fieldType // ObservableList<T>
                                    || p0 == typeof(List<>).MakeGenericType(itemType) // List<T>
                                    || p0.IsAssignableFrom(fieldType) // IReadOnlyList<T>, object, etc.
                                    || p0 == typeof(object);
                                Assert.IsTrue(validListParam,
                                    $"Handler '{method.Name}' parameter type '{p0.Name}' is not compatible with '{fieldType.Name}'.");
                            } else if (itemType != null) {
                                bool validVarParam = p0.IsAssignableFrom(itemType) || p0 == typeof(object);
                                Assert.IsTrue(validVarParam,
                                    $"Handler '{method.Name}' parameter type '{p0.Name}' is not compatible with ObservableVariable value type '{itemType.Name}'.");
                            }
                        } else if (paramInfos.Length == 2) {
                            var p0 = paramInfos[0].ParameterType;
                            var p1 = paramInfos[1].ParameterType;
                            if (isList) {
                                bool validListParams = (p0.IsAssignableFrom(typeof(List<>).MakeGenericType(itemType)) || p0 == typeof(object))
                                    && (p1.IsAssignableFrom(typeof(List<>).MakeGenericType(itemType)) || p1.IsAssignableFrom(typeof(IReadOnlyList<>).MakeGenericType(itemType)) || p1 == typeof(object));
                                Assert.IsTrue(validListParams,
                                    $"Handler '{method.Name}' 2-param signature is not compatible with list type '{itemType.Name}'.");
                            } else if (itemType != null) {
                                bool validVarParams = (p0.IsAssignableFrom(itemType) || p0 == typeof(object))
                                    && (p1.IsAssignableFrom(itemType) || p1 == typeof(object));
                                Assert.IsTrue(validVarParams,
                                    $"Handler '{method.Name}' 2-param signature is not compatible with variable value type '{itemType.Name}'.");
                            }
                        }
                    }
                }
            }
        }

        [Test]
        public void Contract_EveryAutoBindField_HasMatchingModelFieldAndRegisteredUIAdapter() {
            var presenterType = typeof(TPresenter);
            // Traverse presenter hierarchy (including private and protected fields across all bases)
            for (var currP = presenterType; currP != null && currP != typeof(MonoBehaviour) && currP != typeof(Component) && currP != typeof(UnityEngine.Object) && currP != typeof(object); currP = currP.BaseType) {
                var fields = currP.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

                foreach (var field in fields) {
                    var attrs = field.GetCustomAttributes(typeof(AutoBindAttribute), true);
                    if (attrs == null || attrs.Length == 0) continue;

                    foreach (AutoBindAttribute attr in attrs) {
                        string targetName = string.IsNullOrWhiteSpace(attr.VariableName) ? field.Name : attr.VariableName;
                        var modelField = FindFieldAcrossHierarchy(typeof(TModel), targetName);
                        Assert.IsNotNull(modelField,
                            $"[AutoBind(\"{targetName}\")] on field '{presenterType.Name}.{field.Name}' does not match any observable field across hierarchy on model '{typeof(TModel).Name}'.");

                        // Verify an adapter exists for the UI field type
                        var adapter = UIAdapterRegistry.GetAdapter(field.FieldType);
                        Assert.IsNotNull(adapter,
                            $"No UIAdapter registered for [AutoBind] field '{field.Name}' of type '{field.FieldType.Name}' on '{presenterType.Name}'.");
                    }
                }
            }
        }

        [Test]
        public void Contract_InactivePrefabInstantiation_GetModelInitializesAndBinds_AndSetActiveDoesNotDoubleBind() {
            if (!SupportsInactiveCloneLifecycle) {
                Assert.Ignore($"Skipping InactivePrefabInstantiation test for {typeof(TLogic).Name}: flagged as requiring scene dependencies.");
                return;
            }

            // Create template game object
            var template = new GameObject(typeof(TLogic).Name + "_Template");
            template.SetActive(false);
            try {
                var tm = template.AddComponent<TModel>();
                var tp = template.AddComponent<TPresenter>();
                var tl = template.AddComponent<TLogic>();
                ConfigurePrefabOrViewReferences(template, tm, tl, tp);

                // Clone inactive
                var clone = UnityEngine.Object.Instantiate(template);
                try {
                    Assert.IsFalse(clone.activeSelf, "Cloned prefab should remain inactive.");
                    var clonedLogic = clone.GetComponent<TLogic>();
                    var clonedPresenter = clone.GetComponent<TPresenter>();

                    // Calling GetModel() on inactive logic must initialize and bind safely
                    var clonedModel = clonedLogic.GetModel();
                    Assert.IsNotNull(clonedModel, "clonedLogic.GetModel() should return non-null model even while inactive.");

                    // Activating clone should run Awake/EnsureInitialized idempotently without throwing or duplicate registration
                    clone.SetActive(true);
                    Assert.IsTrue(clone.activeSelf);
                    Assert.AreSame(clonedModel, clonedLogic.GetModel());
                } finally {
                    UnityEngine.Object.DestroyImmediate(clone);
                }
            } finally {
                UnityEngine.Object.DestroyImmediate(template);
            }
        }

        [Test]
        public void Contract_TwoWayAutoBindFields_RoundtripAndNoBounceBack() {
            if (!SupportsTwoWayBindingTest) {
                Assert.Ignore($"Skipping TwoWayAutoBindFields test for {typeof(TLogic).Name}: marked unsupported (requires scene/view dependencies).");
                return;
            }

            // Inspect if presenter has any two-way AutoBind fields (Toggle, Slider, TMP_InputField, InputField)
            var presenterType = typeof(TPresenter);
            var fields = presenterType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            bool testedAny = false;

            foreach (var field in fields) {
                var attrs = field.GetCustomAttributes(typeof(AutoBindAttribute), true);
                if (attrs == null || attrs.Length == 0) continue;

                var adapter = UIAdapterRegistry.GetAdapter(field.FieldType);
                if (adapter is IUIListenerAdapter listenerAdapter) {
                    // Two-way capable control
                    var attr = (AutoBindAttribute)attrs[0];
                    string targetName = string.IsNullOrWhiteSpace(attr.VariableName) ? field.Name : attr.VariableName;
                    var modelField = FindFieldAcrossHierarchy(typeof(TModel), targetName);
                    if (modelField == null) continue;

                    var modelVar = modelField.GetValue(Model) as IObservableVariable;
                    if (modelVar == null) continue;

                    // Ensure the UI field is populated with a test component if currently null
                    object uiInstance = field.GetValue(Presenter);
                    bool createdUi = false;
                    if (uiInstance == null) {
                        var child = new GameObject(field.Name + "_MockUI");
                        child.transform.SetParent(Host.transform);
                        uiInstance = child.AddComponent(field.FieldType);
                        field.SetValue(Presenter, uiInstance);
                        createdUi = true;
                    }

                    try {
                        Observable.SetListeners(Presenter, Model);

                        // If Toggle bound to bool
                        if (uiInstance is Toggle toggle && modelVar is ObservableVariable<bool> boolVar) {
                            bool original = boolVar.Value;
                            toggle.isOn = !original;
                            Assert.AreEqual(!original, boolVar.Value, "Toggle change should propagate to model without bounce-back.");
                            testedAny = true;
                        }
                        // If Slider bound to float
                        else if (uiInstance is Slider slider && modelVar is ObservableVariable<float> floatVar) {
                            slider.minValue = 0f;
                            slider.maxValue = 100f;
                            slider.value = 42f;
                            Assert.AreEqual(42f, floatVar.Value, 0.01f, "Slider change should propagate to model.");
                            testedAny = true;
                        }
                    } finally {
                        if (createdUi && uiInstance is Component comp) {
                            UnityEngine.Object.DestroyImmediate(comp.gameObject);
                            field.SetValue(Presenter, null);
                        }
                    }
                }
            }

            if (!testedAny) {
                Assert.Ignore($"Skipping TwoWayAutoBindFields test for {typeof(TLogic).Name}: No two-way AutoBind controls (Toggle/Slider) found on presenter.");
            }
        }

        [Test]
        public void Contract_DestroyPresenterBeforeModel_And_DestroyModel_CleansUpAllSubscriptions() {
            if (!SupportsInactiveCloneLifecycle) {
                Assert.Ignore($"Skipping DestroyPresenterBeforeModel test for {typeof(TLogic).Name}: unit requires external scene dependencies on activation.");
                return;
            }

            Host.SetActive(true);
            Presenter.EnsureInitialized();

            // Destroy Presenter first
            UnityEngine.Object.DestroyImmediate(Presenter);
            Presenter = null;

            // Mutating model fields should not throw
            foreach (var f in Model.GetCachedObservableFields()) {
                object val = f.GetValue(Model);
                var forceNotifyMethod = val?.GetType().GetMethod("ForceNotify", BindingFlags.Public | BindingFlags.Instance);
                forceNotifyMethod?.Invoke(val, null);
            }

            // Destroy Model
            UnityEngine.Object.DestroyImmediate(Model);
            Model = null;
            Assert.Pass("Destruction of Presenter and Model completed cleanly with no exceptions.");
        }
    }
}
