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
      Logger.Debug("AssignmentEscalation. Start");
      
      // Константа, задающая количество, на которое будет делиться порция.
      var chunk = PublicConstants.Module.Chunk;
      
      var assignmentIds = DirRX.Escalation.Functions.Module.GetEscalationAssignmentIds();
      
      Logger.DebugFormat("AssignmentEscalation. Assignment count: {0}", assignmentIds.Count());
      
      if (assignmentIds.Any())
      {
        var i = 0;
        
        while (assignmentIds.Skip(i * chunk).Take(chunk).Any())
        {
          var employeeIdsTake = assignmentIds.Skip(i * chunk).Take(chunk);
          var ids = string.Join(",", employeeIdsTake);
          
          var sendEscalationNotification = AsyncHandlers.SendEscalationNotification.Create();
          sendEscalationNotification.IdsAssignment = ids;
          sendEscalationNotification.ExecuteAsync();
          i++;
        }
      }
      else
        Logger.Debug("Can not Get escalation assignments for escalation");
      
      Logger.Debug("AssignmentEscalation. End");
    }
  }
}