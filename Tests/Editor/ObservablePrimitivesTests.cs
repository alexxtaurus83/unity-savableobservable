using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using SavableObservable;
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
        public void ObservableVariable_IdenticalValueAssignment_FiresNotification_CurrentFrameworkBehavior() {
            var variable = new ObservableVariable<string> { Value = "Hello" };
            int invocationCount = 0;

            variable.OnValueChanged.Add(_ => invocationCount++, null);

            variable.Value = "Hello";

            Assert.AreEqual(1, invocationCount, "Framework currently notifies unconditionally on value set.");
            Assert.AreEqual("Hello", variable.PreviousValue);
            Assert.AreEqual("Hello", variable.Value);
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
#endif

        #endregion

        #region ObservableList Tests

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
            Assert.That(() => list[0] = 42, Is.Not.AllocatingGCMemory());
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
