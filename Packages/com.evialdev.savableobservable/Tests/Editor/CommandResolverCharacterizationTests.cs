using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;

namespace SavableObservable.Tests {

    [TestFixture]
    public class CommandResolverCharacterizationTests {

        #region Test Logic Classes

        public class BaseCommandLogic {
            public void BaseMethod() { }
            public virtual void VirtualMethod() { }
            public void ShadowedMethod() { }
            public virtual void OverloadedParam(int x) { }
            public void BaseValueCommand(int x) { }
        }

        public class DerivedCommandLogic : BaseCommandLogic {
            // Shadow base method with 'new'
            public new void ShadowedMethod() { }

            // Override virtual method
            public override void VirtualMethod() { }

            // Static methods
            public static void StaticZeroParam() { }
            public static void StaticOneParam(int x) { }

            // Private / internal methods
            private void PrivateZeroParam() { }
            internal void InternalZeroParam() { }
            private void PrivateOneParam(int x) { }

            // Generic methods
            public void GenericMethod<T>() { }
            public void GenericOneParam<T>(T x) { }
            public void GenericParamMethod<T>(int x) { }

            // Async void method
            public async void AsyncVoidZeroParam() {
                await Task.Yield();
            }
            public async void AsyncVoidOneParam(int x) {
                await Task.Yield();
            }

            // Task / ValueTask / IEnumerator returning methods
            public Task TaskZeroParam() => Task.CompletedTask;
            public Task TaskOneParam(int x) => Task.CompletedTask;
            public ValueTask ValueTaskZeroParam() => default;
            public ValueTask ValueTaskOneParam(int x) => default;
            public IEnumerator CoroutineZeroParam() { yield break; }
            public IEnumerator CoroutineOneParam(int x) { yield break; }

            // Non-void return
            public int IntReturnZeroParam() => 42;
            public int IntReturnOneParam(int x) => x;

            // Parameter count mismatches
            public void ZeroParam() { }
            public void OneParamInt(int x) { }
            public void OneParamFloat(float x) { }
            public void TwoParams(int a, int b) { }

            // Overloaded by parameter type
            public void OverloadedByParam(int x) { }
            public void OverloadedByParam(string s) { }

            // Ambiguous overloads with same signature type on same declaring type (cannot be done in C# with same param, but multiple overloads with different types)
            public void AmbiguousZeroParamOverload() { }

            // ref / out parameters
            public void RefParam(ref int x) { }
            public void OutParam(out int x) { x = 0; }
        }

        public class StaticOnlyLogic {
            public static void OnlyStatic() { }
            public static void OnlyStaticParam(int x) { }
        }

        public class PrivateOnlyLogic {
            private void OnlyPrivate() { }
            private void OnlyPrivateParam(int x) { }
        }

        public class AmbiguousDerivedLogic : BaseCommandLogic {
            // Overloaded method with different parameter types on derived class
            public void MultiParamType(int x) { }
            public void MultiParamType(string s) { }
        }

        public interface ISiblingA {
            void InterfaceCommand();
        }

        public interface ISiblingB {
            void InterfaceCommand();
        }

        public class AmbiguousInterfaceLogic : ISiblingA, ISiblingB {
            void ISiblingA.InterfaceCommand() { }
            void ISiblingB.InterfaceCommand() { }
        }

        #endregion

        #region Reflection Helpers to Invoke Private Resolvers

        private static (MethodInfo method, string error) InvokeResolveClick(Type logicType, string commandName) {
            var method = typeof(Observable).GetMethod("ResolveClickCommandMethod",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "ResolveClickCommandMethod should exist on Observable");
            var result = method.Invoke(null, new object[] { logicType, commandName });
            // result is ValueTuple<MethodInfo, string>
            var tuple = ((MethodInfo, string))result;
            return tuple;
        }

        private static (MethodInfo method, string error) InvokeResolveCommand(Type logicType, string commandName, Type valueType) {
            var method = typeof(Observable).GetMethod("ResolveCommandMethod",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "ResolveCommandMethod should exist on Observable");
            var result = method.Invoke(null, new object[] { logicType, commandName, valueType });
            var tuple = ((MethodInfo, string))result;
            return tuple;
        }

        #endregion

        #region ClickCommand Resolver Characterization Tests

        [Test]
        public void ResolveClick_ValidZeroParam_ReturnsMethod() {
            var (method, error) = InvokeResolveClick(typeof(DerivedCommandLogic), nameof(DerivedCommandLogic.ZeroParam));
            Assert.IsNull(error);
            Assert.IsNotNull(method);
            Assert.AreEqual(nameof(DerivedCommandLogic.ZeroParam), method.Name);
            Assert.AreEqual(typeof(DerivedCommandLogic), method.DeclaringType);
        }

