using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using SavableObservable;
using SavableObservable.Editor;
using SavableObservable.Samples;
#if UNITY_EDITOR
using UnityEditor;
#endif
using Is = UnityEngine.TestTools.Constraints.Is;

namespace SavableObservable.Tests {
    [TestFixture]
    public class ObservablePrimitivesTests {
        #region ObservableVariable Tests

        [Test]
        public void ObservableVariable_ValueAssignment_TriggersOnValueChangedAndUpdatesPrevious() {
            var variable = new ObservableVariable<int> { Value = 5 };
            int receivedNewValue = 0;
            int invocationCount = 0;

            variable.OnValueChanged.Add(v => {
                receivedNewValue = v.Value;
                invocationCount++;
            }, null);

            variable.Value = 42;

            Assert.AreEqual(1, invocationCount);
            Assert.AreEqual(42, receivedNewValue);
            Assert.AreEqual(5, variable.PreviousValue);
            Assert.AreEqual(42, variable.Value);
        }

        [Test]
        public void ObservableVariable_EqualAssignment_DoesNotNotify_AndKeepsPreviousValue() {
            var variable = new ObservableVariable<int> { Value = 1 };
            variable.Value = 2; // Value = 2, PreviousValue = 1
            int invocationCount = 0;

            variable.OnValueChanged.Add(_ => invocationCount++, null);

            variable.Value = 2;

            Assert.AreEqual(0, invocationCount, "Equal assignment must be silent by default.");
            Assert.AreEqual(2, variable.Value);
            Assert.AreEqual(1, variable.PreviousValue, "Suppressed assignment must not touch PreviousValue.");
        }

        [Test]
        public void ObservableVariable_EqualStringAssignment_DoesNotNotify() {
            var variable = new ObservableVariable<string> { Value = "Hello" };
            int invocationCount = 0;
            variable.OnValueChanged.Add(_ => invocationCount++, null);

            variable.Value = "Hello";

            Assert.AreEqual(0, invocationCount);
            Assert.AreEqual("Hello", variable.Value);
        }

        [Test]
        public void ObservableVariable_DifferentAssignment_Notifies() {
            var variable = new ObservableVariable<int> { Value = 1 };
            int invocationCount = 0;
            variable.OnValueChanged.Add(_ => invocationCount++, null);

            variable.Value = 2;

            Assert.AreEqual(1, invocationCount);
            Assert.AreEqual(1, variable.PreviousValue);
        }

        [Test]
        public void ObservableVariable_SameReferenceInstance_IsSuppressed() {
            var instance = new object();
            var variable = new ObservableVariable<object> { Value = instance };
            int invocationCount = 0;
            variable.OnValueChanged.Add(_ => invocationCount++, null);

            variable.Value = instance;
            Assert.AreEqual(0, invocationCount, "Same reference must be treated as equal.");

            variable.Value = new object();
            Assert.AreEqual(1, invocationCount, "A different reference must notify.");
        }

        [Test]
        public void ObservableVariable_EqualEquatableStruct_IsSuppressed() {
            var variable = new ObservableVariable<Vector2Int> { Value = new Vector2Int(3, 4) };
            int invocationCount = 0;
            variable.OnValueChanged.Add(_ => invocationCount++, null);

            variable.Value = new Vector2Int(3, 4);
            Assert.AreEqual(0, invocationCount);

            variable.Value = new Vector2Int(3, 5);
            Assert.AreEqual(1, invocationCount);
        }

        [Test]
        public void ObservableVariable_NaNAssignedOverNaN_IsSuppressed() {
            var variable = new ObservableVariable<float> { Value = float.NaN };
            int invocationCount = 0;
            variable.OnValueChanged.Add(_ => invocationCount++, null);

            variable.Value = float.NaN;

            Assert.AreEqual(0, invocationCount, "EqualityComparer<float>.Default treats NaN as equal to NaN.");
        }

