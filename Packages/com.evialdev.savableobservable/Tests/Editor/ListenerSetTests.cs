using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using SavableObservable;

namespace SavableObservable.Tests {

    [TestFixture]
    public class ListenerSetTests {

        private class TargetHelper {
            public int CallCount;
            public string LastString;

            public void ZeroArgAction() {
                CallCount++;
            }

            public void OneArgAction(string val) {
                CallCount++;
                LastString = val;
            }
        }

        [Test]
        public void Listen_ZeroArg_AttachesAndFires() {
            var set = new ListenerSet();
            var evt = new UnityEvent();
            var helper = new TargetHelper();

            set.Listen(evt, helper.ZeroArgAction);
            evt.Invoke();

            Assert.AreEqual(1, helper.CallCount);

            set.Dispose();
        }

        [Test]
        public void Listen_OneArg_AttachesAndFires() {
            var set = new ListenerSet();
            var evt = new UnityEvent<string>();
            var helper = new TargetHelper();

            set.Listen(evt, helper.OneArgAction);
            evt.Invoke("hello");

            Assert.AreEqual(1, helper.CallCount);
            Assert.AreEqual("hello", helper.LastString);

            set.Dispose();
        }

        [Test]
        public void Listen_Deduplicates_SameEventAndEqualDelegate() {
            var set = new ListenerSet();
            var evt = new UnityEvent();
            var helper = new TargetHelper();

            // Method group conversions create distinct delegate instances pointing to same target/method
            set.Listen(evt, helper.ZeroArgAction);
            set.Listen(evt, helper.ZeroArgAction);

            evt.Invoke();

            Assert.AreEqual(1, helper.CallCount);

            set.Dispose();
        }

        [Test]
        public void Listen_OneArg_Deduplicates_SameEventAndEqualDelegate() {
            var set = new ListenerSet();
            var evt = new UnityEvent<string>();
            var helper = new TargetHelper();

            set.Listen(evt, helper.OneArgAction);
            set.Listen(evt, helper.OneArgAction);

            evt.Invoke("test");

            Assert.AreEqual(1, helper.CallCount);

            set.Dispose();
        }

        [Test]
        public void Listen_IgnoresNullEventOrHandler() {
            var set = new ListenerSet();
            var evt = new UnityEvent();
            var evtString = new UnityEvent<string>();

            Assert.DoesNotThrow(() => set.Listen(null, () => { }));
            Assert.DoesNotThrow(() => set.Listen(evt, (UnityAction)null));
            Assert.DoesNotThrow(() => set.Listen<string>(null, val => { }));
            Assert.DoesNotThrow(() => set.Listen<string>(evtString, (UnityAction<string>)null));

            set.Dispose();
        }

        [Test]
        public void Clear_RemovesListeners_AllowsReuse() {
            var set = new ListenerSet();
            var evt = new UnityEvent();
            var helper = new TargetHelper();

            set.Listen(evt, helper.ZeroArgAction);
            set.Clear();

            evt.Invoke();
            Assert.AreEqual(0, helper.CallCount);

            // Set is reusable after Clear
            set.Listen(evt, helper.ZeroArgAction);
            evt.Invoke();
            Assert.AreEqual(1, helper.CallCount);

            set.Dispose();
        }

        [Test]
        public void Dispose_RemovesListeners_ThrowsOnSubsequentListen() {
            var set = new ListenerSet();
            var evt = new UnityEvent();
            var helper = new TargetHelper();

            set.Listen(evt, helper.ZeroArgAction);
            set.Dispose();

            evt.Invoke();
            Assert.AreEqual(0, helper.CallCount);

            Assert.Throws<ObjectDisposedException>(() => set.Listen(evt, helper.ZeroArgAction));
            Assert.Throws<ObjectDisposedException>(() => set.Listen(new UnityEvent<int>(), val => { }));
        }

        [Test]
        public void Dispose_Repeated_IsIdempotent() {
            var set = new ListenerSet();
            set.Dispose();
            Assert.DoesNotThrow(() => set.Dispose());
            Assert.DoesNotThrow(() => set.Clear());
        }

        [Test]
        public void Clear_DoesNotRemoveNonOwnedListeners() {
            var set = new ListenerSet();
            var evt = new UnityEvent();
            int directCalls = 0;
            int setCalls = 0;

            UnityAction directAction = () => directCalls++;
            evt.AddListener(directAction);

            set.Listen(evt, () => setCalls++);
            set.Clear();

            evt.Invoke();
            Assert.AreEqual(1, directCalls);
            Assert.AreEqual(0, setCalls);

            evt.RemoveListener(directAction);
            set.Dispose();
        }

        [Test]
        public void RollbackTo_RollsBackRecentEntriesOnly() {
            var set = new ListenerSet();
            var evt1 = new UnityEvent();
            var evt2 = new UnityEvent();
            int calls1 = 0;
            int calls2 = 0;

            set.Listen(evt1, () => calls1++);
            int checkpoint = set.Count;

            set.Listen(evt2, () => calls2++);
            set.RollbackTo(checkpoint);

            evt1.Invoke();
            evt2.Invoke();

            Assert.AreEqual(1, calls1);
            Assert.AreEqual(0, calls2);

            set.Dispose();
        }

        [Test]
        public void MultipleOwnerSets_DoNotInterfere() {
            var setA = new ListenerSet();
            var setB = new ListenerSet();
            var evt = new UnityEvent();
            int callsA = 0;
            int callsB = 0;

            setA.Listen(evt, () => callsA++);
            setB.Listen(evt, () => callsB++);

            evt.Invoke();
            Assert.AreEqual(1, callsA);
            Assert.AreEqual(1, callsB);

            setA.Clear();
            evt.Invoke();
            Assert.AreEqual(1, callsA);
            Assert.AreEqual(2, callsB);

            setB.Dispose();
            setA.Dispose();
        }
    }
}