        [Test]
        public void ResolveClick_InheritedMethod_ReturnsMethod() {
            var (method, error) = InvokeResolveClick(typeof(DerivedCommandLogic), nameof(BaseCommandLogic.BaseMethod));
            Assert.IsNull(error);
            Assert.IsNotNull(method);
            Assert.AreEqual(nameof(BaseCommandLogic.BaseMethod), method.Name);
            Assert.AreEqual(typeof(BaseCommandLogic), method.DeclaringType);
        }

        [Test]
        public void ResolveClick_VirtualMethod_ResolvesToMostDerivedCandidate() {
            var (method, error) = InvokeResolveClick(typeof(DerivedCommandLogic), nameof(BaseCommandLogic.VirtualMethod));
            Assert.IsNull(error);
            Assert.IsNotNull(method);
            // Both BaseCommandLogic and DerivedCommandLogic declare VirtualMethod; the derived filter keeps the subclass
            Assert.AreEqual(typeof(DerivedCommandLogic), method.DeclaringType);
        }

        [Test]
        public void ResolveClick_ShadowedNewMethod_ResolvesToDerivedMethod() {
            var (method, error) = InvokeResolveClick(typeof(DerivedCommandLogic), nameof(DerivedCommandLogic.ShadowedMethod));
            Assert.IsNull(error);
            Assert.IsNotNull(method);
            Assert.AreEqual(typeof(DerivedCommandLogic), method.DeclaringType);
        }

        [Test]
        public void ResolveClick_NotFound_ReturnsExpectedError() {
            var (method, error) = InvokeResolveClick(typeof(DerivedCommandLogic), "NonExistentMethod");
            Assert.IsNull(method);
            Assert.AreEqual("Method 'NonExistentMethod' not found on 'DerivedCommandLogic'.", error);
        }

        [Test]
        public void ResolveClick_AllStatic_ReturnsStaticError() {
            var (method, error) = InvokeResolveClick(typeof(StaticOnlyLogic), nameof(StaticOnlyLogic.OnlyStatic));
            Assert.IsNull(method);
            Assert.AreEqual("Method 'OnlyStatic' on 'StaticOnlyLogic' is static. Commands must be public instance methods.", error);
        }

        [Test]
        public void ResolveClick_AllNonPublic_ReturnsNotPublicError() {
            var (method, error) = InvokeResolveClick(typeof(PrivateOnlyLogic), "OnlyPrivate");
            Assert.IsNull(method);
            Assert.AreEqual("Method 'OnlyPrivate' on 'PrivateOnlyLogic' is not public. Commands must be public instance methods.", error);
        }

        [Test]
        public void ResolveClick_GenericMethod_ReturnsGenericError() {
            var (method, error) = InvokeResolveClick(typeof(DerivedCommandLogic), nameof(DerivedCommandLogic.GenericMethod));
            Assert.IsNull(method);
            Assert.AreEqual("Method 'GenericMethod' on 'DerivedCommandLogic' is generic. Generic methods are not supported.", error);
        }

        [Test]
        public void ResolveClick_AsyncVoid_ReturnsAsyncVoidError() {
            var (method, error) = InvokeResolveClick(typeof(DerivedCommandLogic), nameof(DerivedCommandLogic.AsyncVoidZeroParam));
            Assert.IsNull(method);
            Assert.AreEqual("Method 'AsyncVoidZeroParam' on 'DerivedCommandLogic' is async void. Commands must be synchronous.", error);
        }

        [Test]
        public void ResolveClick_TaskReturn_ReturnsTaskError() {
            var (method, error) = InvokeResolveClick(typeof(DerivedCommandLogic), nameof(DerivedCommandLogic.TaskZeroParam));
            Assert.IsNull(method);
            Assert.AreEqual("Method 'TaskZeroParam' on 'DerivedCommandLogic' returns 'Task'. Commands must be synchronous with void return.", error);
        }

        [Test]
        public void ResolveClick_ValueTaskReturn_ReturnsValueTaskError() {
            var (method, error) = InvokeResolveClick(typeof(DerivedCommandLogic), nameof(DerivedCommandLogic.ValueTaskZeroParam));
            Assert.IsNull(method);
            Assert.AreEqual("Method 'ValueTaskZeroParam' on 'DerivedCommandLogic' returns 'ValueTask'. Commands must be synchronous with void return.", error);
        }

