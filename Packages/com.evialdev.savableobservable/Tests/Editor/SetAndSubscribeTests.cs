using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SavableObservable;

namespace SavableObservable.Tests {
    [TestFixture]
    public class SetAndSubscribeTests {

        #region Set

        [Test]
        public void Set_DifferentValue_NotifiesAndReturnsTrue() {
            var variable = new ObservableVariable<int> { Value = 1 };
            int count = 0;
            variable.OnValueChanged.Add(_ => count++, null);

            Assert.IsTrue(variable.Set(2));
            Assert.AreEqual(1, count);
            Assert.AreEqual(2, variable.Value);
            Assert.AreEqual(1, variable.PreviousValue);
        }

        [Test]
        public void Set_EqualValue_IsSilentAndReturnsFalse() {
            var variable = new ObservableVariable<string> { Value = "a" };
            int count = 0;
            variable.OnValueChanged.Add(_ => count++, null);

            Assert.IsFalse(variable.Set("a"));
            Assert.AreEqual(0, count);
        }

        [Test]
        public void Set_EqualValueWithNotifyOnSameValue_NotifiesAndReturnsTrue() {
            var variable = new ObservableVariable<int> { Value = 3, NotifyOnSameValue = true };
            int count = 0;
            variable.OnValueChanged.Add(_ => count++, null);

            Assert.IsTrue(variable.Set(3));
            Assert.AreEqual(1, count);
        }

        [Test]
        public void ValueSetter_DelegatesToSet_SameNotificationCount() {
            var viaSetter = new ObservableVariable<int>();
            var viaSet = new ObservableVariable<int>();
            int a = 0, b = 0;
            viaSetter.OnValueChanged.Add(_ => a++, null);
            viaSet.OnValueChanged.Add(_ => b++, null);

            foreach (var v in new[] { 1, 1, 2, 2, 3 }) {
                viaSetter.Value = v;
                viaSet.Set(v);
            }

            Assert.AreEqual(3, a);
            Assert.AreEqual(a, b);
        }

        [Test]
        public void Set_DuringNotification_IsQueued_AndReturnsWhetherItDiffers() {
            var variable = new ObservableVariable<int>();
            var seen = new List<int>();
            bool nestedEqualResult = true, nestedResult = false;
            variable.OnValueChanged.Add(v => {
                seen.Add(v.Value);
                if (v.Value == 1) {
                    nestedEqualResult = variable.Set(1);
                    nestedResult = variable.Set(5);
                }
            }, null);

            variable.Set(1);

            Assert.IsFalse(nestedEqualResult, "Queued equal value reports no change.");
            Assert.IsTrue(nestedResult, "Queued different value reports a change.");
            CollectionAssert.AreEqual(new[] { 1, 5 }, seen);
        }

        [Test]
        public void Set_DuringNotification_ComparesWithLatestQueuedValue() {
            var variable = new ObservableVariable<int>();
            var seen = new List<int>();
            bool toTwo = false, backToOne = false, twoAgain = true;
            variable.OnValueChanged.Add(v => {
                seen.Add(v.Value);
                if (v.Value == 1 && seen.Count == 1) {
                    toTwo = variable.Set(2);
                    backToOne = variable.Set(1);
                    twoAgain = variable.Set(1);
                }
            }, null);

            variable.Set(1);

            Assert.IsTrue(toTwo);
            Assert.IsTrue(backToOne, "2 -> 1 notifies, so Set reports true.");
            Assert.IsFalse(twoAgain, "Duplicate of the last queued value does not notify.");
            CollectionAssert.AreEqual(new[] { 1, 2, 1 }, seen);
        }

        #endregion

        #region Subscribe

        [Test]
        public void VariableSubscribe_ReplayTrue_CallsImmediatelyWithCurrentValue() {
            var variable = new ObservableVariable<int> { Value = 7 };
            var seen = new List<int>();

            using (variable.Subscribe(seen.Add)) {
                variable.Value = 8;
            }

            CollectionAssert.AreEqual(new[] { 7, 8 }, seen);
        }

