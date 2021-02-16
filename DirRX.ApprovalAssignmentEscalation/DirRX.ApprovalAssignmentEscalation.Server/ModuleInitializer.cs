using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;
using Sungero.Domain.Initialization;

namespace DirRX.ApprovalAssignmentEscalation.Server
{
  public partial class ModuleInitializer
  {

    public override void Initializing(Sungero.Domain.ModuleInitializingEventArgs e)
    {
      CreateRoles();
    }
    /// <summary>
    /// Создание ролей.
    /// </summary>
    public static void CreateRoles()
    {
      Logger.Debug("Init: creating roles");
      Sungero.Docflow.PublicInitializationFunctions.Module.CreateRole(
        Resources.RoleNameManagersWithoutEsclalation, DirRX.ApprovalAssignmentEscalation.Resources.RoleDescriptionManagersWithoutEsclation, Constants.Module.ManagersWithoutEscalations);
    }
  }
}
