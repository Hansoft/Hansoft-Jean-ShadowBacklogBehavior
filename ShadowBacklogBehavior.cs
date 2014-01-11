using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;

using HPMSdk;

using Hansoft.Jean.Behavior;
using Hansoft.ObjectWrapper;

namespace Hansoft.Jean.Behavior.ShadowBacklogBehavior
{
    public class ShadowBacklogBehavior : AbstractBehavior
    {

        class ShadowedColumn
        {
            private bool isCustomColumn;
            private string customColumnName;
            private HPMProjectCustomColumnsColumn sourceCustomColumn;
            private HPMProjectCustomColumnsColumn shadowCustomColumn;

            private EHPMProjectDefaultColumn defaultColumnType;

            internal ShadowedColumn(bool isCustomColumn, string customColumnName, EHPMProjectDefaultColumn defaultColumnType)
            {
                this.isCustomColumn = isCustomColumn;
                this.customColumnName = customColumnName;
                this.defaultColumnType = defaultColumnType;
            }

            internal void Initialize(ProductBacklog sourceBacklog, ProductBacklog shadowBacklog)
            {
                if (isCustomColumn)
                {
                    sourceCustomColumn = sourceBacklog.GetCustomColumn(customColumnName);
                    if (sourceCustomColumn == null)
                        throw new ArgumentException("Could not find custom column:" + customColumnName + " in source backlog.");
                    shadowCustomColumn = shadowBacklog.GetCustomColumn(customColumnName);
                    if (shadowCustomColumn == null)
                        throw new ArgumentException("Could not find custom column:" + customColumnName + " in shadow backlog.");
                }
            }

            internal void DoUpdate(Task sourceTask, Task shadowTask)
            {
                if (isCustomColumn)
                {
                    shadowTask.SetCustomColumnValue(shadowCustomColumn, sourceTask.GetCustomColumnValue(sourceCustomColumn));
                }
                else
                    shadowTask.SetDefaultColumnValue(defaultColumnType, sourceTask.GetDefaultColumnValue(defaultColumnType));
            }
        }
        string sourceProjectName;
        Project sourceProject;
        ProductBacklog sourceBacklog;
        string shadowProjectName;
        Project shadowProject;
        ProductBacklog shadowBacklog;
        List<ShadowedColumn> shadowedColumns;
        string sourceDatabaseIDColumnName;
        HPMProjectCustomColumnsColumn sourceDatabaseIDColumn;
        bool changeImpact = false;
        bool initializationOK = false;
        string title;

        public ShadowBacklogBehavior(XmlElement configuration)
            : base(configuration)
        {
            sourceProjectName = GetParameter("SourceProject");
            shadowProjectName = GetParameter("ShadowProject");
            sourceDatabaseIDColumnName = GetParameter("SourceDataBaseIDColumn");
            shadowedColumns = GetShadowedColumns(configuration);
            title = "ShadowBacklogBehavior: " + configuration.InnerText;
        }

        // TODO: Subject to refactoring
        private List<ShadowedColumn> GetShadowedColumns(XmlElement parent)
        {
            List<ShadowedColumn> columnDefaults = new List<ShadowedColumn>();
            foreach (XmlNode node in parent.ChildNodes)
            {
                if (node is XmlElement)
                {
                    XmlElement el = (XmlElement)node;
                    switch (el.Name)
                    {
                        case ("CustomColumn"):
                            columnDefaults.Add(new ShadowedColumn(true, el.GetAttribute("Name"), EHPMProjectDefaultColumn.NewVersionOfSDKRequired));
                            break;
                        case ("Risk"):
                            columnDefaults.Add(new ShadowedColumn(false, null, EHPMProjectDefaultColumn.Risk));
                            break;
                        case ("Priority"):
                            columnDefaults.Add(new ShadowedColumn(false, null, EHPMProjectDefaultColumn.BacklogPriority));
                            break;
                        case ("EstimatedDays"):
                            columnDefaults.Add(new ShadowedColumn(false, null, EHPMProjectDefaultColumn.EstimatedIdealDays));
                            break;
                        case ("Category"):
                            columnDefaults.Add(new ShadowedColumn(false, null, EHPMProjectDefaultColumn.BacklogCategory));
                            break;
                        case ("Points"):
                            columnDefaults.Add(new ShadowedColumn(false, null, EHPMProjectDefaultColumn.ComplexityPoints));
                            break;
                        case ("Status"):
                            columnDefaults.Add(new ShadowedColumn(false, null, EHPMProjectDefaultColumn.ItemStatus));
                            break;
                        case ("Confidence"):
                            columnDefaults.Add(new ShadowedColumn(false, null, EHPMProjectDefaultColumn.Confidence));
                            break;
                        case ("Hyperlink"):
                            columnDefaults.Add(new ShadowedColumn(false, null, EHPMProjectDefaultColumn.Hyperlink));
                            break;
                        case ("Name"):
                            columnDefaults.Add(new ShadowedColumn(false, null, EHPMProjectDefaultColumn.ItemName));
                            break;
                        case ("WorkRemaining"):
                            columnDefaults.Add(new ShadowedColumn(false, null, EHPMProjectDefaultColumn.WorkRemaining));
                            break;
                        default:
                            throw new ArgumentException("Unknown column type specified in ShadowBacklogBehavior : " + el.Name);
                    }
                }
            }
            return columnDefaults;
        }