        [Test]
        public void ObservableVariable_NotifyOnSameValueFlag_NotifiesOnEqualAssign_WithPreviousEqualToValue() {
            var variable = new ObservableVariable<int> { Value = 1 };
            variable.Value = 7; // Previous = 1, Value = 7
            variable.NotifyOnSameValue = true;
            int invocationCount = 0;
            int observedValue = 0;
            int observedPrevious = 0;
            variable.OnValueChanged.Add(v => {
                invocationCount++;
                observedValue = v.Value;
                observedPrevious = v.PreviousValue;
            }, null);

            variable.Value = 7;

            Assert.AreEqual(1, invocationCount);
            Assert.AreEqual(7, observedValue);
            Assert.AreEqual(7, observedPrevious, "Equal assignment with the flag on gives PreviousValue == Value.");
        }

        [Test]
        public void ObservableVariable_NotifyOnSameValue_DefaultsToFalse() {
            Assert.IsFalse(new ObservableVariable<int>().NotifyOnSameValue);
        }

        [Test]
        public void ObservableVariable_NotifyOnSameValue_IsNotSerialized() {
            var variable = new ObservableVariable<int> { Value = 5 };
            variable.NotifyOnSameValue = true;

            var json = JsonUtility.ToJson(variable);

            StringAssert.DoesNotContain("otify", json, "The same-value flag must not be serialized.");
            var restored = new ObservableVariable<int>();
            JsonUtility.FromJsonOverwrite(json, restored);
            Assert.AreEqual(5, restored.Value);
            Assert.IsFalse(restored.NotifyOnSameValue);
        }

        private class SameValueAttributeModel : BaseObservableDataModel {
            [NotifyOnSameValue] public ObservableVariable<int> flagged = new ObservableVariable<int>();
            public ObservableVariable<int> plain = new ObservableVariable<int>();
        }

        private class SameValueRuntimeFlagModel : BaseObservableDataModel {
            public ObservableVariable<int> value = new ObservableVariable<int>();
        }

        private class SameValueInvalidTargetsModel : BaseObservableDataModel {
            [NotifyOnSameValue] public ObservableList<int> listField = new ObservableList<int>();
            [NotifyOnSameValue] public int plainField;
        }

