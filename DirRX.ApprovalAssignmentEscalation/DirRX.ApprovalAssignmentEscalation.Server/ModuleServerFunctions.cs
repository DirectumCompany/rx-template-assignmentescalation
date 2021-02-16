using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;

namespace DirRX.ApprovalAssignmentEscalation.Server
{
  public class ModuleFunctions
  {
    /// <summary>
    /// Получить задачу на продление срока по заданию.
    /// </summary>
    /// <param name="assignment">Задание.</param>
    /// <returns>Задача, на основе которой создано задание.</returns>
    private  Sungero.Docflow.IDeadlineExtensionTask GetDeadlineExtension(Sungero.Workflow.IAssignment assignment)
    {
      var task = Sungero.Docflow.DeadlineExtensionTasks.GetAll()
        .Where(j => Equals(j.ParentAssignment, assignment))
        .Where(j => j.Status == Sungero.Workflow.Task.Status.InProcess || j.Status == Sungero.Workflow.Task.Status.Draft)
        .FirstOrDefault();
      return task;
    }
    
    /// <summary>
    /// Пользователь руководитель департамента.
    /// </summary>
    /// <param name="user">Пользователь.</param>
    /// <returns>Признак, что  пользователь является руководителем подразделения.</returns>
    public  bool IsUserDepartmentManager(Sungero.CoreEntities.IUser user)
    {
      var employee = Sungero.Company.Employees.GetAll(e => Equals(user, e)).SingleOrDefault();
      var employeeDepartment = employee != null ? employee.Department : null;
      
      if (employeeDepartment == null)
        return false;
      
      return Equals(employeeDepartment.Manager, employee);
    }
    
    /// <summary>
    /// Отправить уведомление о задержке.
    /// </summary>
    /// <param name="assignment">Задание.</param>
    /// <param name="toManager">Уведомление руководителю.</param>
    private  void SendEscalationNotification(Sungero.Workflow.IAssignment assignment, bool toManager)
    {
      var document = Sungero.Docflow.OfficialDocuments.Null;
      if (Sungero.Docflow.ApprovalAssignments.Is(assignment))
        document = Sungero.Docflow.ApprovalAssignments.As(assignment).DocumentGroup.OfficialDocuments.First();
      else
        document = Sungero.Docflow.ApprovalManagerAssignments.As(assignment).DocumentGroup.OfficialDocuments.First();
      
      var performer = Sungero.Company.Employees.Null;
      var text = string.Empty;
      var subject = string.Empty;
      if (toManager)
      {
        performer = GetManager(assignment.Performer);
        text = Resources.ManagerNotificationTextFormat(Hyperlinks.Get(assignment));
        subject = Resources.ManagerNotificationSubjectFormat(document.Name);
      }
      else
      {
        performer = Sungero.Company.Employees.GetAll(e => Equals(assignment.Performer, e)).SingleOrDefault();
        text = Resources.FirstPerformerNotificationTextFormat(Hyperlinks.Get(assignment));
        subject = Resources.FirstPerformerNotificationSubjectFormat(assignment.Subject);
      }
      
      if (performer != null)
      {
        var notice = Sungero.Workflow.SimpleTasks.CreateWithNotices(subject, performer);
        notice.ActiveText = text;
        notice.Attachments.Add(assignment);
        notice.Start();
      }
    }
    
    public virtual List<Sungero.Docflow.IApprovalAssignment> GetEscalationApprovalAssignments()
    {
      return Sungero.Docflow.ApprovalAssignments.GetAll()
        .Where(x => x.Status == Sungero.Docflow.ApprovalAssignment.Status.InProcess)
        .Where(x => x.Deadline < Calendar.Now)
        .ToList();
    }
    
