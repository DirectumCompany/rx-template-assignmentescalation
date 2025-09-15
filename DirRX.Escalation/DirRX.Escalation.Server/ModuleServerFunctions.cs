using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;

namespace DirRX.Escalation.Server
{
  public partial class ModuleFunctions
  {
    /// <summary>
    /// Получить задачу на продление срока по заданию.
    /// </summary>
    /// <param name="assignment">Задание.</param>
    /// <returns>Задача, на основе которой создано задание.</returns>
    private Sungero.Docflow.IDeadlineExtensionTask GetDeadlineExtension(Sungero.Workflow.IAssignment assignment)
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
    public bool IsUserDepartmentManager(Sungero.CoreEntities.IUser user)
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
    /// <param name="isManager">Адресат - это менеджер.</param>
    /// <param name="addressee">Адресат уведомления.</param>
    /// <returns>Идентификатор уведомления.</returns>
    private void SendEscalationNotification(Sungero.Workflow.IAssignment assignment, Sungero.CoreEntities.IUser addressee, bool isManager)
    {
      var performer = Sungero.Company.Employees.GetAll(e => Equals(addressee, e)).SingleOrDefault();
      var text = string.Empty;
      var subject = string.Empty;
      
      // Если получатель - руководитель.
      if (isManager)
      {
        // То формирует сообщения для руководителя.
        text = Resources.ManagerNotificationTextFormat(Hyperlinks.Get(assignment));
        subject = Resources.ManagerNotificationSubjectFormat(assignment.Subject);
      }
      else
      {
        // Если нет, то формруем сообщение для исполнителя задания.
        text = Resources.FirstPerformerNotificationTextFormat(Hyperlinks.Get(assignment));
        subject = Resources.FirstPerformerNotificationSubjectFormat(assignment.Subject);
      }
      
      // Отправляем уведомление.
      if (performer != null)
      {
        try
        {
          var notice = Sungero.Workflow.SimpleTasks.CreateWithNotices(subject, performer);
          notice.ActiveText = text;
          notice.Attachments.Add(assignment);
          notice.Start();
          
          if (isManager)
            Logger.Debug(string.Format("Send esclation notification (Id={0}) to manager (Id={1}) about assignment (Id={2})", notice.Id, addressee.Id, assignment.Id));
          else
            Logger.Debug(string.Format("Send esclation notification (Id={0}) to employee (Id={1}) about assignment (Id={2})", notice.Id, addressee.Id, assignment.Id));
        }
        catch (Exception ex)
        {
          Logger.Error(string.Format("Error on sending esclation notification to user (Id={0}) for assignment (Id={1})", assignment.Performer.Id, assignment.Id), ex);
          return;
        } 
      }
    }
    
    /// <summary>
    /// Получить задания на эскалацию.
    /// </summary>
    /// <returns>Список заданий на эскалацию.</returns>
    public virtual List<Sungero.Workflow.IAssignment> GetEscalationAssignments()
    {
      return Sungero.Docflow.ApprovalAssignments.GetAll()
        .Where(x => x.Status == Sungero.Workflow.Assignment.Status.InProcess)
        .Where(x => x.Deadline < Calendar.Now)
        .Select(x => Sungero.Workflow.Assignments.As(x))
        .ToList();
    }
    
