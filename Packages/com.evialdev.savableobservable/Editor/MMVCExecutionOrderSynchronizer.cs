using System;
using UnityEditor;
using UnityEngine;

namespace SavableObservable.Editor {

    /// <summary>
    /// Synchronizes script execution orders for project scripts under "Assets/".
    /// Target execution orders:
    /// - ServiceLocator: -200 (registers services, then binds all scene presenters)
    /// - BaseObservablePresenter: -150 (binds dynamically instantiated prefabs)
    /// - BaseObservableDataModel / LoaderWithModel / BaseLogic: 0 (the default; they do not depend on execution order,
    ///   so any earlier non-default order is reset to 0)
    /// </summary>
    [InitializeOnLoad]
    public static class MMVCExecutionOrderSynchronizer {
        static MMVCExecutionOrderSynchronizer() {
            EditorApplication.delayCall += SyncAll;
        }

        public static void SyncAll() {
            var monoScripts = MonoImporter.GetAllRuntimeMonoScripts();
            var toUpdate = new System.Collections.Generic.List<(MonoScript script, int targetOrder)>();

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
                        toUpdate.Add((script, targetOrder));
                    }
                }
            }

            if (toUpdate.Count > 0) {
                AssetDatabase.StartAssetEditing();
                try {
                    for (int i = 0; i < toUpdate.Count; i++) {
                        MonoImporter.SetExecutionOrder(toUpdate[i].script, toUpdate[i].targetOrder);
                    }
                } finally {
                    AssetDatabase.StopAssetEditing();
                }
            }
        }

        public static int GetTargetExecutionOrder(Type type) {
            if (typeof(ServiceLocator).IsAssignableFrom(type)) {
                return -200;
            }
            if (TypeUtil.IsSubclassOfRawGeneric(typeof(BaseObservablePresenter<>), type)) {
                return -150;
            }
            if ((typeof(BaseObservableDataModel).IsAssignableFrom(type) && type != typeof(BaseObservableDataModel))
                || TypeUtil.IsSubclassOfRawGeneric(typeof(LoaderWithModel<>), type)
                || typeof(BaseLogic).IsAssignableFrom(type)) {
                return 0;
            }
            return int.MinValue;
        }
    }
}
