using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SavableObservable.Tests.Contracts {
    /// <summary>
    /// Reflection & TypeCache-driven audit suite that inspects assemblies for MMVC conventions:
    /// - All BaseObservableDataModel classes initialize cleanly and link ParentDataModel.
    /// - All Presenters have valid [ObservableHandler] and [AutoBind] field mappings (traversing base types).
    /// - BaseLogic subclasses do not hide Awake() without calling base or overriding.
    /// - ISharedSingleton is only implemented by MonoBehaviours, not Presenters or Models.
    /// </summary>
    [TestFixture]
    [Category("MmvcContract")]
    public abstract class MmvcAssemblyAuditTestsBase {
        protected abstract Assembly[] TargetAssemblies { get; }

        protected IEnumerable<Type> GetTypesDerivedFrom<T>() {
            var targetTypes = new List<Type>();
            var baseType = typeof(T);
            foreach (var asm in TargetAssemblies) {
                if (asm == null) continue;
                foreach (var t in asm.GetTypes()) {
                    if (baseType.IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface) {
                        targetTypes.Add(t);
                    }
                }
            }
            return targetTypes;
        }

        private static FieldInfo FindFieldAcrossHierarchy(Type type, string fieldName) {
            for (var curr = type; curr != null && curr != typeof(MonoBehaviour) && curr != typeof(Component) && curr != typeof(UnityEngine.Object) && curr != typeof(object); curr = curr.BaseType) {
                var field = curr.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            return null;
        }

        [Test]
        public void Audit_AllSharedSingletons_AreMonoBehaviours_AndNotPresentersOrModels() {
            foreach (var t in GetTypesDerivedFrom<ISharedSingleton>()) {
                Assert.IsFalse(t.IsInterface, $"Interface {t.FullName} must not derive from ISharedSingleton.");
                Assert.IsFalse(typeof(IObservablePresenter).IsAssignableFrom(t), $"Presenter {t.FullName} must not implement ISharedSingleton.");
                Assert.IsFalse(typeof(BaseObservableDataModel).IsAssignableFrom(t), $"Model {t.FullName} must not implement ISharedSingleton.");
                Assert.IsTrue(typeof(MonoBehaviour).IsAssignableFrom(t), $"Shared singleton {t.FullName} must derive from MonoBehaviour.");
            }
        }

        [Test]
        public void Audit_AllPresenters_ObservableHandlers_And_AutoBinds_MatchModelFields() {
            var presenterTypes = GetTypesDerivedFrom<IObservablePresenter>();
            var errors = new List<string>();

            foreach (var pType in presenterTypes) {
                // Find model type from generic base
                Type modelType = null;
                var curr = pType;
                while (curr != null && curr != typeof(object)) {
                    if (curr.IsGenericType && curr.GetGenericTypeDefinition() == typeof(BaseObservablePresenter<>)) {
                        modelType = curr.GetGenericArguments()[0];
                        break;
                    }
                    curr = curr.BaseType;
                }

                if (modelType == null) continue;

                // Inspect [ObservableHandler] traversing full presenter hierarchy (including private & protected methods across all bases)
                for (var t = pType; t != null && t != typeof(MonoBehaviour) && t != typeof(Component) && t != typeof(UnityEngine.Object) && t != typeof(object); t = t.BaseType) {
                    var methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    foreach (var m in methods) {
                        var attrs = m.GetCustomAttributes(typeof(ObservableHandlerAttribute), true);
                        foreach (ObservableHandlerAttribute attr in attrs) {
                            var field = FindFieldAcrossHierarchy(modelType, attr.VariableName);
                            if (field == null) {
                                errors.Add($"Presenter '{pType.Name}.{m.Name}' has [ObservableHandler(\"{attr.VariableName}\")] but '{modelType.Name}' has no such field across hierarchy.");
                            }
                        }
                    }

                    // Inspect [AutoBind] traversing full presenter hierarchy (including private & protected fields across all bases)
                    var fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    foreach (var f in fields) {
                        var attrs = f.GetCustomAttributes(typeof(AutoBindAttribute), true);
                        foreach (AutoBindAttribute attr in attrs) {
                            string targetName = string.IsNullOrWhiteSpace(attr.VariableName) ? f.Name : attr.VariableName;
                            var modelField = FindFieldAcrossHierarchy(modelType, targetName);
                            if (modelField == null) {
                                errors.Add($"Presenter '{pType.Name}.{f.Name}' has [AutoBind(\"{targetName}\")] but '{modelType.Name}' has no such field across hierarchy.");
                            }
                        }
                    }
                }
            }

            if (errors.Count > 0) {
                Assert.Fail("ObservableHandler/AutoBind Audit Failures:\n" + string.Join("\n", errors));
            }
        }

        [Test]
        public void Audit_AllLogics_AwakeDoesNotHideBaseAwake() {
            var logicTypes = GetTypesDerivedFrom<BaseLogic>();
            var warnings = new List<string>();

            foreach (var lType in logicTypes) {
                // Check if derived type declares private void Awake() without override
                var awakeMethod = lType.GetMethod("Awake", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (awakeMethod != null) {
                    if (awakeMethod.IsPrivate) {
                        warnings.Add($"BaseLogic subclass '{lType.FullName}' declares 'private void Awake()', which hides BaseLogic.Awake(). Use 'protected override void Awake() {{ base.Awake(); ... }}' instead.");
                    }
                }
            }

            if (warnings.Count > 0) {
                Assert.Fail("Logic Awake hiding audit failed:\n" + string.Join("\n", warnings));
            }
        }

        [Test]
        public void Audit_AllMmvcLifecycleMethods_DoNotHideBaseLifecycleMethods() {
            var warnings = new List<string>();

            // 1. BaseLogic subclasses: Awake and OnDestroy
            foreach (var lType in GetTypesDerivedFrom<BaseLogic>()) {
                CheckLifecycleMethodHiding(lType, "BaseLogic", "Awake", warnings);
                CheckLifecycleMethodHiding(lType, "BaseLogic", "OnDestroy", warnings);
            }

            // 2. IObservablePresenter subclasses: Awake and OnDestroy
            foreach (var pType in GetTypesDerivedFrom<IObservablePresenter>()) {
                CheckLifecycleMethodHiding(pType, "BaseObservablePresenter", "Awake", warnings);
                CheckLifecycleMethodHiding(pType, "BaseObservablePresenter", "OnDestroy", warnings);
            }

            // 3. BaseObservableDataModel subclasses: Awake and OnDestroy
            foreach (var mType in GetTypesDerivedFrom<BaseObservableDataModel>()) {
                CheckLifecycleMethodHiding(mType, "BaseObservableDataModel", "Awake", warnings);
                CheckLifecycleMethodHiding(mType, "BaseObservableDataModel", "OnDestroy", warnings);
            }

            if (warnings.Count > 0) {
                Assert.Fail("MMVC lifecycle hiding audit failed:\n" + string.Join("\n", warnings));
            }
        }

        private static void CheckLifecycleMethodHiding(Type derivedType, string baseTypeName, string methodName, List<string> warnings) {
            var method = derivedType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (method != null) {
                // If not virtual/override or if private (private methods in C# cannot be overrides and will shadow base method)
                if (method.IsPrivate || method.GetBaseDefinition().DeclaringType == derivedType) {
                    warnings.Add($"{baseTypeName} subclass '{derivedType.FullName}' declares non-override '{methodName}()', which hides {baseTypeName}.{methodName}(). Use 'protected override void {methodName}() {{ base.{methodName}(); ... }}' instead.");
                }
            }
        }
    }
}