        [Test]
        public void ResolveClick_CoroutineReturn_ReturnsCoroutineError() {
            var (method, error) = InvokeResolveClick(typeof(DerivedCommandLogic), nameof(DerivedCommandLogic.CoroutineZeroParam));
            Assert.IsNull(method);
            Assert.AreEqual("Method 'CoroutineZeroParam' on 'DerivedCommandLogic' returns 'IEnumerator'. Commands must be synchronous with void return.", error);
        }

        [Test]
        public void ResolveClick_NonVoidReturn_ReturnsNonVoidError() {
            var (method, error) = InvokeResolveClick(typeof(DerivedCommandLogic), nameof(DerivedCommandLogic.IntReturnZeroParam));
            Assert.IsNull(method);
            Assert.AreEqual("Method 'IntReturnZeroParam' on 'DerivedCommandLogic' returns 'Int32'. Commands must return void.", error);
        }

        [Test]
        public void ResolveClick_HasParameters_ReturnsParameterCountError() {
            var (method, error) = InvokeResolveClick(typeof(DerivedCommandLogic), nameof(DerivedCommandLogic.OneParamInt));
            Assert.IsNull(method);
            Assert.AreEqual("Method 'OneParamInt' on 'DerivedCommandLogic' has 1 parameters. Click commands must have zero parameters.", error);
        }

        #endregion

        #region Command Resolver Characterization Tests

        [Test]
        public void ResolveCommand_ValidOneParam_ReturnsMethod() {
            var (method, error) = InvokeResolveCommand(typeof(DerivedCommandLogic), nameof(DerivedCommandLogic.OneParamInt), typeof(int));
            Assert.IsNull(error);
            Assert.IsNotNull(method);
            Assert.AreEqual(nameof(DerivedCommandLogic.OneParamInt), method.Name);
            Assert.AreEqual(typeof(DerivedCommandLogic), method.DeclaringType);
        }

        [Test]
        public void ResolveCommand_InheritedMethod_ReturnsMethod() {
            var (method, error) = InvokeResolveCommand(typeof(DerivedCommandLogic), nameof(BaseCommandLogic.BaseValueCommand), typeof(int));
            Assert.IsNull(error);
            Assert.IsNotNull(method);
            Assert.AreEqual(nameof(BaseCommandLogic.BaseValueCommand), method.Name);
            Assert.AreEqual(typeof(BaseCommandLogic), method.DeclaringType);
        }

        [Test]
        public void ResolveCommand_OverloadedByParam_SelectsMatchingParamType() {
            var (methodInt, errorInt) = InvokeResolveCommand(typeof(DerivedCommandLogic), nameof(DerivedCommandLogic.OverloadedByParam), typeof(int));
            Assert.IsNull(errorInt);
            Assert.IsNotNull(methodInt);
            Assert.AreEqual(typeof(int), methodInt.GetParameters()[0].ParameterType);

            var (methodStr, errorStr) = InvokeResolveCommand(typeof(DerivedCommandLogic), nameof(DerivedCommandLogic.OverloadedByParam), typeof(string));
            Assert.IsNull(errorStr);
            Assert.IsNotNull(methodStr);
            Assert.AreEqual(typeof(string), methodStr.GetParameters()[0].ParameterType);
        }

        [Test]
        public void ResolveCommand_NotFound_ReturnsExpectedError() {
            var (method, error) = InvokeResolveCommand(typeof(DerivedCommandLogic), "NonExistentCommand", typeof(int));
            Assert.IsNull(method);
            Assert.AreEqual("Method 'NonExistentCommand' not found on 'DerivedCommandLogic'.", error);
        }

        [Test]
        public void ResolveCommand_AllStatic_ReturnsStaticError() {
            var (method, error) = InvokeResolveCommand(typeof(StaticOnlyLogic), nameof(StaticOnlyLogic.OnlyStaticParam), typeof(int));
            Assert.IsNull(method);
            Assert.AreEqual("Method 'OnlyStaticParam' on 'StaticOnlyLogic' is static. Commands must be public instance methods.", error);
        }

        [Test]
        public void ResolveCommand_AllNonPublic_ReturnsNotPublicError() {
            var (method, error) = InvokeResolveCommand(typeof(PrivateOnlyLogic), "OnlyPrivateParam", typeof(int));
            Assert.IsNull(method);
            Assert.AreEqual("Method 'OnlyPrivateParam' on 'PrivateOnlyLogic' is not public. Commands must be public instance methods.", error);
        }

