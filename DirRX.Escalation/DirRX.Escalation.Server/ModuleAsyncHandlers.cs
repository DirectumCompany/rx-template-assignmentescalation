using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;

namespace DirRX.Escalation.Server
{
  public partial class ModuleAsyncHandlers
  {

    public virtual void SendEscalationNotification(DirRX.Escalation.Server.AsyncHandlerInvokeArgs.SendEscalationNotificationInvokeArgs args)
    {
      Logger.Debug("SendEscalationNotification. Start");

      var splittedIds = args.IdsAssignment.Split(',')
        .Select(long.Parse)
        .ToList();
      
      DirRX.Escalation.Functions.Module.AssignmentsEscalationChunk(splittedIds);
      
      Logger.Debug("SendEscalationNotification. End");
    }

  }
}