    /// <summary>
    /// Эскалация заданий на согласование.
    /// </summary>
    public void AssignmentsEscalation()
    {
      var assignments = new List<Sungero.Workflow.IAssignment>();
      try
      {
        assignments = GetEscalationAssignments();
        Logger.Debug(string.Format("Got {0} assignments for escalation", assignments.Count));
      }
      catch (Exception ex)
      {
        Logger.Error("Can not Get escalation assignments for escalation",ex);
      }
      
      foreach (var assignment in assignments)
      {
        // Получаем задания на продление срока у задания.
        var deadlineExtension = GetDeadlineExtension(assignment);
        
        // Если задание на продление срока в работе, то такие задания не эскалируются.
        if (deadlineExtension != null && deadlineExtension.Status == Sungero.Workflow.Task.Status.InProcess)
          continue;
        
        // Следует ли отправлять уведомление о задержке менеджерам.
        var shouldNotifyManagers = ShouldSendEscalationNotificationToManager(assignment);
        
        // Если задание на продление срока - не стратовано, то отменяем завершаем ее.
        // Это сделано для того, чтобы не было конфликтов в дальнейшем.
        try
        {
          if (deadlineExtension != null && deadlineExtension.Status == Sungero.Workflow.Task.Status.Draft)
          {
            deadlineExtension.ActiveText = Resources.ApprovalPeriodExpired;
            deadlineExtension.Abort();
          }
        }
        catch (Exception ex)
        {
          Logger.Error(string.Format("Error on aborting DeadlineExtensionAssignment (Id = {0}) for assignment (Id = {1})", deadlineExtension.Id, assignment.Id), ex);
        }
        // Если исполнитель задания - руководитель подразделения
        if (Functions.Module.IsUserDepartmentManager(assignment.Performer))
        {
          //То отправляем только уведомление, если нужно отправлять уведомления менеджерам.
          if (shouldNotifyManagers)
          {
            SendEscalationNotification(assignment, GetManager(assignment.Performer), true);
            continue;
          }
        }
        
        // Получаем руководителя исполнителя.
        var manager = GetManager(assignment.Performer);
        if (manager != null)
        {
          // Если руководитель участвует в процессе эскалации
          // (входит в роль "Руководители, не участвующие в процессе эскалации").
          if (manager.IncludedIn(Constants.Module.ManagersWithoutEscalations))
          {
            // То отправляем уведомление о задержке, если нужно.
            if (shouldNotifyManagers)
              SendEscalationNotification(assignment, GetManager(assignment.Performer), true);
            continue;
          }
          
          assignment.ActiveText = Resources.ApprovalPeriodExpired;
          var previousPerformer = assignment.Performer;
          
          // Переадресуем задание руководителю.
          if (ForwardAssignemnt(assignment, manager))
            // Если задание было переадресовано, то уведомляет прерыдущего исполнителя.
            SendEscalationNotification(assignment, previousPerformer, false);
          
        }
      }
    }
    
    /// <summary>
    /// Переадресовать задание.
    /// </summary>
    /// <param name="assignment">Задание на переадресацию.</param>
    /// <param name="addressee">Работник, кому переадресовать задание.</param>
    /// <returns>True - если задание было переадресовано.</returns>
    public virtual bool ForwardAssignemnt(Sungero.Workflow.IAssignment assignment, Sungero.Company.IEmployee addressee)
    {
      if (CanForward(assignment, addressee))
      {
        if (Sungero.Docflow.ApprovalAssignments.Is(assignment))
        {
          var approvalAssign = Sungero.Docflow.ApprovalAssignments.As(assignment);
          try
          {
            approvalAssign.Addressee = addressee;
            assignment.Complete(Sungero.Docflow.ApprovalAssignment.Result.Forward);
          }
          catch(Exception ex)
          {
            Logger.Error(string.Format("Error on forwarding assignment (Id = {0}) to user (Id = {1})", assignment.Id, addressee.Id), ex);
            return false;
          }
          return true;
        }
      }
      return false;
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
    /// Нужно ли оправить уведомление о задежке задания руководителю.
    /// </summary>
    /// <param name="assignment">Задание.</param>
    /// <param name="manager">Руководитель.</param>
    /// <returns></returns>
    public virtual bool ShouldSendEscalationNotificationToManager(Sungero.Workflow.IAssignment assignment)
    {
      return true;
    }
    
    /// <summary>
    /// Можно ли переадресовать задание сотруднику.
    /// </summary>
    /// <param name="assignment">Задание на переадресацию.</param>
    /// <param name="employee">Работник, кому переадресуется задание.</param>
    /// <returns></returns>
    public virtual bool CanForward(Sungero.Workflow.IAssignment assignment, Sungero.Company.IEmployee employee)
    {
      return true;
    }
  }
}