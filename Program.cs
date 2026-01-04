// Decompiled with JetBrains decompiler
// Type: CPUUtilityHybrid.Program
// Assembly: XenoCPUUtility, Version=1.5.1.0, Culture=neutral, PublicKeyToken=null
// MVID: 8EA8C267-943B-48AB-9688-875FFCA314A8
// Assembly location: C:\Program Files (x86)\Xeno CPU utility\XenoCPUUtility.dll

using System;
using System.Windows.Forms;

#nullable disable
namespace CPUUtilityHybrid;

internal static class Program
{
  [STAThread]
  private static void Main()
  {
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);
    Application.Run((Form) new Form1());
  }
}
