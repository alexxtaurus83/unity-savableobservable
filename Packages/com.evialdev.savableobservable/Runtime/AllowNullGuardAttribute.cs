using System;

namespace SavableObservable {

    /// <summary>
    /// Marks a reference as optional, so a null check on it is intended (analyzer rule <c>MMVC017</c> does not report it).
    /// Required references (<c>[SerializeField]</c> fields, <c>GetModel()</c>, <c>Services.Get&lt;T&gt;()</c>, <c>Services.GetModel&lt;M&gt;()</c>)
    /// must not be null-checked: a missing assignment or registration should throw. Put it on:
    /// <list type="bullet">
    /// <item>an optional service or model class (every <c>Services.Get</c> of it may be checked),</item>
    /// <item>an optional <c>[SerializeField]</c> field,</item>
    /// <item>a method, property or class whose null checks are all intended.</item>
    /// </list>
    /// Always give the reason. No runtime effect.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
    public sealed class AllowNullGuardAttribute : Attribute {
        public string Reason { get; }

        public AllowNullGuardAttribute(string reason) {
            Reason = reason;
        }
    }
}
