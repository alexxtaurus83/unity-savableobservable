using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SavableObservable.Editor {

    /// <summary>Hierarchy menu <c>GameObject/MMVC/Create Unit/</c>: asks for a folder and a unit name, then scaffolds the unit.</summary>
    internal static class MmvcScaffoldMenu {
        private const string Root = "GameObject/MMVC/Create Unit/";

        [MenuItem(Root + "View Unit", false, 10)]
        private static void CreateViewUnit(MenuCommand command) => MmvcScaffoldWizard.Open(MmvcUnitKind.ViewUnit, command.context as GameObject);

        [MenuItem(Root + "Model + Logic", false, 11)]
        private static void CreateModelAndLogic(MenuCommand command) => MmvcScaffoldWizard.Open(MmvcUnitKind.ModelAndLogic, command.context as GameObject);

        [MenuItem(Root + "Service", false, 12)]
        private static void CreateService(MenuCommand command) => MmvcScaffoldWizard.Open(MmvcUnitKind.Service, command.context as GameObject);
    }

    internal sealed class MmvcScaffoldWizard : ScriptableWizard {
        public string unitName = string.Empty;
        [Tooltip("Project folder under Assets/ that receives the scripts.")]
        public string targetFolder = "Assets";

        [NonSerialized] private MmvcUnitKind _kind;
        [NonSerialized] private GameObject _parent;

        internal static void Open(MmvcUnitKind kind, GameObject parent) {
            var wizard = DisplayWizard<MmvcScaffoldWizard>("Create MMVC Unit: " + kind, "Create", "Browse...");
            wizard._kind = kind;
            wizard._parent = parent;
            wizard.targetFolder = SelectedProjectFolder();
            wizard.OnWizardUpdate();
        }

        private void OnWizardUpdate() {
            var names = string.IsNullOrWhiteSpace(unitName) ? "<Name>" : unitName;
            helpString = "Creates " + string.Join(", ", MmvcScaffoldGenerator.GetTypeNames(_kind, names)) + " (empty classes) and adds them to a new GameObject after the compile.";
            if (!IsUnderAssets(targetFolder)) {
                errorString = "Target folder must be under Assets/.";
                isValid = false;
                return;
            }
            isValid = MmvcScaffoldGenerator.TryValidate(_kind, targetFolder, unitName, out var error);
            errorString = error ?? string.Empty;
        }

        private void OnWizardOtherButton() {
            var picked = EditorUtility.OpenFolderPanel("Target folder", targetFolder, string.Empty);
            if (string.IsNullOrEmpty(picked)) return;
            var projectRoot = Path.GetFullPath(Directory.GetCurrentDirectory()).Replace('\\', '/');
            picked = Path.GetFullPath(picked).Replace('\\', '/');
            if (picked.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase)) {
                targetFolder = picked.Substring(projectRoot.Length + 1);
            } else {
                targetFolder = picked;
            }
            OnWizardUpdate();
        }

        private void OnWizardCreate() {
            MmvcScaffoldGenerator.Generate(_kind, targetFolder, unitName, _parent);
        }

        private static bool IsUnderAssets(string folder) {
            if (string.IsNullOrEmpty(folder)) return false;
            var normalized = folder.Replace('\\', '/');
            return normalized == "Assets" || normalized.StartsWith("Assets/", StringComparison.Ordinal);
        }

        private static string SelectedProjectFolder() {
            foreach (var guid in Selection.assetGUIDs) {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path)) return path;
                var dir = Path.GetDirectoryName(path)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(dir) && AssetDatabase.IsValidFolder(dir)) return dir;
            }
            return "Assets";
        }
    }
}
