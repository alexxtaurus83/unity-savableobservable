using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SavableObservable;

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

            variable.OnValueChanged.Add(v => {
                receivedNewValue = v.Value;
                changeCount++;
            }, null);

            // 1. Designer begins GUI edit in Inspector: captures snapshot
            variable.OnBeginGui();

            // 2. Inspector modifies the internal backing field directly via SerializedProperty
            // Simulate editor serialization change:
            variable.Value = 250;

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
