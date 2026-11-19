// Tipos que o compilador C# 9+ procura e que o perfil .NET Standard 2.1 da Unity não traz.
// Públicos de propósito: as demais assemblies LifeVerse.Unity.* (que usam `required`, `init`) enxergam esta cópia.
// Os plugins pré-compilados (LifeVerse.Shared/Scripting) têm a sua própria cópia interna.
namespace System.Runtime.CompilerServices
{
    public static class IsExternalInit { }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    public sealed class RequiredMemberAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    public sealed class CompilerFeatureRequiredAttribute : Attribute
    {
        public CompilerFeatureRequiredAttribute(string featureName) { FeatureName = featureName; }
        public string FeatureName { get; }
        public bool IsOptional { get; init; }
    }
}

namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
    public sealed class SetsRequiredMembersAttribute : Attribute { }
}
