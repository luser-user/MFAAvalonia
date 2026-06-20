using MFAAvalonia.Helper.ValueType;
using MFAAvalonia.ViewModels.UsersControls.Settings;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MFAAvalonia.Extensions.MaaFW;

internal enum MultiRoleLoopValidationError
{
    None,
    MultipleMarkers,
    MissingSwitchTask,
    MultipleSwitchTasks,
    EmptyLoopBody,
    InvalidCount,
}

internal sealed record MultiRoleLoopPlannedTask(
    DragItemViewModel Task,
    int? LoopRound = null,
    int? LoopTotal = null,
    bool IsLoopRoundStart = false);

internal sealed class MultiRoleLoopPlan
{
    public bool IsValid => ValidationError == MultiRoleLoopValidationError.None;
    public bool IsLoopEnabled { get; init; }
    public MultiRoleLoopValidationError ValidationError { get; init; }
    public IReadOnlyList<MultiRoleLoopPlannedTask> Tasks { get; init; } = [];
    public IReadOnlyList<DragItemViewModel> InvalidTasks { get; init; } = [];
}

internal static class MultiRoleLoopPlanner
{
    internal const string SwitchAccountTaskName = "切换账号";

    public static MultiRoleLoopPlan Create(IReadOnlyList<DragItemViewModel> tasks, bool enableLoop)
    {
        var taskList = tasks.ToList();
        if (!enableLoop)
        {
            return CreatePassThrough(taskList.Where(task => !AddTaskDialogViewModel.IsMultiRoleLoopTask(task)));
        }

        var markers = taskList.Where(AddTaskDialogViewModel.IsMultiRoleLoopTask).ToList();
        if (markers.Count == 0)
        {
            return CreatePassThrough(taskList);
        }

        if (markers.Count > 1)
        {
            return CreateInvalid(MultiRoleLoopValidationError.MultipleMarkers, markers);
        }

        var marker = markers[0];
        if (!TryGetLoopCount(marker, out var loopCount) || loopCount is < 1 or > 8)
        {
            return CreateInvalid(MultiRoleLoopValidationError.InvalidCount, [marker]);
        }

        var markerIndex = taskList.IndexOf(marker);
        var switchTasks = taskList
            .Skip(markerIndex + 1)
            .Where(IsSwitchAccountTask)
            .ToList();

        if (switchTasks.Count == 0)
        {
            return CreateInvalid(MultiRoleLoopValidationError.MissingSwitchTask, [marker]);
        }

        if (switchTasks.Count > 1)
        {
            return CreateInvalid(MultiRoleLoopValidationError.MultipleSwitchTasks, switchTasks);
        }

        var switchTask = switchTasks[0];
        var switchIndex = taskList.IndexOf(switchTask);
        var loopBody = taskList.Skip(markerIndex + 1).Take(switchIndex - markerIndex - 1).ToList();
        if (loopBody.Count == 0)
        {
            return CreateInvalid(MultiRoleLoopValidationError.EmptyLoopBody, [marker, switchTask]);
        }

        var plannedTasks = new List<MultiRoleLoopPlannedTask>();
        plannedTasks.AddRange(taskList.Take(markerIndex).Select(task => new MultiRoleLoopPlannedTask(task)));

        for (var round = 1; round <= loopCount; round++)
        {
            for (var index = 0; index < loopBody.Count; index++)
            {
                plannedTasks.Add(new MultiRoleLoopPlannedTask(
                    loopBody[index],
                    round,
                    loopCount,
                    IsLoopRoundStart: index == 0));
            }

            if (round < loopCount)
            {
                plannedTasks.Add(new MultiRoleLoopPlannedTask(switchTask, round, loopCount));
            }
        }

        plannedTasks.AddRange(taskList.Skip(switchIndex + 1).Select(task => new MultiRoleLoopPlannedTask(task)));

        return new MultiRoleLoopPlan
        {
            IsLoopEnabled = true,
            Tasks = plannedTasks,
        };
    }

    public static bool IsSwitchAccountTask(DragItemViewModel task)
    {
        return string.Equals(
            task.InterfaceItem?.Name,
            SwitchAccountTaskName,
            StringComparison.Ordinal);
    }

    private static MultiRoleLoopPlan CreatePassThrough(IEnumerable<DragItemViewModel> tasks)
    {
        return new MultiRoleLoopPlan
        {
            Tasks = tasks.Select(task => new MultiRoleLoopPlannedTask(task)).ToList(),
        };
    }

    private static MultiRoleLoopPlan CreateInvalid(
        MultiRoleLoopValidationError error,
        IReadOnlyList<DragItemViewModel> invalidTasks)
    {
        return new MultiRoleLoopPlan
        {
            ValidationError = error,
            InvalidTasks = invalidTasks,
        };
    }

    private static bool TryGetLoopCount(DragItemViewModel marker, out int count)
    {
        count = 1;
        var entry = marker.InterfaceItem?.Entry;
        var pipelineOverride = marker.InterfaceItem?.PipelineOverride;
        if (string.IsNullOrEmpty(entry) || pipelineOverride == null)
        {
            return true;
        }

        if (!pipelineOverride.TryGetValue(entry, out var node) || node is not JObject nodeObject)
        {
            return true;
        }

        var paramToken = nodeObject["custom_action_param"];
        JObject? param = paramToken as JObject;
        if (param == null && paramToken is JValue { Type: JTokenType.String } stringValue)
        {
            try
            {
                param = JObject.Parse((string)stringValue!);
            }
            catch
            {
                return false;
            }
        }
        else if (param == null && paramToken != null)
        {
            return false;
        }

        var countToken = param?["count"];
        return countToken == null || int.TryParse(countToken.ToString(), out count);
    }
}
