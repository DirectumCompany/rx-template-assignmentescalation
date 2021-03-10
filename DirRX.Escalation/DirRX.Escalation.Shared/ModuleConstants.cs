using System;
using Sungero.Core;

namespace DirRX.Escalation.Constants
{
  public static class Module
  {
    /// <summary>
    /// GUID роли "Руководители, не участвующие в процессе эскалации"
    /// </summary>
    [Public]
    public static readonly Guid ManagersWithoutEscalations = Guid.Parse("39C47E09-69B9-4192-AC09-E806D3860FD0");
  }
}