using System;

namespace Snap.HPatch;

[AttributeUsage(AttributeTargets.All)]
internal sealed class NativeTypeNameAttribute : Attribute
{
    public NativeTypeNameAttribute(string name)
    {
    }
}