        [Test]
        public void VariableSubscribe_ReplayFalse_OnlyReceivesChanges() {
            var variable = new ObservableVariable<int> { Value = 7 };
            var seen = new List<int>();

            var handle = variable.Subscribe(seen.Add, replay: false);
            variable.Value = 9;
            handle.Dispose();

            CollectionAssert.AreEqual(new[] { 9 }, seen);
        }

        [Test]
        public void VariableSubscribe_Dispose_StopsNotifications_AndIsIdempotent() {
            var variable = new ObservableVariable<int>();
            int count = 0;
            var handle = variable.Subscribe(_ => count++, replay: false);

            handle.Dispose();
            handle.Dispose();
            variable.Value = 1;

            Assert.AreEqual(0, count);
            Assert.AreEqual(0, variable.OnValueChanged.HandlerCount);
        }

        [Test]
        public void VariableSubscribe_SameHandlerTwice_GivesTwoIndependentSubscriptions() {
            var variable = new ObservableVariable<int>();
            int count = 0;
            Action<int> handler = _ => count++;

            var first = variable.Subscribe(handler, replay: false);
            var second = variable.Subscribe(handler, replay: false);
            variable.Value = 1;
            first.Dispose();
            variable.Value = 2;
            second.Dispose();

            Assert.AreEqual(3, count);
        }

        [Test]
        public void VariableSubscribe_NullHandler_Throws() {
            Assert.Throws<ArgumentNullException>(() => new ObservableVariable<int>().Subscribe(null));
        }

        [Test]
        public void ListSubscribe_ReplayAndChanges_DisposeStops() {
            var list = new ObservableList<int>();
            list.Add(1);
            var counts = new List<int>();

            var handle = list.Subscribe(items => counts.Add(items.Count));
            list.Add(2);
            handle.Dispose();
            list.Add(3);

            CollectionAssert.AreEqual(new[] { 1, 2 }, counts);
            Assert.AreEqual(0, list.OnChanged.HandlerCount);
        }

        #endregion

        #region SubscriptionSet

        private sealed class CountingDisposable : IDisposable {
            public int Count;
            public void Dispose() => Count++;
        }

        private sealed class ThrowingDisposable : IDisposable {
            public void Dispose() => throw new InvalidOperationException("boom");
        }

        [Test]
        public void SubscriptionSet_Clear_DisposesAll_AndStaysOpen() {
            var set = new SubscriptionSet();
            var a = new CountingDisposable();
            var b = new CountingDisposable();
            set.Add(a);
            set.Add(b);
            set.Add(null);

            set.Clear();
            set.Clear();

            Assert.AreEqual(1, a.Count);
            Assert.AreEqual(1, b.Count);
            Assert.AreEqual(0, set.Count);
            set.Add(new CountingDisposable());
            Assert.AreEqual(1, set.Count);
        }

        [Test]
        public void SubscriptionSet_Dispose_IsIdempotent_AndRejectsLaterAdd() {
            var set = new SubscriptionSet();
            var a = new CountingDisposable();
            set.Add(a);

            set.Dispose();
            set.Dispose();

            Assert.AreEqual(1, a.Count);
            Assert.Throws<ObjectDisposedException>(() => set.Add(new CountingDisposable()));
        }

        [Test]
        public void SubscriptionSet_ThrowingHandle_IsLogged_OthersStillDisposed() {
            var set = new SubscriptionSet();
            var a = new CountingDisposable();
            set.Add(a);
            set.Add(new ThrowingDisposable());

            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("boom"));
            set.Clear();

            Assert.AreEqual(1, a.Count);
        }

        [Test]
        public void SubscriptionSet_WithVariableSubscriptions_ClearUnsubscribes() {
            var variable = new ObservableVariable<int>();
            var set = new SubscriptionSet();
            int count = 0;
            set.Add(variable.Subscribe(_ => count++, replay: false));
            set.Add(variable.Subscribe(_ => count++, replay: false));

            variable.Value = 1;
            set.Clear();
            variable.Value = 2;

            Assert.AreEqual(2, count);
            Assert.AreEqual(0, variable.OnValueChanged.HandlerCount);
        }

        #endregion
    }
}
