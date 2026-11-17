// Compiler metadata absent from the Unity-compatible reference profile.
namespace System.Runtime.CompilerServices
{
    internal sealed class IsExternalInit { }
    [System.AttributeUsage(System.AttributeTargets.All, Inherited=false)]
    internal sealed class RequiredMemberAttribute:System.Attribute { }
    [System.AttributeUsage(System.AttributeTargets.All, Inherited=false)]
    internal sealed class CompilerFeatureRequiredAttribute:System.Attribute
    {
        public CompilerFeatureRequiredAttribute(string featureName){FeatureName=featureName;}
        public string FeatureName {get;}
        public bool IsOptional {get;init;}
    }
}
namespace System.Diagnostics.CodeAnalysis
{
    [System.AttributeUsage(System.AttributeTargets.Constructor,Inherited=false)]
    internal sealed class SetsRequiredMembersAttribute:System.Attribute { }
}
