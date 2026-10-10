#if UNITY_6000_5_OR_NEWER || UNITY_HAS_LIFECYCLE_MANAGEMENT
// Using Unity's built-in Unity.Scripting.LifecycleManagement
#else
namespace Unity.Scripting.LifecycleManagement {
    [System.AttributeUsage(
        System.AttributeTargets.Class |
        System.AttributeTargets.Struct |
        System.AttributeTargets.Field |
        System.AttributeTargets.Property |
        System.AttributeTargets.Event)]
    internal sealed class AutoStaticsCleanupAttribute : System.Attribute { }

    [System.AttributeUsage(
        System.AttributeTargets.Class |
        System.AttributeTargets.Struct |
        System.AttributeTargets.Field |
        System.AttributeTargets.Property |
        System.AttributeTargets.Event)]
    internal sealed class NoAutoStaticsCleanupAttribute : System.Attribute { }
}
#endif
