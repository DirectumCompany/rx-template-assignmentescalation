using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;

namespace DirRX.Escalation.Server
{
  public partial class ModuleJobs
  {
    public virtual void AssignmentEscalation()
    {
      DirRX.Escalation.Functions.Module.AssignmentsEscalation();
    }
  }
}