        public override void Initialize()
        {
            initializationOK = false;
            sourceProject = HPMUtilities.FindProject(sourceProjectName);
            if (sourceProject == null)
                throw new ArgumentException("Could not find source project:" + sourceProjectName);
            shadowProject = HPMUtilities.FindProject(shadowProjectName);
            if (shadowProject == null)
                throw new ArgumentException("Could not find source project:" + shadowProjectName);
            sourceBacklog = sourceProject.ProductBacklog;
            shadowBacklog = shadowProject.ProductBacklog;
            sourceDatabaseIDColumn = shadowProject.ProductBacklog.GetCustomColumn(sourceDatabaseIDColumnName);
            if (sourceDatabaseIDColumn == null)
                throw new ArgumentException("Could not find custom column in shadow product backlog:" + sourceDatabaseIDColumnName);
            foreach (ShadowedColumn shadowedColumn in shadowedColumns)
                shadowedColumn.Initialize(sourceBacklog, shadowBacklog);
            initializationOK = true;
            DoUpdate();
        }

        public override string Title
        {
            get { return title; }
        }

        private void DoUpdate()
        {
            if (initializationOK)
            {
                List<Task> sourceTasks = new List<Task>(sourceBacklog.DeepChildren.Cast<Task>());
                List<Task> shadowTasks = new List<Task>(shadowBacklog.DeepChildren.Cast<Task>());
                List<Task> deletedTasks = shadowTasks.FindAll(shadow => !sourceTasks.Exists(source => source.UniqueID.m_ID == shadow.GetCustomColumnValue(sourceDatabaseIDColumn).ToInt()));
                List<Task> addedTasks = sourceTasks.FindAll(source => !shadowTasks.Exists(shadow => shadow.GetCustomColumnValue(sourceDatabaseIDColumn).ToInt() == source.UniqueID.m_ID));

                // Delete tasks in the shadow project that don't exist in the source project any more
                foreach (ProductBacklogItem deletedTask in deletedTasks)
                    SessionManager.Session.TaskDelete(deletedTask.UniqueTaskID);

                // Create new tasks in the shadow project that have been added to the source project
                while (addedTasks.Count > 0)
                {
                    // Find a task that can be hooked in in the right place
                    Task addedTask = addedTasks.Find(added => added.Predecessor == null || shadowTasks.Exists(shadow => shadow.GetCustomColumnValue(sourceDatabaseIDColumn).ToInt() == added.Predecessor.UniqueID.m_ID));
                    if (addedTask == null) 
                        throw new InvalidOperationException("Could not find any predecessor for added tasks");
                    Task shadowPredecessor;
                    if (addedTask.Predecessor == null)
                        shadowPredecessor = null;
                    else
                        shadowPredecessor = shadowTasks.Find(shadow => shadow.GetCustomColumnValue(sourceDatabaseIDColumn).ToInt() ==  addedTask.Predecessor.UniqueID.m_ID);
                    HansoftItem shadowParent;
                    if (addedTask.Parent.Equals(sourceBacklog))
                        shadowParent = shadowBacklog;
                    else
                        shadowParent = shadowTasks.Find(shadow => shadow.GetCustomColumnValue(sourceDatabaseIDColumn).ToInt() ==  addedTask.Parent.UniqueID.m_ID);
                    HPMTaskCreateUnified createData = new HPMTaskCreateUnified();
                    createData.m_Tasks = new HPMTaskCreateUnifiedEntry[1];
                    createData.m_Tasks[0] = new HPMTaskCreateUnifiedEntry();
                    createData.m_Tasks[0].m_bIsProxy = false;
                    createData.m_Tasks[0].m_LocalID = 1;
                    createData.m_Tasks[0].m_NonProxy_WorkflowID = 0xffffffff;
                    createData.m_Tasks[0].m_ParentRefIDs = new HPMTaskCreateUnifiedReference[1];
                    createData.m_Tasks[0].m_ParentRefIDs[0] = new HPMTaskCreateUnifiedReference();
                    createData.m_Tasks[0].m_ParentRefIDs[0].m_RefID = shadowParent.UniqueID;
                    createData.m_Tasks[0].m_PreviousRefID = new HPMTaskCreateUnifiedReference();
                    if (shadowPredecessor != null)
                        createData.m_Tasks[0].m_PreviousRefID.m_RefID = shadowPredecessor.UniqueID;
                    else
                        createData.m_Tasks[0].m_PreviousRefID.m_RefID = -1;
                    createData.m_Tasks[0].m_PreviousWorkPrioRefID = new HPMTaskCreateUnifiedReference();
                    createData.m_Tasks[0].m_PreviousWorkPrioRefID.m_RefID = -2;
                    createData.m_Tasks[0].m_TaskLockedType = EHPMTaskLockedType.BacklogItem;
                    createData.m_Tasks[0].m_TaskType = EHPMTaskType.Planned;
                    HPMChangeCallbackData_TaskCreateUnified result = SessionManager.Session.TaskCreateUnifiedBlock(shadowBacklog.UniqueID, createData);
                    if (result.m_Tasks.Length != 1)
                        throw new InvalidOperationException("Incorrect number of tasks created");
                    SessionManager.Session.TaskSetFullyCreated(SessionManager.Session.TaskRefGetTask(result.m_Tasks[0].m_TaskRefID));
                    Task newTask = Task.GetTask(result.m_Tasks[0].m_TaskRefID);
                    newTask.SetCustomColumnValue(sourceDatabaseIDColumn, addedTask.UniqueID.m_ID);
                    shadowTasks.Add(newTask);
                    addedTasks.Remove(addedTask);
                }

                shadowTasks = new List<Task>(shadowBacklog.DeepChildren.Cast<Task>());
                
                // Traverse through all items and check if the parents and the previous items match
                bool dispositionOK = true;
                foreach(Task shadowTask in shadowTasks)
                {
                    HansoftItem shadowParent = shadowTask.Parent;
                    Task shadowPredecessor = shadowTask.Predecessor;
                    HPMUniqueID sourceID = new HPMUniqueID();
                    sourceID.m_ID = (int)shadowTask.GetCustomColumnValue(sourceDatabaseIDColumn).ToInt();
                    Task sourceTask = Task.GetTask(sourceID);
                    HansoftItem sourceParent = sourceTask.Parent;
                    Task sourcePredecessor = sourceTask.Predecessor;
                    if (! (((shadowParent is ProductBacklog && sourceParent is ProductBacklog) || (shadowParent is ProductBacklogItem && sourceParent is ProductBacklogItem && ((Task)shadowParent).GetCustomColumnValue(sourceDatabaseIDColumn).ToInt() == sourceParent.UniqueID.m_ID)) &&
                           ((shadowPredecessor == null && sourcePredecessor == null) || (shadowPredecessor != null && sourcePredecessor != null &&  ((Task)shadowPredecessor).GetCustomColumnValue(sourceDatabaseIDColumn).ToInt() == sourcePredecessor.UniqueID.m_ID))))
                    {
                        dispositionOK = false;
                        break;
                    }
                }

                // If there is a disposition mismatch, rebuild the whole disposition
                if (!dispositionOK)
                    SynchronizeDisposition();

                // Synchronize the columns
                foreach (Task shadowTask in shadowTasks)
                {
                    HPMUniqueID sourceID = new HPMUniqueID();
                    sourceID.m_ID = (int)shadowTask.GetCustomColumnValue(sourceDatabaseIDColumn).ToInt();
                    SynchronizeColumns(Task.GetTask(sourceID), shadowTask);
                }
            }
        }