    /// <summary>
    /// Эскалация заданий на согласование.
    /// </summary>
    public  void ApprovalAssignmentsEscalation()
    {
      var assignments = GetEscalationApprovalAssignments();
      
      foreach (var assignment in assignments)
      {
        var deadlineExtension = GetDeadlineExtension(assignment);
        
        if (deadlineExtension != null && deadlineExtension.Status == Sungero.Workflow.Task.Status.InProcess)
          continue;
        
        var shouldNotifyManager = ShouldSendEscalationNotificationToManager(assignment);
          
        if (deadlineExtension != null && deadlineExtension.Status == Sungero.Workflow.Task.Status.Draft)
        {
          deadlineExtension.ActiveText = Resources.ApprovalPeriodExpired;
          deadlineExtension.Abort();
        }

        if (Functions.Module.IsUserDepartmentManager(assignment.Performer))
        {
          if (shouldNotifyManager)
            SendEscalationNotification(assignment, true);
          continue;
        }
        
        var manager = GetManager(assignment.Performer);
        if (manager != null)
        {
          if (manager.IncludedIn(Constants.Module.ManagersWithoutEscalations))
          {
            if (shouldNotifyManager)
              SendEscalationNotification(assignment, true);
            continue;
          }
          
          assignment.Addressee = manager;
          assignment.ActiveText = Resources.ApprovalPeriodExpired;
          
            if (CanForward(assignment))
            assignment.Complete(Sungero.Docflow.ApprovalAssignment.Result.Approved);
          else
            assignment.Complete(Sungero.Docflow.ApprovalAssignment.Result.Forward);
          
          SendEscalationNotification(assignment, false);
        }
      }
    }
       
    /// <summary>
    /// Определить руководителя сотрудника.
    /// </summary>
    /// <param name="user">Пользователь.</param>
    /// <returns>Руководитель.</returns>
    public static Sungero.Company.IEmployee GetManager(IUser user)
    {
      var department = GetDepartment(user);
      if (department == null)
        return Sungero.Company.Employees.Null;
      
      var manager = department.Manager;
      
      // Если сотрудник является руководителем своего же подразделения,
      // тогда его непосредственным руководителем является руководитель головного подразделения.
      if (manager == null || (manager != null && Equals(user, Users.As(manager))))
        manager = GetHeadDepartmentManager(department);
      
      return manager;
    }
    
    /// <summary>
    /// Получить подразделение сотрудника.
    /// </summary>
    /// <param name="user">Пользователь.</param>
    /// <returns>Подразделение сотрудника.</returns>
    private static Sungero.Company.IDepartment GetDepartment(IUser user)
    {
      var employee = Sungero.Company.Employees.GetAll().FirstOrDefault(u => Equals(u, user));
      
      if (employee == null)
        return null;

      return employee.Department;
    }
    
    /// <summary>
    /// Получить вышестоящего руководителя.
    /// </summary>
    /// <param name="department">Подразделение.</param>
    /// <returns>Руководитель.</returns>
    private static Sungero.Company.IEmployee GetHeadDepartmentManager(Sungero.Company.IDepartment department)
    {
      var manager = Sungero.Company.Employees.Null;
      var currentDepartment = department;
      var repeat = true;
      while (repeat)
      {
        var headDepartment = currentDepartment.HeadOffice;
        if (headDepartment == null)
          repeat = false;
        else
        {
          if (headDepartment.Manager == null)
            currentDepartment = headDepartment;
          else
          {
            manager = headDepartment.Manager;
            repeat = false;
          }
        }
      }
      return manager;
    }
    
    /// <summary>
    /// Нужно ли оправить уведомление о задежке задания руководителю/
    /// </summary>
    /// <param name="assignment">Задание.</param>
    /// <param name="manager">Руководитель.</param>
    /// <returns></returns>
    public virtual bool ShouldSendEscalationNotificationToManager(Sungero.Docflow.IApprovalAssignment assignment)
    {
      return true; 
    }
    