        [Test]
        public void NotifyOnSameValueAttribute_Field_NotifiesOnEqualAssign_PlainFieldStaysSilent() {
            var go = new GameObject("SameValueAttr");
            try {
                var model = go.AddComponent<SameValueAttributeModel>();
                model.EnsureFieldsInitialized();
                int flaggedCount = 0;
                int plainCount = 0;
                int previous = -1;
                model.flagged.OnValueChanged.Add(v => { flaggedCount++; previous = v.PreviousValue; }, null);
                model.plain.OnValueChanged.Add(_ => plainCount++, null);

                model.flagged.Value = 3;
                flaggedCount = 0;
                model.flagged.Value = 3;
                model.plain.Value = 0; // default 0 -> equal
                model.plain.Value = 0;

                Assert.IsTrue(model.flagged.NotifyOnSameValue);
                Assert.IsFalse(model.plain.NotifyOnSameValue);
                Assert.AreEqual(1, flaggedCount);
                Assert.AreEqual(3, previous, "Equal assignment on a flagged field gives PreviousValue == Value.");
                Assert.AreEqual(0, plainCount);
            } finally {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void NotifyOnSameValue_RuntimeFlagWithoutAttribute_SurvivesEnsureFieldsInitialized() {
            var go = new GameObject("SameValueRuntime");
            try {
                var model = go.AddComponent<SameValueRuntimeFlagModel>();
                model.EnsureFieldsInitialized();
                int count = 0;
                model.value.OnValueChanged.Add(_ => count++, null);

                model.value.NotifyOnSameValue = true;
                model.EnsureFieldsInitialized();
                model.EnsureFieldsInitialized();

                Assert.IsTrue(model.value.NotifyOnSameValue, "Re-initialization must never reset a runtime opt-in.");
                model.value.Value = 0;
                Assert.AreEqual(1, count);
            } finally {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ObservableVariable_ForceNotify_FiresRegardlessOfFlag() {
            var variable = new ObservableVariable<int> { Value = 4 };
            int count = 0;
            variable.OnValueChanged.Add(_ => count++, null);

            Assert.IsFalse(variable.NotifyOnSameValue);
            variable.ForceNotify();
            Assert.AreEqual(1, count, "ForceNotify must fire with the flag off.");

            variable.NotifyOnSameValue = true;
            variable.ForceNotify();
            Assert.AreEqual(2, count, "ForceNotify must fire with the flag on.");
            Assert.AreEqual(4, variable.PreviousValue);
        }

        [Test]
        public void NotifyOnSameValueAttribute_OnListOrPlainField_LogsNoEffectWarning() {
            // The warning is logged once per model type, when its descriptor is first built, and descriptors stay cached for the whole
            // Editor session. Without a domain reload (a rerun, or an earlier test that touched the type) it would not be logged again,
            // so evict the type first: the test must not depend on what ran before it.
            EvictModelDescriptor(typeof(SameValueInvalidTargetsModel));

            // Expectations first: the warning is emitted when the per-type metadata is first built,
            // which may already happen during AddComponent (Awake).
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"NotifyOnSameValueAttribute\] on 'listField'.*no effect"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"NotifyOnSameValueAttribute\] on 'plainField'.*no effect"));
            var go = new GameObject("SameValueInvalid");
            try {
                var model = go.AddComponent<SameValueInvalidTargetsModel>();

                model.EnsureFieldsInitialized();
                model.EnsureFieldsInitialized(); // cached metadata: warns once per model type only
            } finally {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        /// <summary>Removes the cached <c>ModelDescriptor</c> of <paramref name="modelType"/> so the next use rebuilds it (and logs its one-time warnings again).</summary>
        private static void EvictModelDescriptor(Type modelType) {
            const BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;
            var cacheField = typeof(ModelDescriptor).GetField("_cache", Static);
            var lockField = typeof(ModelDescriptor).GetField("_cacheLock", Static);
            Assert.IsNotNull(cacheField, "ModelDescriptor._cache was renamed: update EvictModelDescriptor.");
            Assert.IsNotNull(lockField, "ModelDescriptor._cacheLock was renamed: update EvictModelDescriptor.");
            var cache = (System.Collections.IDictionary)cacheField.GetValue(null);
            lock (lockField.GetValue(null)) {
                cache.Remove(modelType);
            }
        }

        [Test]
        public void ObservableVariable_ForceNotify_InvokesHandlers() {
            var variable = new ObservableVariable<float> { Value = 3.14f };
            bool notified = false;

            variable.OnValueChanged.Add(_ => notified = true, null);
            variable.ForceNotify();

            Assert.IsTrue(notified);
            Assert.AreEqual(3.14f, variable.PreviousValue);
        }

        [Test]
        public void ObservableVariable_ReferenceType_NullHandling() {
            var variable = new ObservableVariable<string> { Value = "Initial" };
            string notifiedValue = "not-null";

            variable.OnValueChanged.Add(v => notifiedValue = v.Value, null);
            variable.Value = null;

            Assert.IsNull(notifiedValue);
            Assert.IsNull(variable.Value);
            Assert.AreEqual("Initial", variable.PreviousValue);
        }

#if UNITY_EDITOR
        [Test]
        public void ObservableVariable_EditorOnValidate_EmitsNotificationWithCorrectPreviousValue() {
            var variable = new ObservableVariable<int> { Value = 100 };
            int receivedNewValue = 0;
            int changeCount = 0;

            // 1. Designer begins GUI edit in Inspector: captures snapshot
            variable.OnBeginGui();

            // 2. Inspector modifies the backing field directly via SerializedProperty (without triggering C# property setter)
            var backingField = typeof(ObservableVariable<int>).GetField("_value", BindingFlags.Instance | BindingFlags.NonPublic);
            backingField?.SetValue(variable, 250);

            // Subscribe after edit to verify OnValidate fires the change event
            variable.OnValueChanged.Add(v => {
                receivedNewValue = v.Value;
                changeCount++;
            }, null);

            // 3. Unity calls OnValidate()
            variable.OnValidate();

            Assert.AreEqual(100, variable.PreviousValue, "PreviousValue must reflect the pre-GUI snapshot");
            Assert.AreEqual(250, variable.Value);
            Assert.AreEqual(250, receivedNewValue);
            Assert.AreEqual(1, changeCount);
        }

        [Test]
        public void BaseObservableDataModel_OnValidate_ForwardsToObservableFields_WhenSerializedObjectChangesApplied() {
            var host = new GameObject("TestModelHost");
            try {
                var model = host.AddComponent<SamplePlayerModel>();
                model.InitializeDefaults();
                int healthChanges = 0;
                model.health.OnValueChanged.Add(_ => healthChanges++, null);
                int inventoryChanges = 0;
                model.inventory.OnChanged.Add(_ => inventoryChanges++, null);

                var so = new SerializedObject(model);
                var healthProp = so.FindProperty("health._value");
                healthProp.intValue = 75;
                var inventoryProp = so.FindProperty("inventory._items");
                inventoryProp.arraySize = 3;
                inventoryProp.GetArrayElementAtIndex(2).stringValue = "Shield";
                so.ApplyModifiedProperties();

                // Invoke BaseObservableDataModel.OnValidate
                var onValidateMethod = typeof(BaseObservableDataModel).GetMethod("OnValidate", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(onValidateMethod, "BaseObservableDataModel must declare protected virtual OnValidate()");
                onValidateMethod.Invoke(model, null);

                Assert.AreEqual(75, model.health.Value);
                Assert.AreEqual(100, model.health.PreviousValue);
                Assert.AreEqual(1, healthChanges);

                Assert.AreEqual(3, model.inventory.Count);
                CollectionAssert.AreEqual(new[] { "Sword", "Potion", "Shield" }, model.inventory.Value);
                CollectionAssert.AreEqual(new[] { "Sword", "Potion" }, model.inventory.PreviousValue);
                Assert.AreEqual(1, inventoryChanges);
            } finally {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void PropertyResolver_GetTargetObjectsOfProperty_ResolvesAcrossMultiObjectSelection() {
            var hostA = new GameObject("HostA");
            var hostB = new GameObject("HostB");
            try {
                var modelA = hostA.AddComponent<SamplePlayerModel>();
                var modelB = hostB.AddComponent<SamplePlayerModel>();
                modelA.InitializeDefaults();
                modelB.InitializeDefaults();

                var so = new SerializedObject(new UnityEngine.Object[] { modelA, modelB });
                var prop = so.FindProperty("health");
                var targets = PropertyResolver.GetTargetObjectsOfProperty(prop);

                Assert.IsNotNull(targets);
                Assert.AreEqual(2, targets.Length);
                Assert.AreSame(modelA.health, targets[0]);
                Assert.AreSame(modelB.health, targets[1]);
            } finally {
                UnityEngine.Object.DestroyImmediate(hostA);
                UnityEngine.Object.DestroyImmediate(hostB);
            }
        }

        private class BaseHierarchyModel : MonoBehaviour {
            [SerializeField] private ObservableVariable<int> _score = new ObservableVariable<int> { Value = 42 };
            public ObservableVariable<int> ScoreField => _score;
        }

        private class DerivedHierarchyModel : BaseHierarchyModel {
            public int Score => 999;
        }

        [Test]
        public void PropertyResolver_FieldFirstHierarchyLookup_PrefersFieldOverProperty() {
            var host = new GameObject("HierarchyTestHost");
            try {
                var derived = host.AddComponent<DerivedHierarchyModel>();
                var so = new SerializedObject(derived);
                var prop = so.FindProperty("_score");
                var target = PropertyResolver.GetTargetObjectOfProperty(prop);

                Assert.IsNotNull(target);
                Assert.AreSame(derived.ScoreField, target);
            } finally {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }
#endif

        #endregion

        #region ObservableList Tests

        [Test]
        public void ObservableList_InspectorValidation_TracksPreviousSnapshotAndUndo() {
            var list = new ObservableList<int>();
            list.Add(1);
            int changes = 0;
            list.OnChanged.Add(_ => changes++, null);

            list.OnBeginGui();
            list.Value.Add(2);
            list.OnValidate();
            CollectionAssert.AreEqual(new[] { 1 }, list.PreviousValue);
            CollectionAssert.AreEqual(new[] { 1, 2 }, list.Value);
            Assert.AreEqual(1, changes);

            // Undo can validate without a preceding inspector draw.
            list.Value.RemoveAt(1);
            list.OnValidate();
            CollectionAssert.AreEqual(new[] { 1, 2 }, list.PreviousValue);
            CollectionAssert.AreEqual(new[] { 1 }, list.Value);
            Assert.AreEqual(2, changes);
            list.OnValidate();
            Assert.AreEqual(2, changes, "An unchanged validation must not notify again.");
        }

        [Test]
        public void ObservableList_NullItemsDeserializationGuard_SucceedsWithoutException() {
            var list = new ObservableList<int>();
            // Force _items to null via reflection (simulating deserialization)
            var itemsField = typeof(ObservableList<int>).GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(itemsField);
            itemsField.SetValue(list, null);

            Assert.AreEqual(0, list.Count);
            Assert.AreEqual("Count = 0", list.ToString());
            Assert.DoesNotThrow(() => {
                foreach (var item in list) { }
            });
            Assert.DoesNotThrow(() => list.Add(42));
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual(42, list[0]);
            Assert.DoesNotThrow(() => list.Clear());
            Assert.AreEqual(0, list.Count);
        }

        [Test]
        public void ObservableList_AddAndRemove_MutatesAndFiresChangeNotification() {
            var list = new ObservableList<string>();
            int changes = 0;

            list.OnChanged.Add(_ => changes++, null);

            list.Add("Item1");
            Assert.AreEqual(1, changes);
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual(0, list.PreviousValue.Count);

            list.Add("Item2");
            Assert.AreEqual(2, changes);
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual(1, list.PreviousValue.Count);
            Assert.AreEqual("Item1", list.PreviousValue[0]);

            list.Remove("Item1");
            Assert.AreEqual(3, changes);
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual("Item2", list[0]);

            list.Clear();
            Assert.AreEqual(4, changes);
            Assert.AreEqual(0, list.Count);
        }

        [Test]
        public void ObservableList_IndexerAssignment_FiresChangeNotification() {
            var list = new ObservableList<int> { 10, 20, 30 };
            int changes = 0;

            list.OnChanged.Add(_ => changes++, null);

            list[1] = 99;

            Assert.AreEqual(1, changes);
            Assert.AreEqual(99, list[1]);
            Assert.AreEqual(20, list.PreviousValue[1]);
        }

        [Test]
        public void ObservableList_ValueReplacement_ReplacesUnderlyingList() {
            var list = new ObservableList<string> { "A", "B" };
            bool changed = false;

            list.OnChanged.Add(_ => changed = true, null);
            list.Value = new List<string> { "X", "Y", "Z" };

            Assert.IsTrue(changed);
            Assert.AreEqual(3, list.Count);
            Assert.AreEqual("X", list[0]);
            Assert.AreEqual(2, list.PreviousValue.Count);
        }

        [Test]
        public void ObservableList_AddRange_FiresSingleNotificationAndPreservesPreviousSnapshot() {
            var list = new ObservableList<string> { "Initial" };
            int changes = 0;

            list.OnChanged.Add(_ => changes++, null);
            list.AddRange(new[] { "Second", "Third" });

            Assert.AreEqual(1, changes);
            Assert.AreEqual(3, list.Count);
            Assert.AreEqual(1, list.PreviousValue.Count);
            Assert.AreEqual("Initial", list.PreviousValue[0]);
        }

        [Test]
        public void ObservableList_InsertAndRemoveAt_MutatesCorrectly() {
            var list = new ObservableList<int> { 1, 3 };
            int changes = 0;

            list.OnChanged.Add(_ => changes++, null);

            list.Insert(1, 2);
            Assert.AreEqual(1, changes);
            Assert.AreEqual(3, list.Count);
            Assert.AreEqual(2, list[1]);

            list.RemoveAt(0);
            Assert.AreEqual(2, changes);
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual(2, list[0]);
        }

        [Test]
        public void ObservableList_PreviousValue_PopulatedWithoutSubscribers() {
            var list = new ObservableList<string>();

            // Mutation without any OnChanged subscriber registered
            list.Add("Alpha");
            Assert.AreEqual(0, list.PreviousValue.Count, "Initial mutation PreviousValue should capture empty snapshot");

            list.Add("Beta");
            Assert.AreEqual(1, list.PreviousValue.Count, "Second mutation PreviousValue should capture pre-mutation items");
            Assert.AreEqual("Alpha", list.PreviousValue[0]);

            list[0] = "Gamma";
            Assert.AreEqual("Alpha", list.PreviousValue[0], "Indexer mutation PreviousValue should capture state prior to edit");

            list.Remove("Beta");
            Assert.AreEqual(2, list.PreviousValue.Count, "Removal mutation PreviousValue should capture 2 items prior to remove");
            Assert.AreEqual("Gamma", list.PreviousValue[0]);
            Assert.AreEqual("Beta", list.PreviousValue[1]);

            list.Clear();
            Assert.AreEqual(1, list.PreviousValue.Count, "Clear mutation PreviousValue should capture items before clearing");
            Assert.AreEqual("Gamma", list.PreviousValue[0]);
        }

        [Test]
        public void ObservableList_PreviousValue_MutationsWithoutSubscribers_RebuiltCorrectly() {
            var list = new ObservableList<string> { "A", "C" };

            // 1. Insert (middle)
            list.Insert(1, "B");
            Assert.AreEqual(2, list.PreviousValue.Count);
            Assert.AreEqual("A", list.PreviousValue[0]);
            Assert.AreEqual("C", list.PreviousValue[1]);

            // 2. RemoveAt (first) - list is ["A", "B", "C"]
            list.RemoveAt(0);
            Assert.AreEqual(3, list.PreviousValue.Count);
            Assert.AreEqual("A", list.PreviousValue[0]);
            Assert.AreEqual("B", list.PreviousValue[1]);
            Assert.AreEqual("C", list.PreviousValue[2]);

            // 3. RemoveAt (middle) - list is ["B", "C"]
            list.Add("D"); // list is ["B", "C", "D"]
            list.RemoveAt(1); // removes "C", list is ["B", "D"]
            Assert.AreEqual(3, list.PreviousValue.Count);
            Assert.AreEqual("B", list.PreviousValue[0]);
            Assert.AreEqual("C", list.PreviousValue[1]);
            Assert.AreEqual("D", list.PreviousValue[2]);

            // 4. RemoveAt (last) - list is ["B", "D"]
            list.RemoveAt(list.Count - 1); // removes "D", list is ["B"]
            Assert.AreEqual(2, list.PreviousValue.Count);
            Assert.AreEqual("B", list.PreviousValue[0]);
            Assert.AreEqual("D", list.PreviousValue[1]);

            // 5. AddRange (array) - list is ["B"]
            list.AddRange(new[] { "X", "Y" });
            Assert.AreEqual(1, list.PreviousValue.Count);
            Assert.AreEqual("B", list.PreviousValue[0]);

            // 6. AddRange (lazy IEnumerable) - list is ["B", "X", "Y"]
            static IEnumerable<string> LazyItems() {
                yield return "Z1";
                yield return "Z2";
            }
            list.AddRange(LazyItems());
            Assert.AreEqual(3, list.PreviousValue.Count);
            Assert.AreEqual("B", list.PreviousValue[0]);
            Assert.AreEqual("X", list.PreviousValue[1]);
            Assert.AreEqual("Y", list.PreviousValue[2]);

            // 7. ForceNotify - list is ["B", "X", "Y", "Z1", "Z2"]
            list.ForceNotify();
            Assert.AreEqual(5, list.PreviousValue.Count);
            Assert.AreEqual("B", list.PreviousValue[0]);
            Assert.AreEqual("Z2", list.PreviousValue[4]);

            // 8. Value replacement
            list.Value = new List<string> { "New1", "New2" };
            Assert.AreEqual(5, list.PreviousValue.Count);
            Assert.AreEqual("B", list.PreviousValue[0]);
            Assert.AreEqual("Z2", list.PreviousValue[4]);

            // 9. Indexer set on last element - list is ["New1", "New2"]
            list[list.Count - 1] = "ReplacedLast";
            Assert.AreEqual(2, list.PreviousValue.Count);
            Assert.AreEqual("New1", list.PreviousValue[0]);
            Assert.AreEqual("New2", list.PreviousValue[1]);
        }

        [Test]
        public void ObservableList_PreviousValue_ReferencePersistsAcrossSubsequentMutations() {
            var list = new ObservableList<string>();
            list.Add("Alpha");
            var prev1 = list.PreviousValue;
            Assert.AreEqual(0, prev1.Count);

            list.Add("Beta");
            var prev2 = list.PreviousValue;
            Assert.AreEqual(1, prev2.Count);
            Assert.AreEqual("Alpha", prev2[0]);

            // Ensure prev1 did not mutate when prev2 was created or list was mutated
            Assert.AreEqual(0, prev1.Count);

            list[0] = "Gamma";
            var prev3 = list.PreviousValue;
            Assert.AreEqual(2, prev3.Count);
            Assert.AreEqual("Alpha", prev3[0]);
            Assert.AreEqual("Beta", prev3[1]);

            // Earlier references remain unchanged
            Assert.AreEqual(0, prev1.Count);
            Assert.AreEqual(1, prev2.Count);
            Assert.AreEqual("Alpha", prev2[0]);
        }

        [Test]
        public void ObservableList_PreviousValue_MultipleReadsWithoutMutation_ReturnsSameInstance() {
            var list = new ObservableList<int>();
            list.Add(10);
            list.Add(20);

            var firstRead = list.PreviousValue;
            var secondRead = list.PreviousValue;

            Assert.AreSame(firstRead, secondRead);
        }

        [Test]
        public void ObservableList_Mutations_DoNotAllocateGCMemory() {
            var list = new ObservableList<int>();
            list.Value.Capacity = 64;

            _ = list.OnChanged;
            list.Add(1);

            Assert.That(() => list.Add(2), Is.Not.AllocatingGCMemory());
            Assert.That(() => list.Insert(1, 99), Is.Not.AllocatingGCMemory());
            Assert.That(() => { list[0] = 42; }, Is.Not.AllocatingGCMemory());
            Assert.That(() => list.RemoveAt(1), Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void ObservableList_PreviousValue_WhenOutsideModificationOccurs_LogsWarningAndReturnsCurrent() {
            var list = new ObservableList<string>();
            list.Add("Alpha");

            list.Value.Add("Beta");

            LogAssert.Expect(LogType.Warning, "ObservableList: External modification of underlying items detected. Delta cannot be applied; returning current list as PreviousValue.");

            IReadOnlyList<string> previous = null;
            Assert.DoesNotThrow(() => previous = list.PreviousValue);
            Assert.IsNotNull(previous);
            Assert.AreEqual(2, previous.Count);
            Assert.AreEqual("Alpha", previous[0]);
            Assert.AreEqual("Beta", previous[1]);
        }

        [Test]
        public void ObservableVariable_CustomStructAndEnum_WorksCorrectly() {
            var structVar = new ObservableVariable<Vector2Int> { Value = new Vector2Int(1, 2) };
            Vector2Int observed = Vector2Int.zero;
            structVar.OnValueChanged.Add(v => observed = v.Value, null);

            structVar.Value = new Vector2Int(5, 10);
            Assert.AreEqual(new Vector2Int(5, 10), observed);
            Assert.AreEqual(new Vector2Int(1, 2), structVar.PreviousValue);
        }

        #endregion

        #region ObservableTrackedAction Tests

        [Test]
        public void ObservableTrackedAction_AddRemove_OperatorsAndCount() {
            var action = new ObservableTrackedAction<ObservableVariable<int>>();
            Action<ObservableVariable<int>> handler1 = _ => { };
            Action<ObservableVariable<int>> handler2 = _ => { };

            Assert.AreEqual(0, action.HandlerCount);

            action += handler1;
            Assert.AreEqual(1, action.HandlerCount);

            action += handler2;
            Assert.AreEqual(2, action.HandlerCount);

            action -= handler1;
            Assert.AreEqual(1, action.HandlerCount);

            action -= handler2;
            Assert.AreEqual(0, action.HandlerCount);
        }

        [Test]
        public void ObservableTrackedAction_DuplicateAdd_Prevented() {
            var action = new ObservableTrackedAction<ObservableVariable<int>>();
            Action<ObservableVariable<int>> handler = _ => { };

            action.Add(handler, null);
            action.Add(handler, null);

            Assert.AreEqual(1, action.HandlerCount);
        }

        #endregion
    }
}
