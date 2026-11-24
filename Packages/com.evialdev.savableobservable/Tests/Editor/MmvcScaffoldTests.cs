using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using SavableObservable.Editor;
using SavableObservable.Samples;

namespace SavableObservable.Tests {
    [TestFixture]
    public class MmvcScaffoldTests {
        private string _folder;

        [SetUp]
        public void SetUp() {
            _folder = Path.Combine(Path.GetTempPath(), "kilo", "mmvc-scaffold-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
        }

        [TearDown]
        public void TearDown() {
            SessionState.EraseString(MmvcScaffoldGenerator.PendingKey);
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        }

        [TestCase(MmvcUnitKind.ViewUnit, new[] { "ScoreDataModel", "ScorePresenter" })]
        [TestCase(MmvcUnitKind.ModelAndLogic, new[] { "ScoreDataModel", "ScorePresenter", "ScoreLogic" })]
        [TestCase(MmvcUnitKind.Service, new[] { "ScoreService" })]
        public void Write_CreatesEmptyClasses_PerKind(MmvcUnitKind kind, string[] expected) {
            var files = MmvcScaffoldGenerator.Write(kind, _folder, "Score");

            CollectionAssert.AreEqual(expected, files.Select(f => f.TypeName).ToArray());
            foreach (var file in files) {
                var text = File.ReadAllText(file.Path);
                StringAssert.Contains("public class " + file.TypeName + " :", text);
                StringAssert.DoesNotContain("SerializeField", text, "Templates have no field lookups.");
                StringAssert.DoesNotContain("void ", text, "Templates have no dummy members.");
            }
        }

        [Test]
        public void Write_UsesBaseTypesOfTheUnitShape() {
            var files = MmvcScaffoldGenerator.Write(MmvcUnitKind.ModelAndLogic, _folder, "Hud");
            var all = string.Join("\n", files.Select(f => File.ReadAllText(f.Path)));

            StringAssert.Contains("class HudDataModel : BaseObservableDataModel", all);
            StringAssert.Contains("class HudPresenter : BaseObservablePresenter<HudDataModel>", all);
            StringAssert.Contains("class HudLogic : BaseLogic<HudDataModel, HudPresenter>", all);

            var service = MmvcScaffoldGenerator.Write(MmvcUnitKind.Service, _folder, "Audio")[0];
            StringAssert.Contains("class AudioService : MonoBehaviour, ISharedSingleton", File.ReadAllText(service.Path));
        }

        [Test]
        public void Namespace_IsCopiedFromFirstCsFileInFolder() {
            File.WriteAllText(Path.Combine(_folder, "B.cs"), "namespace Other.Ns { class B {} }");
            File.WriteAllText(Path.Combine(_folder, "A.cs"), "using X;\nnamespace Game.Features.Hud {\n class A {} }");

            var files = MmvcScaffoldGenerator.Write(MmvcUnitKind.ViewUnit, _folder, "Hud");

            Assert.AreEqual("Game.Features.Hud.HudPresenter", files[1].FullTypeName);
            StringAssert.Contains("namespace Game.Features.Hud {", File.ReadAllText(files[1].Path));
        }

        [Test]
        public void Namespace_IsEmpty_WhenFolderHasNoScripts() {
            var file = MmvcScaffoldGenerator.Write(MmvcUnitKind.Service, _folder, "Audio")[0];

            Assert.AreEqual("AudioService", file.FullTypeName);
            StringAssert.DoesNotContain("namespace", File.ReadAllText(file.Path));
        }

        [Test]
        public void Write_RefusesToOverwrite_AndWritesNothing() {
            var existing = Path.Combine(_folder, "ScorePresenter.cs");
            File.WriteAllText(existing, "// keep");

            Assert.IsFalse(MmvcScaffoldGenerator.TryValidate(MmvcUnitKind.ViewUnit, _folder, "Score", out var error));
            StringAssert.Contains("never overwrites", error);
            Assert.Throws<IOException>(() => MmvcScaffoldGenerator.Write(MmvcUnitKind.ViewUnit, _folder, "Score"));

            Assert.AreEqual("// keep", File.ReadAllText(existing));
            Assert.IsFalse(File.Exists(Path.Combine(_folder, "ScoreDataModel.cs")), "Nothing is written when one file exists.");
        }

        [TestCase("")]
        [TestCase("1Score")]
        [TestCase("Score Board")]
        public void TryValidate_RejectsInvalidNames(string name) {
            Assert.IsFalse(MmvcScaffoldGenerator.TryValidate(MmvcUnitKind.ViewUnit, _folder, name, out _));
        }

        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator GeneratedSources_Compile_AgainstThePackage() {
            File.WriteAllText(Path.Combine(_folder, "Anchor.cs"), "namespace Scaffold.Check { }");
            MmvcScaffoldGenerator.Write(MmvcUnitKind.ModelAndLogic, _folder, "Hud");
            MmvcScaffoldGenerator.Write(MmvcUnitKind.Service, _folder, "Audio");
            var sources = Directory.GetFiles(_folder, "*.cs");
            var output = Path.Combine(_folder, "ScaffoldCheck.dll");

            var builder = new AssemblyBuilder(output, sources) {
                referencesOptions = ReferencesOptions.UseEngineModules,
                additionalReferences = new[] { typeof(BaseObservableDataModel).Assembly.Location },
                flags = AssemblyBuilderFlags.EditorAssembly
            };
            CompilerMessage[] messages = null;
            bool finished = false;
            builder.buildFinished += (_, result) => { messages = result; finished = true; };

            Assert.IsTrue(builder.Build(), "AssemblyBuilder did not start.");
            var deadline = EditorApplication.timeSinceStartup + 120;
            while (!finished && EditorApplication.timeSinceStartup < deadline) yield return null;

            Assert.IsTrue(finished, "AssemblyBuilder did not finish within 120 s.");
            var errors = messages?.Where(m => m.type == CompilerMessageType.Error).Select(m => m.message).ToArray() ?? Array.Empty<string>();
            CollectionAssert.IsEmpty(errors, string.Join("\n", errors));
            Assert.IsTrue(File.Exists(output));
        }

        [Test]
        public void AttachPending_CreatesGameObjectWithComponentsInOrder_AndClearsState() {
            MmvcScaffoldGenerator.SetPending("ScaffoldUnit", null, new[] {
                typeof(SamplePlayerModel).FullName, typeof(SamplePlayerPresenter).FullName
            });
            Assert.IsTrue(MmvcScaffoldGenerator.HasPending);

            var go = MmvcScaffoldGenerator.AttachPending();
            try {
                Assert.IsNotNull(go);
                Assert.AreEqual("ScaffoldUnit", go.name);
                Assert.IsNotNull(go.GetComponent<SamplePlayerModel>());
                Assert.IsNotNull(go.GetComponent<SamplePlayerPresenter>());
                Assert.IsFalse(MmvcScaffoldGenerator.HasPending);
                Assert.IsNull(MmvcScaffoldGenerator.AttachPending(), "Nothing pending after attach.");
            } finally {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void AttachPending_MissingType_LogsError_CreatesNothing() {
            MmvcScaffoldGenerator.SetPending("Broken", null, new[] { "No.Such.Type" });
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("No.Such.Type' is not compiled"));

            Assert.IsNull(MmvcScaffoldGenerator.AttachPending());
            Assert.IsFalse(MmvcScaffoldGenerator.HasPending);
        }
    }
}