    public virtual bool CanForward(Sungero.Docflow.IApprovalAssignment assignment)
    {
      return true;
    }
      //    public virtual bool CanForwardAssignment(Sungero.Workflow.IAssignment assignment, Sungero.Company.IEmployee manager)
      //    {
      //      if (Sungero.Docflow.ApprovalAssignments.Is(assignment))
      //      {
      //        Sungero.Docflow.ApprovalAssignments.As().for
      //        var assignments = Sungero.Docflow.ApprovalAssignments.GetAll(a => Equals(a.Task, assignment.Task) &&
      //                                                                     Equals(a.TaskStartId, assignment.TaskStartId) &&
      //                                                                     Equals(a.IterationId, assignment.IterationId));
//
      //        // Если у сотрудника есть хоть одно задание в работе - считаем что нет смысла дублировать ему задания.
      //        // BUG: если assignments материализовать (завернуть ToList), то в задании можно будет переадресовать самому себе, т.к. в BeforeComplete задание считается уже выполненным.
      //        var hasInProcess = assignments.Where(a => Equals(a.Status, Sungero.Docflow.ApprovalAssignment.Status.InProcess) && Equals(a.Performer, manager)).Any();
      //        if (hasInProcess)
      //          return false;
//
      //        // При последовательном выполнении сотрудники ещё не получили задания, вычисляем их.
      //        var currentStageApprovers = GetCurrentIterationEmployeesWithoutAssignment(assignment);
      //        if (currentStageApprovers.Contains(manager))
      //          return false;
//
      //        var materialized = assignments.ToList();
      //        // Если у сотрудника нет заданий в работе, проверяем, все ли его задания созданы.
      //        foreach (var assign in materialized)
      //        {
      //          var added = assignment.ForwardedTo.Count(u => Equals(u, manager));
      //          var created = materialized.Count(a => Equals(a.Performer, manager) && Equals(a.ForwardedFrom, assignment));
      //          if (added != created)
      //            return false;
      //        }
//
      //        return true;
      //      }
      //      else
      //        return false;
      //    }
//
      //    private List<Sungero.Company.IEmployee> GetCurrentIterationEmployeesWithoutAssignment(Sungero.Workflow.IAssignment assignment)
      //    {
      //      var approvalTask = Sungero.Docflow.ApprovalTasks.As(assignment.Task);
      //      var performers = Sungero.Docflow.ApprovalAssignments.GetAll(x => Equals(x.Task, approvalTask) && x.IterationId == _obj.IterationId &&
      //                                                  x.BlockId == _obj.BlockId && x.TaskStartId == _obj.TaskStartId)
      //        .Select(x => x.Performer).Distinct().ToList();
      //      return GetCurrentIterationEmployees(approvalTask, assignment.Stage)
      //        .Where(x => !performers.Contains(x))
      //        .ToList();
      //    }
//
      //    private List<Sungero.Company.IEmployee> GetCurrentIterationEmployees(Sungero.Docflow.IApprovalTask task, Sungero.Docflow.IApprovalStage stage)
      //    {
      //      var result = new List<Sungero.Company.IEmployee>();
      //      var lastReworkAssignment = Functions.ApprovalTask.GetLastReworkAssignment(task);
      //      var approvers = Sungero.Docflow.PublicFunctions.ApprovalStage.Remote.GetStagePerformers(task, stage);
//
      //      // Исключить согласующих, если они уже подписали документ, либо в последнем задании на доработку было указано, что повторно не отправлять.
      //      foreach (var approver in approvers)
      //      {
      //        if (lastReworkAssignment == null ||
      //            lastReworkAssignment.Approvers.Any(a => Equals(a.Approver, approver) && a.Action == Sungero.Docflow.ApprovalReworkAssignmentApprovers.Action.SendForApproval) ||
      //            !lastReworkAssignment.Approvers.Any(a => Equals(a.Approver, approver)))
      //        {
      //          if (!Functions.ApprovalTask.HasValidSignature(task, approver))
      //            result.Add(approver);
      //        }
      //      }
//
      //      return result;
      //    }
  }
}