        private List<HPMTaskChangeDispositionEntry> GetChangeEntries(List<Task> sourceTasksOnLevel, List<Task> shadowTasks, uint level)
        {
            List<HPMTaskChangeDispositionEntry> entries = new List<HPMTaskChangeDispositionEntry>();
            foreach (Task sourceTask in sourceTasksOnLevel)
            {
                HPMTaskChangeDispositionEntry changeEntry = new HPMTaskChangeDispositionEntry();
                changeEntry.m_ChangeFlags = EHPMTaskChangeDispositionEntryChangeFlag.PreviousRefID | EHPMTaskChangeDispositionEntryChangeFlag.TreeLevel;
                Task shadowTask = shadowTasks.Find(shadow => shadow.GetCustomColumnValue(sourceDatabaseIDColumn).ToInt() == sourceTask.UniqueID.m_ID);
                if (sourceTask.Predecessor != null)
                {
                    Task shadowPredecessor = shadowTasks.Find(shadow => shadow.GetCustomColumnValue(sourceDatabaseIDColumn).ToInt() == sourceTask.Predecessor.UniqueID.m_ID);
                    changeEntry.m_PreviousRefID = shadowPredecessor.UniqueID;
                }
                else
                    changeEntry.m_PreviousRefID = -1;
                changeEntry.m_TreeLevel = level;
                changeEntry.m_TaskRefID = shadowTask.UniqueID;
                entries.Add(changeEntry);
                if (sourceTask.HasChildren)
                    entries.AddRange(GetChangeEntries(new List<Task>(sourceTask.Children.Cast<Task>()), shadowTasks, level + 1));
            }
            return entries;
        }

