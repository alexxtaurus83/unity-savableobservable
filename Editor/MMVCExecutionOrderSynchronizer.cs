using System;
using UnityEditor;
using UnityEngine;

namespace SavableObservable.Editor {

    /// <summary>
    /// Synchronizes script execution orders for project scripts under "Assets/".
    /// Target execution orders:
    /// - BaseObservableDataModel: -300
    /// - LoaderWithModel: -250
    /// - ServiceLocator: -200
    /// - BaseObservablePresenter: -150
    /// - BaseLogic: 0
    /// </summary>
    [InitializeOnLoad]
    public static class MMVCExecutionOrderSynchronizer {
        static MMVCExecutionOrderSynchronizer() {
            EditorApplication.delayCall += SyncAll;
        }

        public static void SyncAll() {
            var monoScripts = MonoImporter.GetAllRuntimeMonoScripts();
            for (int i = 0; i < monoScripts.Length; i++) {
                var script = monoScripts[i];
                if (script == null) continue;

                var assetPath = AssetDatabase.GetAssetPath(script);
                // Only inspect runtime MonoScript assets whose path starts with "Assets/"
                if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                var scriptClass = script.GetClass();
                if (scriptClass == null) continue;

                int targetOrder = GetTargetExecutionOrder(scriptClass);
                if (targetOrder != int.MinValue) {
                    int currentOrder = MonoImporter.GetExecutionOrder(script);
                    if (currentOrder != targetOrder) {
                        MonoImporter.SetExecutionOrder(script, targetOrder);
                    }
                }
            }
        }

        public static int GetTargetExecutionOrder(Type type) {
            if (typeof(BaseObservableDataModel).IsAssignableFrom(type) && type != typeof(BaseObservableDataModel)) {
                return -300;
            }
            if (IsSubclassOfRawGeneric(typeof(LoaderWithModel<>), type)) {
                return -250;
            }
            if (typeof(ServiceLocator).IsAssignableFrom(type)) {
                return -200;
            }
            if (IsSubclassOfRawGeneric(typeof(BaseObservablePresenter<>), type)) {
                return -150;
            }
            if (typeof(BaseLogic).IsAssignableFrom(type)) {
                return 0;
            }
            return int.MinValue;
        }

        private static bool IsSubclassOfRawGeneric(Type generic, Type toCheck) {
            while (toCheck != null && toCheck != typeof(object)) {
                var cur = toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck;
                if (generic == cur) {
                    return true;
                }
                toCheck = toCheck.BaseType;
            }
            return false;
        }
    }
}
