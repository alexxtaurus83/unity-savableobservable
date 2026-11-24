using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace SavableObservable.Editor {

    /// <summary>The unit shapes the scaffolding menu can generate.</summary>
    public enum MmvcUnitKind {
        /// <summary><c>&lt;Name&gt;DataModel</c> + <c>&lt;Name&gt;Presenter</c>.</summary>
        ViewUnit,
        /// <summary><c>&lt;Name&gt;DataModel</c> + <c>&lt;Name&gt;Presenter</c> + <c>&lt;Name&gt;Logic : BaseLogic&lt;M, P&gt;</c>.</summary>
        ModelAndLogic,
        /// <summary><c>&lt;Name&gt;Service : MonoBehaviour, ISharedSingleton</c>.</summary>
        Service
    }

    /// <summary>
    /// Writes empty MMVC unit classes and, after the next script compile, adds them to a new GameObject.
    /// Templates have no sample members. The namespace is copied from the first <c>.cs</c> file in the target folder (ordinal
    /// file-name order); no namespace when there is none. Existing files are never overwritten. The pending GameObject is kept
    /// in <see cref="SessionState"/> (survives the domain reload, no statics).
    /// </summary>
    public static class MmvcScaffoldGenerator {

        /// <summary>One file to write: its path and the full type name it declares.</summary>
        public readonly struct ScaffoldFile {
            public readonly string Path;
            public readonly string TypeName;
            public readonly string FullTypeName;
            public readonly string Content;

            public ScaffoldFile(string path, string typeName, string fullTypeName, string content) {
                Path = path;
                TypeName = typeName;
                FullTypeName = fullTypeName;
                Content = content;
            }
        }

        [Serializable]
        private sealed class PendingAttach {
            public string gameObjectName;
            public string parentGlobalId;
            public string[] typeNames;
        }

        /// <summary><see cref="SessionState"/> key of the GameObject waiting for the compile.</summary>
        public const string PendingKey = "SavableObservable.Scaffold.PendingAttach";

        private static readonly Regex IdentifierPattern = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$");
        private static readonly Regex NamespacePattern = new Regex(@"^\s*namespace\s+([A-Za-z_][\w.]*)", RegexOptions.Multiline);

        /// <summary>Type names generated for <paramref name="kind"/>, in the order their components are added.</summary>
        public static string[] GetTypeNames(MmvcUnitKind kind, string unitName) {
            switch (kind) {
                case MmvcUnitKind.ViewUnit: return new[] { unitName + "DataModel", unitName + "Presenter" };
                case MmvcUnitKind.ModelAndLogic: return new[] { unitName + "DataModel", unitName + "Presenter", unitName + "Logic" };
                case MmvcUnitKind.Service: return new[] { unitName + "Service" };
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        /// <summary>Namespace declared by the first <c>.cs</c> file in <paramref name="folder"/>, or an empty string.</summary>
        public static string ResolveNamespace(string folder) {
            if (!Directory.Exists(folder)) return string.Empty;
            var files = Directory.GetFiles(folder, "*.cs", SearchOption.TopDirectoryOnly);
            if (files.Length == 0) return string.Empty;
            Array.Sort(files, StringComparer.Ordinal);
            var match = NamespacePattern.Match(File.ReadAllText(files[0]));
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        /// <summary>The files <see cref="Write"/> would create (nothing is written).</summary>
        public static List<ScaffoldFile> PlanFiles(MmvcUnitKind kind, string folder, string unitName) {
            var ns = ResolveNamespace(folder);
            var names = GetTypeNames(kind, unitName);
            var files = new List<ScaffoldFile>(names.Length);
            for (int i = 0; i < names.Length; i++) {
                var name = names[i];
                var body = BuildClass(kind, name, unitName);
                files.Add(new ScaffoldFile(
                    System.IO.Path.Combine(folder, name + ".cs"),
                    name,
                    string.IsNullOrEmpty(ns) ? name : ns + "." + name,
                    Wrap(ns, body, kind == MmvcUnitKind.Service)));
            }
            return files;
        }

        /// <summary>Checks the unit name, the folder and that no target file exists.</summary>
        public static bool TryValidate(MmvcUnitKind kind, string folder, string unitName, out string error) {
            error = null;
            if (string.IsNullOrWhiteSpace(unitName) || !IdentifierPattern.IsMatch(unitName)) {
                error = "Unit name must be a C# identifier (letters, digits, underscore; not starting with a digit).";
                return false;
            }
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) {
                error = $"Folder '{folder}' does not exist.";
                return false;
            }
            foreach (var file in PlanFiles(kind, folder, unitName)) {
                if (File.Exists(file.Path)) {
                    error = $"'{file.Path}' already exists; scaffolding never overwrites a file.";
                    return false;
                }
            }
            return true;
        }

        /// <summary>Writes the files. Throws <see cref="IOException"/> (nothing written) when validation fails.</summary>
        public static List<ScaffoldFile> Write(MmvcUnitKind kind, string folder, string unitName) {
            if (!TryValidate(kind, folder, unitName, out var error)) throw new IOException(error);
            var files = PlanFiles(kind, folder, unitName);
            var encoding = new UTF8Encoding(false);
            var written = new List<string>(files.Count);
            try {
                foreach (var file in files) {
                    // CreateNew: never overwrite, even if a file appeared after validation.
                    using (var stream = new FileStream(file.Path, FileMode.CreateNew, FileAccess.Write)) {
                        written.Add(file.Path);
                        var bytes = encoding.GetBytes(file.Content);
                        stream.Write(bytes, 0, bytes.Length);
                    }
                }
            } catch {
                foreach (var path in written) {
                    try { File.Delete(path); } catch (IOException) { }
                }
                throw;
            }
            return files;
        }

        /// <summary>
        /// Menu entry point: writes the files into the project folder, remembers the GameObject to create and refreshes the
        /// AssetDatabase. The components are added by <see cref="AttachPending"/> after the compile.
        /// </summary>
        public static void Generate(MmvcUnitKind kind, string assetFolder, string unitName, GameObject parent) {
            var files = Write(kind, assetFolder, unitName);
            var typeNames = new string[files.Count];
            for (int i = 0; i < files.Count; i++) typeNames[i] = files[i].FullTypeName;
            SetPending(unitName, parent, typeNames);
            AssetDatabase.Refresh();
        }

        /// <summary>Stores the GameObject to create once <paramref name="fullTypeNames"/> are compiled.</summary>
        public static void SetPending(string gameObjectName, GameObject parent, string[] fullTypeNames) {
            if (parent != null && string.IsNullOrEmpty(parent.scene.path)) {
                Debug.LogWarning($"[SavableObservable] Scaffold: '{parent.name}' is in an unsaved scene, so it cannot be found again after the compile; '{gameObjectName}' will be created at the scene root.");
            }
            var pending = new PendingAttach {
                gameObjectName = gameObjectName,
                parentGlobalId = parent != null ? GlobalObjectId.GetGlobalObjectIdSlow(parent).ToString() : string.Empty,
                typeNames = fullTypeNames
            };
            SessionState.SetString(PendingKey, JsonUtility.ToJson(pending));
        }

        public static bool HasPending => !string.IsNullOrEmpty(SessionState.GetString(PendingKey, string.Empty));

        [InitializeOnLoadMethod]
        private static void ScheduleAttach() {
            if (HasPending) EditorApplication.delayCall += () => AttachPending();
            // A failed compile has no domain reload: drop the pending unit so it does not attach at an unrelated later reload.
            UnityEditor.Compilation.CompilationPipeline.compilationFinished -= OnCompilationFinished;
            UnityEditor.Compilation.CompilationPipeline.compilationFinished += OnCompilationFinished;
        }

        private static void OnCompilationFinished(object _) {
            if (HasPending && EditorUtility.scriptCompilationFailed) {
                SessionState.EraseString(PendingKey);
                Debug.LogError("[SavableObservable] Scaffold: the scripts were written but did not compile; fix the errors and add the components manually.");
            }
        }

        /// <summary>
        /// Creates the pending GameObject and adds the scaffolded components (in generation order), then clears the pending
        /// state. Returns the GameObject, or null when nothing was pending or a type is not compiled yet (the pending state
        /// is kept for the next reload unless compilation failed).
        /// </summary>
        public static GameObject AttachPending() {
            var json = SessionState.GetString(PendingKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return null;

            var pending = JsonUtility.FromJson<PendingAttach>(json);
            if (pending?.typeNames == null || pending.typeNames.Length == 0) {
                SessionState.EraseString(PendingKey);
                return null;
            }

            var types = new Type[pending.typeNames.Length];
            for (int i = 0; i < types.Length; i++) {
                types[i] = FindComponentType(pending.typeNames[i]);
                if (types[i] == null) {
                    if (EditorUtility.scriptCompilationFailed || !EditorApplication.isCompiling) {
                        SessionState.EraseString(PendingKey);
                        Debug.LogError($"[SavableObservable] Scaffold: type '{pending.typeNames[i]}' is not compiled; the GameObject '{pending.gameObjectName}' was not created. Fix the compile errors and add the components manually.");
                    }
                    return null;
                }
            }
            SessionState.EraseString(PendingKey);

            var go = new GameObject(pending.gameObjectName);
            Undo.RegisterCreatedObjectUndo(go, "Create MMVC Unit");
            if (!string.IsNullOrEmpty(pending.parentGlobalId)
                && GlobalObjectId.TryParse(pending.parentGlobalId, out var parentId)
                && GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parentId) is GameObject parent
                && !EditorUtility.IsPersistent(parent)) {
                if (parent.scene != go.scene) UnityEditor.SceneManagement.EditorSceneManager.MoveGameObjectToScene(go, parent.scene);
                Undo.SetTransformParent(go.transform, parent.transform, "Create MMVC Unit");
            }
            // Package editor tooling: the components exist only after the compile, so they are added here.
            for (int i = 0; i < types.Length; i++) {
                Undo.AddComponent(go, types[i]);
            }
            Selection.activeGameObject = go;
            return go;
        }

        private static Type FindComponentType(string fullName) {
            foreach (var type in TypeCache.GetTypesDerivedFrom<MonoBehaviour>()) {
                if (type.FullName == fullName && !type.IsAbstract) return type;
            }
            return null;
        }

        private static string BuildClass(MmvcUnitKind kind, string typeName, string unitName) {
            var model = unitName + "DataModel";
            var presenter = unitName + "Presenter";
            if (typeName == model) return $"public class {model} : BaseObservableDataModel {{\n}}";
            if (typeName == presenter) return $"public class {presenter} : BaseObservablePresenter<{model}> {{\n}}";
            if (kind == MmvcUnitKind.Service) return $"public class {typeName} : MonoBehaviour, ISharedSingleton {{\n}}";
            return $"public class {typeName} : BaseLogic<{model}, {presenter}> {{\n}}";
        }

        private static string Wrap(string ns, string body, bool needsUnityEngine) {
            var sb = new StringBuilder();
            sb.Append("using SavableObservable;\n");
            if (needsUnityEngine) sb.Append("using UnityEngine;\n");
            sb.Append('\n');
            if (string.IsNullOrEmpty(ns)) {
                sb.Append(body).Append('\n');
            } else {
                sb.Append("namespace ").Append(ns).Append(" {\n");
                foreach (var line in body.Split('\n')) sb.Append("    ").Append(line).Append('\n');
                sb.Append("}\n");
            }
            return sb.ToString();
        }
    }
}
