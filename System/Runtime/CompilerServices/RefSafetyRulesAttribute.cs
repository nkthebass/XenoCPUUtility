// Decompiled with JetBrains decompiler
// Type: System.Runtime.CompilerServices.RefSafetyRulesAttribute
// Assembly: XenoCPUUtility, Version=1.5.1.0, Culture=neutral, PublicKeyToken=null
// MVID: 8EA8C267-943B-48AB-9688-875FFCA314A8
// Assembly location: C:\Program Files (x86)\Xeno CPU utility\XenoCPUUtility.dll

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

#nullable disable
namespace System.Runtime.CompilerServices
{
  [CompilerGenerated]
  [Microsoft.CodeAnalysis.Embedded]
  [AttributeUsage(AttributeTargets.Module, AllowMultiple = false, Inherited = false)]
  internal sealed class RefSafetyRulesAttribute : Attribute
  {
    public readonly int Version;

    public RefSafetyRulesAttribute([In] int obj0) => Version = obj0;
  }
}

namespace Microsoft.CodeAnalysis
{
  [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Enum | AttributeTargets.Delegate | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Event | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
  internal sealed class EmbeddedAttribute : Attribute
  {
  }
}