        [Test]
        public void ResolveCommand_GenericMethod_ReturnsGenericError() {
            var (method, error) = InvokeResolveCommand(typeof(DerivedCommandLogic), nameof(DerivedCommandLogic.GenericParamMethod), typeof(int));
            Assert.IsNull(method);
            Assert.AreEqual("Method 'GenericParamMethod' on 'DerivedCommandLogic' is generic. Generic methods are not supported.", error);
        }

        [Test]
        public void ResolveCommand_AsyncVoid_ReturnsAsyncVoidError() {
            var (method, error) = InvokeResolveCommand(typeof(DerivedCommandLogic), nameof(DerivedCommandLogic.AsyncVoidOneParam), typeof(int));
            Assert.IsNull(method);
            Assert.AreEqual("Method 'AsyncVoidOneParam' on 'DerivedCommandLogic' is async void. Commands must be synchronous.", error);
        }

        [Test]
        public void ResolveCommand_TaskReturn_ReturnsTaskError() {
            var (method, error) = InvokeResolveCommand(typeof(DerivedCommandLogic), nameof(DerivedCommandLogic.TaskOneParam), typeof(int));
            Assert.IsNull(method);
            Assert.AreEqual("Method 'TaskOneParam' on 'DerivedCommandLogic' returns 'Task'. Commands must be synchronous with void return.", error);
        }

        [Test]
        public void ResolveCommand_ValueTaskReturn_ReturnsValueTaskError() {
            var (method, error) = InvokeResolveCommand(typeof(DerivedCommandLogic), nameof(DerivedCommandLogic.ValueTaskOneParam), typeof(int));
            Assert.IsNull(method);
            Assert.AreEqual("Method 'ValueTaskOneParam' on 'DerivedCommandLogic' returns 'ValueTask'. Commands must be synchronous with void return.", error);
        }

        [Test]
        public void ResolveCommand_CoroutineReturn_ReturnsCoroutineError() {
            var (method, error) = InvokeResolveCommand(typeof(DerivedCommandLogic), nameof(DerivedCommandLogic.CoroutineOneParam), typeof(int));
            Assert.IsNull(method);
            Assert.AreEqual("Method 'CoroutineOneParam' on 'DerivedCommandLogic' returns 'IEnumerator'. Commands must be synchronous with void return.", error);
        }

        [Test]
        public void ResolveCommand_NonVoidReturn_ReturnsNonVoidError() {
            var (method, error) = InvokeResolveCommand(typeof(DerivedCommandLogic), nameof(DerivedCommandLogic.IntReturnOneParam), typeof(int));
            Assert.IsNull(method);
            Assert.AreEqual("Method 'IntReturnOneParam' on 'DerivedCommandLogic' returns 'Int32'. Commands must return void.", error);
        }

        [Test]
        public void ResolveCommand_WrongParameterCount_ReturnsParameterCountError() {
            var (method, error) = InvokeResolveCommand(typeof(DerivedCommandLogic), nameof(DerivedCommandLogic.TwoParams), typeof(int));
            Assert.IsNull(method);
            Assert.AreEqual("Method 'TwoParams' on 'DerivedCommandLogic' has 2 parameters. Commands must have exactly one parameter matching the value type 'Int32'.", error);
        }

        [Test]
        public void ResolveCommand_WrongParameterType_ReturnsTypeError() {
            var (method, error) = InvokeResolveCommand(typeof(DerivedCommandLogic), nameof(DerivedCommandLogic.OneParamInt), typeof(float));
            Assert.IsNull(method);
            Assert.AreEqual("Method 'OneParamInt' on 'DerivedCommandLogic' parameter type 'Int32' does not match expected value type 'Single'.", error);
        }

        [Test]
        public void ResolveCommand_RefOrOutParam_ReturnsRefOutError() {
            var (methodRef, errorRef) = InvokeResolveCommand(typeof(DerivedCommandLogic), nameof(DerivedCommandLogic.RefParam), typeof(int));
            Assert.IsNull(methodRef);
            Assert.AreEqual("Method 'RefParam' on 'DerivedCommandLogic' parameter 'x' has ref/out modifier. By-ref parameters are not supported.", errorRef);

            var (methodOut, errorOut) = InvokeResolveCommand(typeof(DerivedCommandLogic), nameof(DerivedCommandLogic.OutParam), typeof(int));
            Assert.IsNull(methodOut);
            Assert.AreEqual("Method 'OutParam' on 'DerivedCommandLogic' parameter 'x' has ref/out modifier. By-ref parameters are not supported.", errorOut);
        }

        #endregion
    }
}
