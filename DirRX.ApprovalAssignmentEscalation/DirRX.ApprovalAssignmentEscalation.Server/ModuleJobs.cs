using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;

namespace DirRX.ApprovalAssignmentEscalation.Server
{
  public class ModuleJobs
  {
    public virtual void ApprovalAssignmentEscalation()
    {
      DirRX.ApprovalAssignmentEscalation.Functions.Module.ApprovalAssignmentsEscalation();
    }

  }
}