        private void SynchronizeDisposition()
        {

            List<HPMTaskChangeDispositionEntry> changeEntries = GetChangeEntries(new List<Task>(sourceBacklog.Children.Cast<Task>()), new List<Task>(shadowBacklog.DeepChildren.Cast<Task>()), 0);
            // Fix disposition on this level
            HPMTaskChangeDispositionEntry[] changeEntriesArray = changeEntries.ToArray();
            HPMTaskChangeDisposition dispositionChange = new HPMTaskChangeDisposition();
            dispositionChange.m_OptionFlags = EHPMTaskChangeDispositionOptionFlag.None;
            dispositionChange.m_TasksToChange = changeEntriesArray;
            HPMChangeCallbackData_TaskChangeDisposition result =  SessionManager.Session.TaskChangeDispositionBlock(shadowBacklog.UniqueID, dispositionChange);
            if (result.m_bDispositionChangedRejected)
                throw new Exception("Could not change the disposition of the shadow project");
        }

        private void SynchronizeColumns(Task sourceTask, Task shadowTask)
        {
            foreach (ShadowedColumn shadowedColumn in shadowedColumns)
                shadowedColumn.DoUpdate(sourceTask, shadowTask);
        }

        public override void OnBeginProcessBufferedEvents(EventArgs e)
        {
            changeImpact = false;
        }

        public override void OnEndProcessBufferedEvents(EventArgs e)
        {
            if (BufferedEvents && changeImpact)
                DoUpdate();
        }


        public override void OnTaskChange(TaskChangeEventArgs e)
        {
            if (!e.Data.m_bChangeInitiatedFromThisSession)
            {
                if (!BufferedEvents)
                    DoUpdate();
                else
                    changeImpact = true;
            }
        }

        public override void OnTaskChangeCustomColumnData(TaskChangeCustomColumnDataEventArgs e)
        {
            if (!e.Data.m_bChangeInitiatedFromThisSession)
            {
                if (!BufferedEvents)
                    DoUpdate();
                else
                    changeImpact = true;
            }
        }

        public override void OnTaskCreate(TaskCreateEventArgs e)
        {
            if (!e.Data.m_bChangeInitiatedFromThisSession)
            {
                if (!BufferedEvents)
                    DoUpdate();
                else
                    changeImpact = true;
            }
        }

        public override void OnTaskDelete(TaskDeleteEventArgs e)
        {
            if (!e.Data.m_bChangeInitiatedFromThisSession)
            {
                if (!BufferedEvents)
                    DoUpdate();
                else
                    changeImpact = true;
            }
        }

        public override void OnTaskMove(TaskMoveEventArgs e)
        {
            if (!e.Data.m_bChangeInitiatedFromThisSession)
            {
                if (!BufferedEvents)
                    DoUpdate();
                else
                    changeImpact = true;
            }
        }
    }
}
